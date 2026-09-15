#!/usr/bin/env bash

set -euo pipefail

REPOSITORY_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
INSTALLER="$REPOSITORY_ROOT/scripts/install.sh"
TEST_ROOT=$(mktemp -d)
trap 'rm -rf "$TEST_ROOT"' EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

HELP_OUTPUT=$(bash "$INSTALLER" --help)
grep -F -- '--user' <<<"$HELP_OUTPUT" >/dev/null \
    || fail "installer help does not document --user"
# These are literal help-text paths, not shell paths to expand.
# shellcheck disable=SC2088
grep -F -- '~/Applications' <<<"$HELP_OUTPUT" >/dev/null \
    || fail "installer help does not document the per-user app path"
# shellcheck disable=SC2088
grep -F -- '~/.local/bin' <<<"$HELP_OUTPUT" >/dev/null \
    || fail "installer help does not document the per-user CLI path"

if bash "$INSTALLER" --not-a-real-option >"$TEST_ROOT/invalid.out" 2>&1; then
    fail "installer accepted an unknown option"
fi
grep -F 'unknown option' "$TEST_ROOT/invalid.out" >/dev/null \
    || fail "unknown-option failure was not clear"

# Load only the actual pure guard functions, never the installer's main body.
# Do not change HOME, source launchd state, or touch the installed application.
red() { printf '%s\n' "$*" >&2; }
eval "$(sed -n '/^conflict_instructions() {/,/^}/p' "$INSTALLER")"
eval "$(sed -n '/^check_install_location_conflicts() {/,/^}/p' "$INSTALLER")"
eval "$(sed -n '/^validate_release_tag() {/,/^}/p' "$INSTALLER")"
OTHER_APP_DIR="$TEST_ROOT/other/Parrot.app"
AGENT_PLIST="$TEST_ROOT/absent.plist"
APP_EXECUTABLE_PATH="$TEST_ROOT/selected/Parrot.app/Contents/MacOS/parrot"
AGENT_TARGET="test-only"
export AGENT_PLIST APP_EXECUTABLE_PATH AGENT_TARGET
mkdir -p "$OTHER_APP_DIR"
if check_install_location_conflicts >"$TEST_ROOT/conflict.out" 2>&1; then
    fail "default installer accepted a conflicting per-user app"
fi
grep -F "another Parrot.app already exists at $OTHER_APP_DIR" \
    "$TEST_ROOT/conflict.out" >/dev/null \
    || fail "conflicting per-user app path was not reported"
grep -F 'will not move between install locations automatically' \
    "$TEST_ROOT/conflict.out" >/dev/null \
    || fail "migration refusal was not explained"

INSTALL_MODE="user"
export INSTALL_MODE
TAG="v0.1.4"
if validate_release_tag >"$TEST_ROOT/version.out" 2>&1; then
    fail "per-user mode accepted incompatible v0.1.4"
fi
grep -F 'v0.1.5 or later; no files were changed' "$TEST_ROOT/version.out" >/dev/null \
    || fail "old-version refusal was not actionable"
for TAG in v0.0.9 v0.1.3 v0.1.5-beta v0.1.5/other; do
    if validate_release_tag >/dev/null 2>&1; then fail "accepted invalid or incompatible tag $TAG"; fi
done
for TAG in v0.1.5 v0.2.0 v1.0.0; do
    validate_release_tag || fail "rejected compatible tag $TAG"
done
INSTALL_MODE="system"
TAG="v0.1.4"
validate_release_tag || fail "system mode rejected the existing public release"
test ! -e "$TEST_ROOT/selected" || fail "guard created an installation directory"
printf 'PASS: installer paths, migration refusal, and release compatibility guards\n'
