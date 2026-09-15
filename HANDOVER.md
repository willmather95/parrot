# Parrot upgrade candidate, 2026-09-15

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
- Windows native C#/.NET Framework preview: local installed System.Speech,
  Ctrl+Alt+Space, visible status, cancellation, protected-focus checks, clipboard
  only, asInvoker manifest. This is not Parakeet quality parity.
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
  static P0/P1 findings. No Windows compile/runtime success is claimed.

Detailed command logs are in the current task's work/ directory. Local model
smoke generated synthetic speech only. No central memory or shared operational
system was updated.

## Remaining gates

1. Final targeted native Windows review completed with no remaining static P0/P1.
2. User approved committing and pushing the reviewed candidate on 2026-09-15.
   Git identity is Bill Mather / will.mather@mathermediasolutions.com. The approved
   branch is codex/parrot-windows-preview; a public release or installed Mac
   replacement remains outside this approval.
3. Run the Windows checks job after the source is pushed. Fix compilation/test
   failures before distributing the preview. CI uses a disposable runner and
   does not prove standard-user policy acceptance.
4. Test real microphone, hotkey, clipboard, secure focus, cancel, repeated use,
   DPI/Narrator, sleep/resume, and install/update on a standard Windows account.
5. A Windows-compatible modern local model/runtime requires dependency/download
   approval, currently requested but not received. Built-in speech remains the
   preview backend.
6. No valid Mac code-signing identities were found locally. Developer ID and
   Windows Authenticode signing need credentials and acceptance verification.
7. Publish a labeled Windows prerelease only with release authority and after
   verification. Keep stable Mac release selection separate. Preserve Mac
   TCC identity/rollback through any later installed upgrade.

## Next action

Commit/push the approved file set and run Windows CI to produce a reviewable
downloadable preview. Do not silently upgrade the daily-running Mac app or
claim Windows/Mac recognition-quality parity.
