# Parrot Studio

Open Studio from the menu-bar bird. Dictation still uses Control + Fn/Globe. Local transcription, model prewarming, original-focus checks, secure-field protection and clipboard fallback remain in the existing runtime.

## Personal wording

Vocabulary performs deterministic whole-phrase replacements after transcription. Snippets require an exact full utterance and expand it into saved text. Neither retrains or biases the speech model. Both lists start empty. Spoken phrases are unique across the two lists. Replacements do not cascade; contractions and word boundaries are protected. Matching vocabulary output uses NFC Unicode composition; unmatched text is byte-preserved only when no rule matches.

Search matches either side of an entry. Command-1/2/3 selects Overview/Vocabulary/Snippets, Command-N starts an entry, and Command-Return saves. Drafts survive section changes. Failed settings loads show unavailable data; failed/stale saves retain the draft and require reopening Studio before retrying. Settings writes preserve model selection and reject a stale phrase snapshot.

## Preview versus installed app

Run `sh preview/build.sh`, then open `preview/.build/ParrotRetroPreview.app`. The separate preview uses synthetic data in memory, no microphone, model, hotkey, clipboard, transcript history or real settings. Its displayed shortcut still belongs to the real dictation app; don't use it as a preview control. The normal source package is the integrated candidate. This source update does not replace an installed app or publish a new downloadable binary release.

The minimum 540 x 430 Studio scrolls vertically. Native keyboard and accessibility-tree checks were performed; full VoiceOver speech and daily dictation usability still need real-use testing. No cloud API, telemetry or new dependency was added.

## Interaction references

Official [Wispr Flow snippets](https://docs.wisprflow.ai/articles/5784437944-create-and-use-snippets), [VoiceInk replacements](https://tryvoiceink.com/docs/word-replacements), [Superwhisper modes](https://superwhisper.com/docs/modes/modes) and [Aqua](https://aquavoice.com/mac) informed clearer tool discovery and guidance. Parrot keeps its own visual design and local runtime. These are feature references, not comparative performance benchmarks.
