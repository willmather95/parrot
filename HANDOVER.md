# Parrot upgrade candidate, 2026-09-16

## User outcome

Update willmather95/parrot while preserving the working Mac installation. Add a
Windows version that can run/install under a standard user account. User also
confirmed disabling HDR on the external LG fixed washed-out captures; that
display issue needs no code change.

## Baseline and canonical source

Canonical checkout: /Users/willmather/projects/parrot.
Base revision: 2cae942f9ad670b1b468fc86b6a7807bbcf1fe7d, public v0.1.4.
The older clean checkout under the August 27 task was left untouched.

The installed /Applications/Parrot.app executable and the executable extracted
from the public v0.1.4 release both hash to:
6407dc88f0e10b573703f9a5734578639d42d3bb440304c6f1d1fc318908211a.
The installed executable hash was rechecked after local work and is unchanged.
No installed app, LaunchAgent, permission identity, or model payload was replaced.

## Candidate

- Mac source version 0.1.5/build 6: opt-in --user installation, supported-bundle
  path resolution, conflict refusal, v0.1.5 compatibility guard, and stronger
  speech-fixture verification.
- Windows native C#/.NET Framework preview: 0.1.0 used installed System.Speech.
  The user requested a quality upgrade on September 16; 0.2.0 replaces that
  backend with bundled Parakeet TDT v2 int8 through sherpa-onnx. Preserve
  Ctrl+Alt+Space, visible status, cancellation, protected-focus checks, clipboard
  only, and the asInvoker manifest. Work-laptop accuracy remains unverified.
- Windows build/package and per-user install scripts, checksum validation,
  rollback tests, and a checks workflow that uploads a preview artifact.
- Documentation describes unsigned/managed-device and real-runtime boundaries.

## Verification

- Baseline Swift tests: 41 passed.
- Candidate `swift test --disable-automatic-resolution`: 50 passed.
- `swift build -c release --disable-automatic-resolution`: passed.
- Candidate real Parakeet generated-speech smoke: passed including phrase match.
- Shell syntax, ShellCheck, installer guard tests, XML/YAML parsing, targeted
  credential-pattern scan, and git diff whitespace checks passed.
- Package.resolved unchanged.
- Independent Fresh Eyes/Boris reviews found issues; fixes and targeted review
  are recorded in the current task. Final targeted reviews found no remaining
  static P0/P1 findings.
- Windows and macOS CI passed on 019bd5c337e9b10970e75dae4aff4c4692ca91c5:
  https://github.com/willmather95/parrot/actions/runs/35037410476.
- Windows compiled with the in-box Framework compiler after making the focus
  task's generic return type explicit. Nine deterministic tests passed; package,
  user-folder installation, update, forced rollback, and corruption-rejection
  checks passed. These ran under runneradmin, not a managed standard account.
- Downloaded artifact 10423223959 and verified its inner ZIP and payload hashes,
  plus the embedded asInvoker/uiAccess=false manifest. ZIP SHA-256:
  654a1f27faa25c49d1263561b2565e7da784fc3ab7419db9a7f36aa5e2a8134a.

Detailed command logs are in the current task's work/ directory. Local model
smoke generated synthetic speech only. No central memory or shared operational
system was updated.

## Remaining gates

1. Final targeted native Windows review completed with no remaining static P0/P1.
2. User approved committing and pushing the reviewed candidate on 2026-09-15.
   Git identity is Bill Mather / will.mather@mathermediasolutions.com. The approved
   branch is codex/parrot-windows-preview; a public release or installed Mac
   replacement remains outside this approval.
3. Branch pushed and both CI jobs passed. The verified Windows preview artifact
   is available from the run above; CI does not prove work-device acceptance.
4. Test real microphone, hotkey, clipboard, secure focus, cancel, repeated use,
   DPI/Narrator, sleep/resume, and install/update on a standard Windows account.
5. User explicitly requested the local transcription-quality upgrade on
   September 16. The bundled modern model/runtime candidate is in progress.
   The earlier CI run and artifact above verify 0.1.0 only; they do not certify
   the new backend. New Windows inference and package checks are required.
6. No valid Mac code-signing identities were found locally. Developer ID and
   Windows Authenticode signing need credentials and acceptance verification.
7. Publish a labeled Windows prerelease only with release authority and after
   verification. Keep stable Mac release selection separate. Preserve Mac
   TCC identity/rollback through any later installed upgrade.

## Next action

Finish the Windows 0.2.0 neural backend, run the declared public-speech quality
suite and installer checks on Windows CI, review the integrated candidate, and
deliver the newly verified artifact. Then test a harmless dictation and manual
Ctrl+V on the ThinkPad. Do not silently upgrade the daily-running Mac app or
claim Windows/Mac recognition-quality parity from model-family similarity.
