# Contributing

Issues and pull requests are welcome. Keep changes small and say how you tested them. Contributions are under the [MIT License](LICENSE).

## Layout

- `Sources/parrot/`: the Mac app and CLI (Audio, Input, Models, Transcription, UI)
- `Tests/parrotTests/`: Swift tests
- `scripts/`: the checksum-verified installer and its tests
- `windows/`: the Windows preview (C#, built with the in-box .NET Framework compiler)
- `preview/`: a standalone UI preview app
- `docs/`: architecture, release checklist, branding, Studio UI and design specs

## Build and test on a Mac

Needs macOS 14 or later on Apple Silicon and a current Swift toolchain.

```sh
swift build -c release
swift test --disable-automatic-resolution
bash -n scripts/install.sh
bash scripts/tests/test-install-paths.sh
```

CI runs the tests and installer checks on every pull request (`.github/workflows/checks.yml`).

Model smoke test, run against the binary you just built. The first run downloads the Parakeet model.

```sh
.build/release/parrot models smoke parakeet-tdt-0.6b-v2
```

UI preview (it uses its own bundle ID, so it does not touch an installed Parrot):

```sh
sh preview/build.sh
```

`test-install-paths.sh` is safe on any Mac. Full installer runs (install, upgrade, rollback) replace the installed app, so do those on a disposable Mac. See [docs/release-checklist.md](docs/release-checklist.md).

## Build on Windows

Needs Windows 11 x64 and network access for the pinned dependencies. From the repository root in PowerShell:

```powershell
.\windows\Package.ps1
```

`Package.ps1` runs `Build.ps1` and its self-tests first. CI also runs `.\windows\Test-Package.ps1`.
