#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
BUILD_DIR="$PROJECT_DIR/build"
APP_VERSION="${1:-1.1.0}"
APP_DIR="$BUILD_DIR/CodexTempo.app"
DMG_ROOT="$BUILD_DIR/dmg-root"
DMG_PATH="$BUILD_DIR/CodexTempo-macOS-universal.dmg"

"$SCRIPT_DIR/build-app.sh" "$APP_VERSION"
"$APP_DIR/Contents/MacOS/CodexTempo" --self-test

rm -rf "$DMG_ROOT"
mkdir -p "$DMG_ROOT"
cp -R "$APP_DIR" "$DMG_ROOT/CodexTempo.app"
ln -s /Applications "$DMG_ROOT/Applications"
rm -f "$DMG_PATH"
hdiutil create \
  -volname "Codex Tempo" \
  -srcfolder "$DMG_ROOT" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

echo "$DMG_PATH"
