# Parrot Retro Preview

This app renders the same Studio and recording overlay source used by the candidate Mac UI. It has a distinct `local.parrot.retro.preview` bundle ID. Its phrase entries are in memory; its levels and recording state are simulated. It does not initialize Parrot's microphone, hotkey, model, clipboard, settings, or LaunchAgent.

From the repository root:

```sh
sh preview/build.sh
preview/.build/ParrotRetroPreview.app/Contents/MacOS/ParrotRetroPreview --state recording --section overview --screenshot preview/.build/overview.png
```

Accepted states: `ready`, `recording`, `transcribing`, `error`. Accepted sections: `overview`, `vocabulary`, `snippets`. Add `--save-failure` to make the in-memory save callback fail so the recovery state can be inspected. Omit `--screenshot` to keep the preview window open for keyboard and accessibility inspection. The build uses `swiftc -warnings-as-errors` with the system AppKit and SwiftUI frameworks; it does not resolve SwiftPM dependencies.

Hallmark preview flags: `--empty`, `--draft`, `--minimum`, `--load-failure` and `--save-failure`. `--reduced-motion` disables view transaction animations for a static preview; it does not change the OS accessibility preference or prove actual OS Reduce Motion behavior.
