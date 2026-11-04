# AGENTS.md: Parrot

## PROTECTED INFRASTRUCTURE
Parrot (`com.digimata.parrot`: the installed app, CLI, speech models, LaunchAgent) runs on Bill's Mac. Never stop, unload, move, delete, prune or re-sign it, even under disk or swap pressure. Health checks are read-only. Never run installer tests against the daily-running app. Never replace the signing identity or the stable `com.digimata.parrot` bundle ID (it keeps Accessibility and microphone permissions).

## Purpose
Local voice-to-text. Press a shortcut, speak, get text. macOS (Apple Silicon, macOS 14+, Control+Fn/Globe) and a Windows 11 x64 preview (Ctrl+Alt+Space, clipboard only). Audio and transcription stay on the machine. Customized fork of digimata/parrot. Repo: `willmather95/parrot`, default branch `master`.

## Stack
- Mac: Swift package (`Package.swift`, `Sources/parrot`), Parakeet TDT 0.6B v2 (Core ML) and WhisperKit, AppKit/SwiftUI Studio window
- Windows: C# built with the in-box .NET Framework compiler (`windows/*.ps1`), sherpa-onnx Parakeet
- Installer: `scripts/install.sh` (checksum-verified, rollback)

## Layout
`Sources/parrot/` (Audio, Input, Models, Transcription, UI), `Tests/parrotTests/`, `AppBundle/Info.plist`, `scripts/`, `windows/`, `preview/` (standalone UI preview app), `docs/` (architecture, release-checklist, branding, studio-ui), `.github/workflows/` (checks, release).

## Run and test (CI-defined; do not run these against the live install)
- Build: `swift build -c release`
- Tests: `swift test --disable-automatic-resolution`
- Installer checks: `bash -n scripts/install.sh`, `bash scripts/tests/test-install-paths.sh`
- Model smoke: `parrot models smoke parakeet-tdt-0.6b-v2`
- UI preview (separate bundle ID `local.parrot.retro.preview`): `sh preview/build.sh`
- Windows: `.\windows\Build.ps1`, `.\windows\Package.ps1` on Windows only

## Deploy
Tag `v*` triggers `release.yml` (build, test, model smoke, stage, publish). A release is public: ask first. Mac releases are ad hoc signed, not notarized (Bill will not pay the $99 Apple account). The README leads with the verified `curl | bash` installer; the ZIP is update-only and needs Open Anyway. Windows ships as a labeled prerelease and must not become the latest Mac release.

## Gotchas
- `git push` to `willmather95/*` as the active gh account (`mavericksolutions7`) fails 403. Push with a per-command credential, token never printed: `git -c credential.helper= -c 'credential.helper=!f(){ echo username=willmather95; echo "password=$(gh auth token --user willmather95)"; }; f' push -u origin <branch>`. Do not `gh auth switch`.
- README or release copy changes need Bill's typed "copy approved" before merge.
- `Package.resolved` must not change in CI (`git diff --exit-code`).
- Insertion happens only if the original field still owns focus; otherwise clipboard. Secure fields abort capture. Keep that behavior.
- Warmup runs one private inference on synthetic audio before "ready"; a cold launch is slower.

## Where state lives
- Settings: `~/Library/Application Support/parrot/settings.json`
- Installed app: `/Applications/Parrot.app` (or `~/Applications` with `--user`)
- In repo: `HANDOVER.md`, `docs/release-checklist.md`
- Memory: `project-parrot-mac-download-gatekeeper-2026-11-03.md`, `gotcha-gh-active-account-cannot-push-willmather95-repos-2026-11-03.md`

Verify in the running app: no verify-parrot skill yet. Parrot is not a web app. README smoke test (read-only health check on the installed app): `/Applications/Parrot.app/Contents/MacOS/parrot doctor --live-audio --model-ready`.
