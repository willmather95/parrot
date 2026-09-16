# Parrot

Fast, private voice-to-text for Apple Silicon Macs. Press one shortcut, speak naturally, and get clean text ready to paste.

Audio and transcription stay on your Mac. There is no account, subscription, or cloud API.

## Upgrade candidate and Windows preview

The working source now includes a **Windows 11 x64 preview** and opt-in macOS
per-user installation. These changes are not yet a published release.
See [Windows setup and limitations](windows/README.md) and the
[release checklist](docs/release-checklist.md).

The Windows preview runs from a user-owned folder and copies dictated text for
manual paste. The 0.2.0 candidate replaces Windows' legacy speech engine with
bundled, local Parakeet TDT 0.6B v2 inference through sherpa-onnx. Its CPU-oriented
int8 model uses the same model family as the Mac app. Accuracy and latency still
need real microphone verification on the target laptop.

The macOS candidate's installer accepts `--user` to install under
`~/Applications/Parrot.app`, with a user-local CLI link. This requires a release
containing the matching new app; the current public v0.1.4 installer does not
support this mode. No implicit migration between system and user locations is
performed. Microphone, Accessibility, and company application policies still
apply.

## Install

Open Terminal, paste this entire line, and press Return:

```sh
curl -fsSL https://github.com/willmather95/parrot/releases/latest/download/install.sh | bash
```

The installer walks you through the two macOS permission prompts, downloads the local speech model, verifies your microphone, and starts Parrot automatically whenever you log in.

Parrot runs one private local inference before it reports ready. A cold launch can therefore take longer, but the one-time Core ML initialization cost is paid before your first dictation instead of after you stop speaking. The warmup uses generated synthetic audio, plays nothing, and retains no transcript.

At login, a crash-aware supervisor opens Parrot as the signed app through macOS Launch Services. That keeps its menu-bar bird and bottom-of-screen listening pill attached to the active desktop instead of stranding them on the desktop that happened to exist during startup. A deliberate menu-bar Quit stays quit; a crash or fatal hotkey failure is restarted automatically.

It also sets the Globe key to "Do Nothing" so macOS does not intercept Parrot's shortcut. You can change that later in System Settings > Keyboard.

**Requires:** macOS 14 or newer on an Apple Silicon Mac (M1 or newer). The first install downloads the on-device speech model and can take a few minutes.

## Install with Codex or Claude Code

Parrot is installed on your Mac. Codex and Claude Code are optional local assistants that can run the official installer for you.

A cloud-only task runs away from your Mac. It cannot install into your `/Applications` folder, approve macOS permissions, or verify your microphone. Use a local session, or a remote-control session explicitly attached to this Mac.

If you already have Codex or Claude Code running locally, paste this prompt:

```text
Install Parrot on this Mac using the official installer from https://github.com/willmather95/parrot.

1. Confirm that this Mac is running macOS 14 or newer on Apple Silicon.
2. Show me this exact command and wait for my approval:
   curl -fsSL https://github.com/willmather95/parrot/releases/latest/download/install.sh | bash
3. Run only that official installer as my normal user. Do not build from source, use a different download, or bypass permission prompts.
4. If macOS asks for Accessibility or Microphone access, stop and tell me exactly which app to enable. Do not claim success until the permission step is complete.
5. After installation, run:
   /Applications/Parrot.app/Contents/MacOS/parrot doctor --live-audio --model-ready
6. Report each check as passed or failed, plus any remaining action I need to take.
```

To start the local assistants from Terminal, use their official setup first if needed:

```sh
# Codex CLI
npm install -g @openai/codex
codex --login
codex

# Claude Code
npm install -g @anthropic-ai/claude-code
claude
```

These CLI commands require a currently supported Node.js and npm installation. If you do not already have them, use the linked setup pages instead. You do not need an AI assistant to install Parrot itself.

You can also use the [Codex app](https://developers.openai.com/codex/app), [Codex CLI](https://developers.openai.com/codex/cli), or [Claude Code](https://code.claude.com/docs/en/getting-started) directly. The agent can guide the install, but you must review and approve the installer command and complete the macOS permission prompts yourself.

## Use it

**Best workflow, especially in Codex:** Use Parrot like a voice clipboard. Press once and let go, speak, press once again and let go, then paste the transcript.

1. Click the text field where you want your words to appear.
2. Press `Control + Fn/Globe` once, then release both keys. You do not need to hold them while you talk.
3. Speak naturally. You can dictate a quick thought or talk for a minute. The pill at the bottom of the screen confirms that Parrot is listening.
4. When you are finished, press `Control + Fn/Globe` once again, then release both keys.
5. Parrot transcribes your recording locally and quickly. In Codex, press `Command-V` to paste the transcript.

In some apps, Parrot inserts the transcript automatically. If the text already appears, you are done. If it does not, Parrot has copied it to your clipboard, so press `Command-V`. This clipboard workflow is the most dependable way to use Parrot across different apps.

Codex desktop currently does not expose its composer as a focused Accessibility control. Parrot therefore uses the safe clipboard fallback there instead of guessing which opaque control owns focus. Auto-insert continues to work in apps that expose a specific editable field.

Parrot will not start in a password or other secure field. If focus moves into one while Parrot is working, it discards that transcript instead of putting it on the clipboard.

## Troubleshooting

Run one check that verifies permissions, the real microphone path, and the selected local model:

```sh
parrot doctor --live-audio --model-ready
```

If `parrot` is not on your PATH, use the full executable path:
`/Applications/Parrot.app/Contents/MacOS/parrot` for the system install, or
`"$HOME/Applications/Parrot.app/Contents/MacOS/parrot"` for a per-user install.

If macOS permissions were changed after installation, run:

```sh
parrot setup
parrot install --launch-at-login
```

After a restart, the idle bird should appear in the menu bar. During recording, macOS shows its microphone privacy indicator and Parrot shows the listening pill near the bottom of the active display. If either Parrot surface is missing, reinstalling the login item with the command above reattaches the app to the current GUI session.

## Useful commands

```sh
parrot doctor                          # check permissions and shortcut settings
parrot models list                     # list available speech models
parrot models download <id>            # download a model before selecting it
parrot --model whisper-large-v3-turbo  # use the larger multilingual model
parrot install --uninstall             # stop launching Parrot at login
```

## What this fork adds

This is a customized fork of [Digimata's original Parrot project](https://github.com/digimata/parrot). Full credit to Digimata for the foundation.

The fork adds:

- A Parakeet model tuned for fast English dictation
- Safer cursor insertion that verifies the original field before typing
- Clipboard fallback when focus changes
- Protection for secure and unobservable fields
- Runtime recovery for microphone route changes and repeated dictation
- Clear clipboard fallback diagnostics when an app does not expose a focused Accessibility control
- Launch Services startup so the menu-bar item and overlay follow the active desktop after login
- A checksum-verified app installer with launch-at-login setup and rollback

The stable `com.digimata.parrot` app identity is intentionally preserved so macOS can retain existing Accessibility and microphone permissions across updates.

## Build from source

```sh
swift build -c release
.build/release/parrot --help
```

See [docs/architecture.md](docs/architecture.md) for the design and safety model.

## How the release is verified

The installer downloads the latest GitHub release, verifies its published SHA-256 checksum, checks the app signature and stable identity, and installs `/Applications/Parrot.app`. Updates are staged transactionally. If a running replacement cannot become ready, the installer restores the prior app and service.

This is an open-source beta. The release is ad hoc signed and is not Apple-notarized. The installer removes quarantine only after the checksum, archive paths, signature, and app identity pass verification. You can [read the installer](scripts/install.sh) before running it.

## License

Parrot remains available under the original project's [MIT License](LICENSE).
