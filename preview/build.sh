#!/bin/sh
set -eu

repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
app_dir="$repo_dir/preview/.build/ParrotRetroPreview.app"
mkdir -p "$app_dir/Contents/MacOS"
cp "$repo_dir/preview/Info.plist" "$app_dir/Contents/Info.plist"
bash "$repo_dir/scripts/build-app-icon.sh" "$repo_dir/AppBundle/ParrotAppIcon.png" "$app_dir/Contents/Resources/Parrot.icns"

swiftc -swift-version 5 -warnings-as-errors -target arm64-apple-macos14.0 \
  "$repo_dir/Sources/parrot/Models/DictationPreferences.swift" \
  "$repo_dir/Sources/parrot/UI/StudioView.swift" \
  "$repo_dir/Sources/parrot/UI/RecordingOverlay.swift" \
  "$repo_dir/Sources/parrot/UI/MenuBarController.swift" \
  "$repo_dir/preview/PreviewApp.swift" \
  -framework AppKit -framework SwiftUI \
  -o "$app_dir/Contents/MacOS/ParrotRetroPreview"

# Sign only this generated preview bundle so LaunchServices can open it normally.
codesign --force --sign - "$app_dir"
codesign --verify --strict "$app_dir"

printf '%s\n' "$app_dir"
