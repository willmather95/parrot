# Parrot identity

Parrot is a quiet personal dictation instrument. The identity takes its cues from early-1980s audio equipment: a warm charcoal surface, ivory labels and amber signals.

One bird is used everywhere: a right-facing crested parrot head drawn as six flat shapes, defined once in `ParrotBrand.mark` in StudioView.swift. The menu bar shows it as a monochrome template image so it stays readable in either macOS appearance. Studio uses the same silhouette beside a monospaced PARROT wordmark. The app icon and the Windows icon are that same bird, unchanged, in cream on a dark green tile, matching the logo on the Parrot website.

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
| Icon tile | #13241C |
| Icon bird | #ECEBDF |
| Recording/error indicator | #E27564 |

All values are exact tokens. The icon tile and bird colors are used only by the app icon and the website logo; the app interface keeps the charcoal palette.

## Keep it quiet

Use the existing Mac system fonts. Keep the name readable; decorative labels use monospaced type. No neon colors, chrome bevels, looping animation or extra icon package. Recording and error indicators keep their existing meanings. Color is never the only state cue.

The Mac app icon is AppBundle/ParrotAppIcon.png and the Windows icons are windows/assets/Parrot.png and windows/assets/Parrot.ico. All three are rendered from the six paths in ParrotBrand.mark, so if the mark changes, render them again:

1. Write a 1024 by 1024 SVG (viewBox `0 0 1024 1024`). Draw the tile as `<rect x="100" y="100" width="824" height="824" rx="185" fill="#13241C"/>`, the standard macOS icon grid. Draw the six mark paths in #ECEBDF inside `<g transform="translate(197 187) scale(25)">`, keeping `fill-rule="evenodd"` on the head path so the eye stays open. This centres the bird's outline (not its 24 by 24 artboard) on the tile.
2. Render it to a 1024 pixel PNG with a transparent background (any SVG renderer; a headless Chromium screenshot with `--default-background-color=00000000` works). That PNG is AppBundle/ParrotAppIcon.png. Check it with `scripts/build-app-icon.sh AppBundle/ParrotAppIcon.png <scratch path>/Parrot.icns`.
3. For Windows, render the same SVG with the viewBox changed to `84 84 856 856`, again at 1024 pixels. The tighter margin lets the tile fill small Windows icon sizes. That PNG is windows/assets/Parrot.png.
4. Scale that Windows PNG to 16, 24, 32, 48, 64 and 256 pixels (`sips -z`) and pack the six PNGs into windows/assets/Parrot.ico as PNG-compressed frames.

The website logo uses the same bird and colors on a 32 by 32 tile; its bird sits slightly larger in the tile than on the macOS grid.

Native menu and Studio marks live in ParrotBrand in StudioView.swift, keeping the executable self-contained.

Parrot 0.1.5 shipped this Studio interface with the earlier ivory and amber app icon; the bird icon ships in 0.1.7. The isolated preview remains available for UI development.
