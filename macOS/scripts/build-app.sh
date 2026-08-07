#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
BUILD_DIR="$PROJECT_DIR/build"
APP_DIR="$BUILD_DIR/CodexTempo.app"
CACHE_DIR="$PROJECT_DIR/.build/local-cache"
MODULE_CACHE_DIR="$PROJECT_DIR/.build/module-cache"

mkdir -p "$CACHE_DIR" "$MODULE_CACHE_DIR"
export XDG_CACHE_HOME="$CACHE_DIR"
export CLANG_MODULE_CACHE_PATH="$MODULE_CACHE_DIR"
export SWIFTPM_MODULECACHE_OVERRIDE="$MODULE_CACHE_DIR"

cd "$PROJECT_DIR"
swift build --disable-sandbox -c release
BIN_DIR="$(swift build --disable-sandbox -c release --show-bin-path)"

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp "$BIN_DIR/CodexTempo" "$APP_DIR/Contents/MacOS/CodexTempo"
cp "$PROJECT_DIR/Assets/CodexTempo.icns" "$APP_DIR/Contents/Resources/CodexTempo.icns"

cat > "$APP_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>zh_CN</string>
    <key>CFBundleExecutable</key>
    <string>CodexTempo</string>
    <key>CFBundleIdentifier</key>
    <string>com.codextempo.mac</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleIconFile</key>
    <string>CodexTempo.icns</string>
    <key>CFBundleName</key>
    <string>Codex Tempo</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.2.2</string>
    <key>CFBundleVersion</key>
    <string>5</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$APP_DIR/Contents/MacOS/CodexTempo"
codesign --force --deep --sign - "$APP_DIR" >/dev/null
echo "$APP_DIR"
