#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
BUILD_DIR="$PROJECT_DIR/build"
APP_DIR="$BUILD_DIR/CodexTempo.app"
APP_VERSION="${1:-1.1.0}"
APP_BUNDLE_ID="${CODEX_TEMPO_BUNDLE_ID:-com.grapymage.codextempo}"
CACHE_DIR="$PROJECT_DIR/.build/local-cache"
MODULE_CACHE_DIR="$PROJECT_DIR/.build/module-cache"

VERSION_PATTERN='^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$'
BUNDLE_ID_PATTERN='^[A-Za-z0-9]+([.-][A-Za-z0-9]+)+$'
if [[ ! "$APP_VERSION" =~ $VERSION_PATTERN ]]; then
  echo "Invalid app version: $APP_VERSION" >&2
  exit 2
fi
if [[ ! "$APP_BUNDLE_ID" =~ $BUNDLE_ID_PATTERN ]]; then
  echo "Invalid bundle identifier: $APP_BUNDLE_ID" >&2
  exit 2
fi
BUILD_NUMBER="$(printf '%s' "$APP_VERSION" | tr -cd '0-9')"

mkdir -p "$CACHE_DIR" "$MODULE_CACHE_DIR"
export XDG_CACHE_HOME="$CACHE_DIR"
export CLANG_MODULE_CACHE_PATH="$MODULE_CACHE_DIR"
export SWIFTPM_MODULECACHE_OVERRIDE="$MODULE_CACHE_DIR"

cd "$PROJECT_DIR"
ARCH_ARGS=(--arch arm64 --arch x86_64)
if [[ "${CODEX_TEMPO_NATIVE_ONLY:-0}" == "1" ]]; then
  ARCH_ARGS=(--arch "$(uname -m)")
fi

swift build --disable-sandbox -c release "${ARCH_ARGS[@]}"
BIN_DIR="$(swift build --disable-sandbox -c release "${ARCH_ARGS[@]}" --show-bin-path)"

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp "$BIN_DIR/CodexTempo" "$APP_DIR/Contents/MacOS/CodexTempo"
cp "$PROJECT_DIR/Assets/CodexTempo.icns" "$APP_DIR/Contents/Resources/CodexTempo.icns"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>zh_CN</string>
    <key>CFBundleExecutable</key>
    <string>CodexTempo</string>
    <key>CFBundleIdentifier</key>
    <string>$APP_BUNDLE_ID</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleIconFile</key>
    <string>CodexTempo.icns</string>
    <key>CFBundleName</key>
    <string>Codex Tempo</string>
    <key>CFBundleDisplayName</key>
    <string>Codex Tempo</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$APP_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$BUILD_NUMBER</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSUIElement</key>
    <false/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$APP_DIR/Contents/MacOS/CodexTempo"
codesign --force --deep --sign - "$APP_DIR" >/dev/null
echo "$APP_DIR"
