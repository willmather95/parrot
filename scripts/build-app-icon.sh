#!/usr/bin/env bash

set -euo pipefail

SOURCE=${1:-AppBundle/ParrotAppIcon.png}
OUTPUT=${2:-dist/Parrot.app/Contents/Resources/Parrot.icns}

for command_name in iconutil sips; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "missing dependency: $command_name" >&2
        exit 1
    fi
done

if [ ! -f "$SOURCE" ]; then
    echo "app icon source is missing: $SOURCE" >&2
    exit 1
fi

SOURCE_WIDTH=$(sips -g pixelWidth "$SOURCE" | awk '/pixelWidth/ {print $2}')
SOURCE_HEIGHT=$(sips -g pixelHeight "$SOURCE" | awk '/pixelHeight/ {print $2}')
SOURCE_ALPHA=$(sips -g hasAlpha "$SOURCE" | awk '/hasAlpha/ {print $2}')

if [ "$SOURCE_WIDTH" != "$SOURCE_HEIGHT" ] || [ "$SOURCE_WIDTH" -lt 1024 ]; then
    echo "app icon source must be square and at least 1024 pixels" >&2
    exit 1
fi

if [ "$SOURCE_ALPHA" != "yes" ]; then
    echo "app icon source must include transparency for macOS icon corners" >&2
    exit 1
fi

TEMP_DIR=$(mktemp -d)
ICONSET="$TEMP_DIR/Parrot.iconset"
mkdir -p "$ICONSET" "$(dirname "$OUTPUT")"

cleanup() {
    rm -rf -- "$TEMP_DIR"
}
trap cleanup EXIT

render_icon() {
    local pixels=$1
    local filename=$2
    sips -z "$pixels" "$pixels" "$SOURCE" --out "$ICONSET/$filename" >/dev/null
}

render_icon 16 icon_16x16.png
render_icon 32 icon_16x16@2x.png
render_icon 32 icon_32x32.png
render_icon 64 icon_32x32@2x.png
render_icon 128 icon_128x128.png
render_icon 256 icon_128x128@2x.png
render_icon 256 icon_256x256.png
render_icon 512 icon_256x256@2x.png
render_icon 512 icon_512x512.png
render_icon 1024 icon_512x512@2x.png

iconutil -c icns "$ICONSET" -o "$OUTPUT"
test -s "$OUTPUT"
