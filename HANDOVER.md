# Parrot upgrade handoff, 2026-09-16

## Outcome and authority

Canonical source: `/Users/willmather/projects/parrot`.
Branch: `codex/parrot-windows-preview` in `willmather95/parrot`.
User approved in-scope commits/pushes, then explicitly requested better Windows
transcription quality. The Windows 0.2.0 candidate now uses local Parakeet TDT
0.6B v2 int8 through sherpa-onnx 1.13.8, replacing legacy System.Speech.

The working Mac installation is protected and unchanged. Its executable was
rechecked at SHA-256
`6407dc88f0e10b573703f9a5734578639d42d3bb440304c6f1d1fc318908211a`.
No installed app, LaunchAgent, model payload, or signing identity was replaced.
The source Mac candidate remains v0.1.5/build 6 with opt-in user installation;
public stable release remains v0.1.4. No merge or public release was performed.

User confirmed disabling HDR fixed the LG screenshot/Teams washout. No further
display work is pending in this task.

## Windows candidate

- Portable Windows 11 x64 executable and optional per-user installer.
- Pinned app-local native/managed runtime and model, CPU-only, two threads;
  no extra installed SDK, VC++ redistributable, GPU driver, or speech language
  pack is needed by this package. Employer policy acceptance is unverified.
- In-memory 16 kHz microphone capture, FIFO buffer handling, bounded recordings,
  quiet-boundary segmentation, asynchronous inference, and stale/cancel guards.
- Global shortcut, visible recording/cancellation status, protected-focus
  checks, clipboard-only delivery. No automatic paste or cloud transcription.
- Recursive checksums, validated old-preview upgrade, transactional rollback,
  exact dependency sizes/hashes, and license/attribution notices.
- Normal dictation saves no audio/transcript history. Explicit benchmark CLI
  writes only the caller-requested JSON result from a supplied test WAV.

## Final verification and delivery

Tested source: `9176eed5de4924ef7173c08150010dfcc882d954`.
CI: https://github.com/willmather95/parrot/actions/runs/35105457141
Windows artifact: https://github.com/willmather95/parrot/actions/runs/35105457141/artifacts/10450437152
Artifacts expire after 14 days and may require GitHub login.

- Windows compile with warnings as errors: passed.
- 17 deterministic tests: passed.
- Package/install/update/schema migration/corruption/forced rollback: passed.
- Three human clips: 3/89 word-token differences; all declared gates passed.
- 53.145-second derived recording: 0% WER, three chunks, 4.52s decode.
- Silence/quiet noise: empty results. Peak working set about 947 MiB on CI.
- Mac CI regression checks passed. Prior candidate local checks also passed:
  50 Swift tests, release build, phrase-matched Parakeet smoke, installer tests.
- Independent Boris and Fresh Eyes review: no P0/P1; native mid-chunk
  cancellation remains a documented P2 limitation.
- Downloaded final ZIP, verified all 15 payload files and dependency pins, and
  matched its executable hash to the quality receipt. asInvoker/uiAccess=false
  embedded manifest verified. ZIP SHA-256: `cf3fdb8d818ef6f02bc373f339bd9140e46115d88bec3a36cc0517308bd5c1b3`.

Temporary downloaded deliverables and machine-readable receipts are under the
current task's `outputs/windows-parakeet-preview-0.2.0/`. The earlier 0.1.0 ZIP
uses the old Windows recognizer and is superseded by this quality preview.
Durable test details: `docs/windows-transcription-quality.md` and
`windows/quality/fixtures.json`. No central memory update was made.

## Remaining boundaries and next action

Have Will extract this new ZIP on the ThinkPad, open Parrot.exe, wait for model
readiness, and test a harmless paragraph containing names/numbers. Confirm the
shortcut, microphone, Cancel, and manual Ctrl+V. Hardware capture, device removal,
sleep/resume, DPI/Narrator, standard-user install, and company controls are not
proven by runneradmin CI. The CPU/RAM model of the ThinkPad remains unknown.

Cancel invalidates delivery immediately but cannot interrupt an active native
Decode call. It checks before subsequent chunks. A hung native call requires
quitting/reopening; new capture is blocked to avoid overlap.

Public prerelease publication, master merge, signing credentials, and replacing
the daily Mac installation still require their own explicit authority. Do not
claim broad best-in-class quality or Mac parity from this small fixture suite.
