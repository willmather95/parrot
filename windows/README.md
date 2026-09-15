# Parrot for Windows preview

A Windows companion to [Will's Parrot](https://github.com/willmather95/parrot).
This preview uses the **installed Windows local speech recognizer**, not the
Mac's Parakeet model. Recognition quality and available languages can differ.

## Download and open

The source candidate includes a Windows CI build. A public downloadable release
has not yet been published. After the checks workflow succeeds, its
`parrot-windows-x64-preview` artifact contains the ZIP and SHA-256 checksum.
Downloading a GitHub Actions artifact may require a GitHub account.

1. On Windows 11 x64, extract the ZIP to a folder you own.
2. Open `Parrot.exe`. No Python, Node, .NET SDK, service, or driver installation is
   needed. It uses the .NET Framework already included with Windows 11.
3. Read the speech availability status. The preview needs an installed English
   desktop speech recognizer and Windows microphone permission.
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

- Speech recognition runs locally through System.Speech; the app has no cloud
  transcription endpoint.
- The host does not save raw audio or transcript history to disk.
- Clipboard delivery is explicit. Windows clipboard history, synchronization,
  and the destination application have their own retention behavior.
- Password fields, unknown focus, and higher-privilege targets fail closed.
- Cancel discards the capture. A ten-minute limit bounds a recording.
- No automatic typing or foreground paste is sent.

## Build and checks

From a Windows source checkout:

```powershell
.\windows\Build.ps1
.\windows\Package.ps1
```

Build.ps1 uses Windows' existing x64 .NET Framework compiler and runs deterministic
self-tests. Package.ps1 produces `windows/build/parrot-windows-x64-preview.zip`.
The app also supports `--version`, `--doctor`, and `--self-test`; these modes must
not be confused with a successful real dictation.

Before a production release, verify on a **standard user** Windows account:
real microphone capture, manual paste into an editor, password/unknown-focus
blocking, repeated capture, cancel, ten-minute limit, microphone disconnection,
sleep/resume, hotkey conflicts, Narrator, and 100/150/200% display scaling.
The CI runner's successful build does not establish standard-user or managed
work-device acceptance.

## Next backend

Mac-quality local inference requires a Windows-compatible model/runtime. Add it
only with pinned artifacts, verified downloads, cancellation/resource limits,
and real accuracy/latency measurements. This preview does not claim that parity.
