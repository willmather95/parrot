# Windows transcription quality decision

## Goal

Replace the Windows 0.1.0 legacy recognizer with modern local English dictation
that can run on a conventional Lenovo ThinkPad without administrator rights,
GPU drivers, a cloud account, or audio uploads.

## Selected candidate

Parakeet TDT 0.6B v2, official sherpa-onnx int8 ONNX conversion, sherpa-onnx
1.13.8 CPU runtime, two inference threads. This is the same underlying model
family as the working Mac app, with a different runtime and quantization.
The portable package bundles the runtime and model. Int8 is a deliberate
CPU/memory/download tradeoff, not a claim of mathematically identical output
to full-precision or the Mac's Core ML model.

The NVIDIA v2 model card reports a 6.05% average word error rate over its listed
English benchmark sets. The v3 multilingual card reports 6.34% on its English
benchmark table, so a higher version number alone is not a reason to change an
English dictation default. These are publisher measurements of their evaluation
pipeline, not results measured in this app or on Will's laptop.

The newer unified English model also merits future comparison: NVIDIA reports
5.91% versus 6.04% for v2 in a shared evaluation. It uses an RNNT decoder and
adds streaming flexibility. We have not measured its CPU latency or quantized
accuracy in this application. This candidate establishes a tested Parakeet
baseline; it does not claim to beat every available speech model.

## Acceptance evidence

The CI quality harness runs the packaged executable against pinned, attributed
public speech fixtures. It reports normalized word error rate, model load time,
decode time, and total wall time. It also checks silence and quiet noise. Each
fixture declares its error threshold before measurement. Case/punctuation are
ignored for word scoring, so that metric alone does not validate formatting.

This small suite detects broken model wiring, missing native dependencies,
empty or nonsensical recognition, and non-speech regressions. It is not a
representative benchmark for accents, specialist vocabulary, numbers, noisy
rooms, or meeting audio. It does not establish a numerical improvement over
the legacy engine because that engine has not been measured on the same host.

Live microphone capture, permissions, cancellation, sleep/resume, and practical
latency still require a standard-user Windows test. Quality acceptance for Will
should include his actual work vocabulary and names. Never upload his work
recordings for testing without explicit direction.

Native inference is not forcibly interrupted in this in-process design. Cancel
and the deadline invalidate delivery immediately and prevent subsequent chunks,
but must wait for the current chunk's native call to return. A hung native call
requires quitting/reopening Parrot; new capture remains blocked rather than
overlapping model operations. Process isolation is a possible later improvement.

## Sources

- [NVIDIA Parakeet TDT v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2)
- [NVIDIA Parakeet TDT v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)
- [NVIDIA unified English](https://huggingface.co/nvidia/parakeet-unified-en-0.6b)
- [sherpa-onnx NeMo models](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/offline-transducer/nemo-transducer-models.html)

Exact artifact URLs, hashes, versions, and license notices are recorded in the
Windows dependency manifest and bundled notices. The running Mac installation
is outside the scope of this Windows backend change.
