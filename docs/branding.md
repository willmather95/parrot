# Parrot identity

Parrot is a quiet personal dictation instrument. The identity takes its cues from early-1980s audio equipment: a warm charcoal surface, ivory labels, amber signals and a burnt-orange beak.

The right-facing bird is retained. Its crest and descending feathers become broad, simple shapes. The app icon uses full color; the menu bar uses a small monochrome silhouette so it remains readable in either macOS appearance. Studio uses that same native silhouette beside a monospaced PARROT wordmark.

## Palette

| Use | Color |
|---|---|
| Background | #181714 |
| Panel | #201F1B |
| Raised surface | #2B2923 |
| Rule | #524C40 |
| Ivory text | #EEE8D5 |
| Secondary text | #B7AF9A |
| Amber | #F0AE52 |
| Icon beak | #C65A35 |
| Recording/error indicator | #E27564 |

The interface values are exact tokens. The raster icon was generated from the previous bird as an identity reference; its colors and softened material may vary slightly from those tokens. The compact native silhouette is authored separately for clarity at menu-bar sizes.

## Keep it quiet

Use the existing Mac system fonts. Keep the name readable; decorative labels use monospaced type. No neon colors, chrome bevels, looping animation or extra icon package. Recording and error indicators keep their existing meanings. Color is never the only state cue.

The full-color icon is AppBundle/ParrotAppIcon.png. Its transparent corners are preserved when building the Mac icon. Native menu and Studio marks live in ParrotBrand in StudioView.swift, keeping the executable self-contained.

This source candidate updates branding and the isolated UI preview. Existing downloadable releases and the installed dictation app are separate from the source PR.
