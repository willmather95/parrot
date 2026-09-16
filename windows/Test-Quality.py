"""Small, reproducible ASR regression gate, not a general accuracy benchmark.

Runs only against explicit public test audio. No microphone access. Python is
needed on the CI/build machine only, never on the user's Windows laptop.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import random
import re
import struct
import subprocess
import time
import urllib.request
import wave


def words(text):
    return re.findall(r"[a-z0-9]+(?:'[a-z0-9]+)?", text.lower().replace("\u2019", "'"))


def edit_distance(reference, hypothesis):
    previous = list(range(len(hypothesis) + 1))
    for row, expected in enumerate(reference, 1):
        current = [row]
        for column, actual in enumerate(hypothesis, 1):
            current.append(min(current[-1] + 1, previous[column] + 1,
                               previous[column - 1] + (expected != actual)))
        previous = current
    return previous[-1]


def fetch_fixture(fixture, destination):
    expected = fixture['sha256'].lower()
    if not re.fullmatch(r'[a-f0-9]{64}', expected):
        raise ValueError('Invalid fixture SHA-256')
    if not fixture['url'].startswith('https://'):
        raise ValueError('Fixtures must use HTTPS')
    if not destination.exists() or hashlib.sha256(destination.read_bytes()).hexdigest() != expected:
        for attempt in range(3):
            try:
                with urllib.request.urlopen(fixture['url'], timeout=45) as response:
                    data = response.read(10 * 1024 * 1024 + 1)
                if len(data) > 10 * 1024 * 1024:
                    raise ValueError('Fixture exceeds 10 MiB limit')
                if hashlib.sha256(data).hexdigest() != expected:
                    raise ValueError('Fixture checksum mismatch: ' + fixture['id'])
                destination.write_bytes(data)
                break
            except (OSError, TimeoutError):
                if attempt == 2:
                    raise
                time.sleep(2 ** attempt)
    return destination


def non_speech(path, noise=False):
    generator = random.Random(20260916)
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(16000)
        output.writeframes(b''.join(struct.pack('<h', generator.randint(-160, 160) if noise else 0)
                                    for _ in range(3 * 16000)))


def repeat_with_pauses(source, destination):
    with wave.open(str(source), 'rb') as original:
        parameters = original.getparams()
        if parameters.nchannels != 1 or parameters.sampwidth != 2:
            raise ValueError('Long-form fixture must be mono PCM16')
        audio = original.readframes(original.getnframes())
    with wave.open(str(destination), 'wb') as output:
        output.setparams(parameters)
        output.writeframes((audio + b'\x00\x00' * parameters.framerate) * 3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--manifest', type=Path,
                        default=Path(__file__).parent / 'quality' / 'fixtures.json')
    parser.add_argument('--work', type=Path,
                        default=Path(__file__).parent / 'build' / 'quality')
    options = parser.parse_args()
    options.work.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(options.manifest.read_text(encoding='utf-8'))
    fixtures = manifest['fixtures']
    if not fixtures:
        raise ValueError('At least one speech reference is required')
    cases = []
    for fixture in fixtures:
        if not re.fullmatch(r'[a-zA-Z0-9_-]+', fixture['id']):
            raise ValueError('Invalid fixture identifier')
        cases.append((fixture, fetch_fixture(fixture, options.work / (fixture['id'] + '.wav'))))
    # Exercise the same segmentation path used by a longer dictation, including
    # a pause between complete utterances. The repeated reference is exact.
    source_fixture, source_audio = cases[-1]
    long_audio = options.work / 'long-with-pauses.wav'
    repeat_with_pauses(source_audio, long_audio)
    cases.append((dict(source_fixture, id='long-with-pauses',
                       reference=' '.join([source_fixture['reference']] * 3)), long_audio))
    for identifier, noise in [('silence', False), ('quiet-noise', True)]:
        audio = options.work / (identifier + '.wav')
        non_speech(audio, noise)
        cases.append(({'id': identifier, 'reference': '', 'max_wer': 0}, audio))
    results = []
    failures = []
    for fixture, audio in cases:
        output = (options.work / (fixture['id'] + '.json')).resolve()
        if output.exists():
            output.unlink()  # This harness owns only its exact prior result file.
        started = time.monotonic()
        completed = subprocess.run([str(options.exe.resolve()), '--transcribe-file',
                                    str(audio.resolve()), '--output-json', str(output)],
                                   timeout=180, check=False)
        elapsed = time.monotonic() - started
        if completed.returncode != 0 or not output.exists():
            raise RuntimeError('Inference failed for ' + fixture['id'])
        result = json.loads(output.read_text(encoding='utf-8-sig'))
        if not isinstance(result.get('text'), str):
            raise ValueError('Inference result has no text')
        for name in ['audio_seconds', 'load_seconds', 'decode_seconds']:
            if not isinstance(result.get(name), (int, float)) or not math.isfinite(result[name]) or result[name] < 0:
                raise ValueError('Missing or invalid inference timing: ' + name)
        expected, actual = words(fixture['reference']), words(result['text'])
        errors = edit_distance(expected, actual)
        wer = errors / len(expected) if expected else (0.0 if not actual else 1.0)
        passed = (math.isfinite(wer) and wer <= fixture['max_wer']) if expected else not result['text'].strip()
        record = dict(result, id=fixture['id'], reference=fixture['reference'],
                      words=len(expected), errors=errors, wer=wer,
                      realtime_factor=result['decode_seconds'] / max(result['audio_seconds'], 0.001),
                      wall_seconds=round(elapsed, 3), passed=passed)
        results.append(record)
        print('{} {}: WER={:.1%}, wall={:.2f}s'.format(
            'PASS' if passed else 'FAIL', fixture['id'], wer, elapsed), flush=True)
        if not passed:
            failures.append(fixture['id'])
    total_words = sum(result['words'] for result in results)
    total_errors = sum(result['errors'] for result in results if result['words'])
    base_ids = {fixture['id'] for fixture in fixtures}
    base_results = [result for result in results if result['id'] in base_ids]
    base_words = sum(result['words'] for result in base_results)
    receipt = {'scope': 'Small public-speech regression suite; not work-laptop acceptance',
               'commit': os.environ.get('GITHUB_SHA'),
               'exe_sha256': hashlib.sha256(options.exe.read_bytes()).hexdigest(),
               'fixture_manifest_sha256': hashlib.sha256(options.manifest.read_bytes()).hexdigest(),
               'aggregate_wer': total_errors / total_words,
               'base_speech_wer': sum(result['errors'] for result in base_results) / base_words,
               'base_speech_words': base_words,
               'reference_words': total_words, 'results': results,
               'passed': not failures}
    (options.work / 'quality-results.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    if failures:
        raise SystemExit('Quality gate failed: ' + ', '.join(failures))


if __name__ == '__main__':
    main()
