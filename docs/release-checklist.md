# Parrot upgrade and release checklist

## Current proof boundary

The current public Mac release is v0.1.6. Windows 0.2.0 is a separate preview
with bundled CPU Parakeet inference. Source changes alone do not mean a public
Windows package or a notarized Mac release exists.

## Candidate verification

- While building, do not disturb an installed copy of Parrot, its local models,
  or the stable `com.digimata.parrot` identity.
- Run `swift test --disable-automatic-resolution` and a release build.
- Run `parrot models smoke parakeet-tdt-0.6b-v2` from the candidate build. The
  generated phrase must match after case and punctuation normalization.
- Run installer path tests and `bash -n scripts/install.sh`.
- On a disposable Mac, test system installation, user installation, refusal of
  conflicting locations, upgrade failure/rollback, login startup, real dictation,
  and retained permissions. Never run installer tests on a Mac where
  you use Parrot every day. They replace the installed app.
- Windows CI builds the executable and checks state transitions, package hashes,
  install/update behavior, and refusal of corrupt downloads. It also decodes
  pinned public speech fixtures with the actual bundled model, measures word
  errors and timing, and checks silence/quiet-noise output. This small suite is
  regression evidence, not a broad accuracy benchmark or Mac parity proof.
- On Windows with a standard account, run every runtime gate in windows/README.md.
  CI may run elevated; a green job is not proof of no-admin work-device acceptance.
- Run an independent code review on the final candidate.

## Publisher identity

The existing macOS release is ad hoc signed, not Developer ID signed or notarized.
The Windows preview is unsigned. Checksums protect against corruption but do not
authenticate a publisher when an attacker can replace both package and checksum.

For a notarized Mac release, obtain a Developer ID Application identity, enable
the required hardened runtime and justified entitlements, sign, submit through
`notarytool`, staple the accepted ticket, and verify Gatekeeper acceptance on the
downloaded artifact. Test TCC migration separately: changing signing requirements
can affect existing Microphone/Accessibility approval. Do not change the signing
identity of existing installs without warning. macOS ties Microphone and
Accessibility approval to it.

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

Before publishing, review the changed files, check the git author and email, get
maintainer approval, and run the checks on the exact revision you will release.
For Windows, attach the tested ZIP and checksum to a release marked as a
prerelease. Do not make the Windows preview the latest stable Mac release. After
publishing, download the release and verify the artifact. Record Windows
runtime acceptance separately from compilation and asset availability.
