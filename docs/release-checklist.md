# Parrot upgrade and release checklist

## Current proof boundary

The installed macOS app and current public release are v0.1.4. The source
candidate is v0.1.5, adding opt-in user installation and stronger speech smoke
verification. Windows 0.2.0 is a separate preview with bundled CPU Parakeet inference.
No public Windows package or notarized Mac release is implied by source changes.

## Candidate verification

- Preserve the running Mac installation, local models, and stable
  `com.digimata.parrot` identity while building.
- Run `swift test --disable-automatic-resolution` and a release build.
- Run `parrot models smoke parakeet-tdt-0.6b-v2` from the candidate build. The
  generated phrase must match after case and punctuation normalization.
- Run installer path tests and `bash -n scripts/install.sh`.
- On a disposable Mac, test system installation, user installation, refusal of
  conflicting locations, upgrade failure/rollback, login startup, real dictation,
  and retained permissions. Never exercise installer tests against Will's
  daily-running app without explicit replacement authority.
- Windows CI builds the executable and checks state transitions, package hashes,
  install/update behavior, and refusal of corrupt downloads. It also decodes
  pinned public speech fixtures with the actual bundled model, measures word
  errors and timing, and checks silence/quiet-noise output. This small suite is
  regression evidence, not a broad accuracy benchmark or Mac parity proof.
- On Windows with a standard account, run every runtime gate in windows/README.md.
  CI may run elevated; a green job is not proof of no-admin work-device acceptance.
- Run Boris and an independent Fresh Eyes review on the final candidate.

## Publisher identity

The existing macOS release is ad hoc signed, not Developer ID signed or notarized.
The Windows preview is unsigned. Checksums protect against corruption but do not
authenticate a publisher when an attacker can replace both package and checksum.

For a notarized Mac release, obtain a Developer ID Application identity, enable
the required hardened runtime and justified entitlements, sign, submit through
`notarytool`, staple the accepted ticket, and verify Gatekeeper acceptance on the
downloaded artifact. Test TCC migration separately: changing signing requirements
can affect existing Microphone/Accessibility approval. Do not silently replace
the established identity in Will's installed app.

For signed Windows distribution, obtain an Authenticode signing identity or an
approved signing service, sign and timestamp the executable, and verify the
signature of the downloaded executable. Company policy may still require IT
approval even when the executable requests `asInvoker` and uses only user paths.

Reference: [Apple Developer ID](https://developer.apple.com/developer-id/) and
[Windows application manifests](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests).

## Updating

Current update delivery is explicit: download a verified release, finish the
current dictation, and run its installer. Preserve previous-version rollback and
user preferences. Windows supports portable use or per-user file installation.
Neither new installer introduces a background auto-updater.

A future “Check for Updates” feature should show current/latest versions and
release notes, verify publisher signatures, and wait until capture/inference is
idle before replacement. Sparkle is a candidate for macOS, but has not been added
as a dependency. See [Sparkle documentation](https://sparkle-project.org/documentation/).

## Publication

Show changed files and commit intent, verify git author/email, obtain the user's
commit/push/release approval, and run checks on the resulting revision. For
Windows, attach the tested ZIP and checksum to an explicitly labeled prerelease.
Do not make the Windows preview the latest stable Mac release. Read back the
public release and its downloaded artifact after publishing. Record Windows
runtime acceptance separately from compilation and asset availability.
