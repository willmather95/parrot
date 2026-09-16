# Parrot for Windows preview

A Windows companion to [Will's Parrot](https://github.com/willmather95/parrot).
The **0.2.0 quality preview** uses bundled **Parakeet TDT 0.6B v2 int8** through
sherpa-onnx 1.13.8. Recognition runs locally on the CPU. This replaces the legacy
Windows speech engine used by preview 0.1.0, and uses the same model family as
Parrot on Mac. Different runtime and quantization mean identical output is not
guaranteed. This is an English dictation model with punctuation and casing.

## Download and open

**No terminal command is needed for portable use. The root README's macOS
`curl ... | bash` installer is not for Windows.**

Sign in to GitHub and open the [verified 0.2.0 build](https://github.com/willmather95/parrot/actions/runs/35105457141).
Scroll to **Artifacts** and download **parrot-windows-x64-preview** (about 481 MB).
Artifacts expire after 14 days; [check newer preview builds](https://github.com/willmather95/parrot/actions/workflows/checks.yml?query=branch%3Acodex%2Fparrot-windows-preview)
if it has expired. A public Windows release has not yet been published.

1. On Windows 11 x64, right-click the downloaded ZIP and choose **Extract All**
   into a folder you own. If that reveals another ZIP, extract it too.
2. Open `Parrot.exe`. No Python, Node, .NET SDK, service, or driver installation is
   needed. It uses the .NET Framework already included with Windows 11 and
   app-local inference libraries. Keep the entire extracted folder together,
   including the DLLs and `models` folder. The model makes this a larger download.
3. Allow Windows microphone access when needed. No Windows speech language pack,
   cloud account, GPU, or first-run model download is required.
4. Focus an ordinary editor, press **Ctrl+Alt+Space**, speak, then press the same
   shortcut again. When Parrot says it copied the result, press **Ctrl+V**.

The first real test should use a harmless sentence. Initializing a recognizer
or passing the software tests does not prove your microphone works.

## Optional per-user installation and updates

Portable use is supported. To put a verified extracted package in
`%LOCALAPPDATA%\Programs\Parrot`, close Parrot and run this from its extracted
download folder using Windows PowerShell:

```powershell
powershell.exe -NoProfile -File .\Install.ps1
```

No administrator rights are requested. The installer verifies the package,
stages the replacement, and restores the previous files if replacement fails.
It does not start recording, register startup, or change execution policy.
Repeat with a newly downloaded package to update. Finish your dictation and quit
Parrot before doing so. Do not store personal files inside the installed folder.

The checksum detects corruption; it is not a verified publisher signature.
This preview is unsigned. Windows SmartScreen or company application-control
policy may block it. If the script or executable is blocked, use your employer's
approved software process. Do not disable those controls or run as administrator.

## Privacy and controls

- Speech recognition runs locally through Parakeet; the app has no cloud
  transcription endpoint or automatic model download.
- The host does not save raw audio or transcript history to disk.
- Clipboard delivery is explicit. Windows clipboard history, synchronization,
  and the destination application have their own retention behavior.
- Password fields, unknown focus, and higher-privilege targets fail closed.
- Cancel discards the capture. A ten-minute limit bounds a recording.
- Cancellation suppresses delivery immediately, but an active native inference
  chunk must finish before its resources can be reused. If it stalls, quit and
  reopen Parrot. New recordings stay blocked during cleanup.
- No automatic typing or foreground paste is sent.

## Build and checks

From a Windows source checkout:

```powershell
.\windows\Build.ps1
.\windows\Package.ps1
```

Build.ps1 uses Windows' existing x64 .NET Framework compiler and runs deterministic
self-tests. Build/package dependencies are pinned and verified before use.
Package.ps1 produces `windows/build/parrot-windows-x64-preview.zip`, including
its neural model and runtime. The app supports `--version`, `--doctor`, and
`--self-test`; these modes must not be confused with successful real dictation.

For controlled test audio, `--transcribe-file example.wav --output-json result.json`
explicitly writes a benchmark result. Normal microphone use does not save audio
or transcript files. The CI-only Python quality harness downloads pinned public
speech fixtures and reports word errors and latency. It also requires empty
output for silence and quiet noise. Python is not needed to run Parrot.

Before a production release, verify on a **standard user** Windows account:
real microphone capture, manual paste into an editor, password/unknown-focus
blocking, repeated capture, cancel, ten-minute limit, microphone disconnection,
sleep/resume, hotkey conflicts, Narrator, and 100/150/200% display scaling.
The CI runner's successful build does not establish standard-user or managed
work-device acceptance.

## Quality acceptance

The small CI regression suite verifies the packaged inference path. It cannot
establish accuracy for your voice, room, microphone, specialist vocabulary, or
ThinkPad performance. A practical acceptance test is three short dictations:
ordinary prose, a work paragraph containing names and numbers, and speech with
pauses. Compare the pasted result against what you said and note the wait after
Stop. Repeated dropped words, invented text, or an excessive wait are failures to
investigate, not acceptable tradeoffs hidden behind a passing build.

Primary model/runtime documentation:
[Parakeet model card](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) and
[sherpa-onnx Parakeet support](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/offline-transducer/nemo-transducer-models.html).
See the bundled dependency notices for license and model provenance.
