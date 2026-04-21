#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
DIST_DIR="$ROOT_DIR/dist"

ARCH="${1:-arm64}"  # arm64 (Apple Silicon) | x64 (Intel)
RID="osx-$ARCH"
PUBLISH_DIR="$DIST_DIR/${RID}-publish"
APP_NAME="Yottacast"
APP_BUNDLE="$DIST_DIR/${APP_NAME}.app"
BUNDLE_ID="com.yottacast.app"
VERSION=$(grep -m1 '<Version>' "$ROOT_DIR/Yottacast/Yottacast.csproj" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')

echo "==> Building $APP_NAME $VERSION for $RID..."
rm -rf "$PUBLISH_DIR" "$APP_BUNDLE"

dotnet publish "$ROOT_DIR/Yottacast/Yottacast.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained \
    -o "$PUBLISH_DIR"

# ── Build .app bundle structure ──────────────────────────────────────────────
echo "==> Creating .app bundle..."
MACOS_DIR="$APP_BUNDLE/Contents/MacOS"
RESOURCES_DIR="$APP_BUNDLE/Contents/Resources"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

# Copy all publish output into MacOS/
cp -r "$PUBLISH_DIR/." "$MACOS_DIR/"

# Info.plist
cat > "$APP_BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSUIElement</key>
    <true/>
</dict>
</plist>
PLIST

# Copy .icns icon if it exists next to this script
ICON_SRC="$SCRIPT_DIR/AppIcon.icns"
if [[ -f "$ICON_SRC" ]]; then
    cp "$ICON_SRC" "$RESOURCES_DIR/AppIcon.icns"
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP_BUNDLE/Contents/Info.plist"
    echo "    Icon: AppIcon.icns"
fi

# Remove quarantine so recipients don't get Gatekeeper warnings
xattr -cr "$APP_BUNDLE"

# ── Create DMG ────────────────────────────────────────────────────────────────
DMG_PATH="$DIST_DIR/${APP_NAME}-${VERSION}-${RID}.dmg"
echo "==> Creating DMG: $DMG_PATH"
rm -f "$DMG_PATH"

CREATE_DMG_OPTS=(
    --volname "$APP_NAME"
    --window-pos 200 120
    --window-size 540 380
    --icon-size 128
    --icon "${APP_NAME}.app" 140 190
    --app-drop-link 400 190
    --no-internet-enable
)

if [[ -f "$SCRIPT_DIR/AppIcon.icns" ]]; then
    CREATE_DMG_OPTS+=(--volicon "$SCRIPT_DIR/AppIcon.icns")
fi

create-dmg "${CREATE_DMG_OPTS[@]}" "$DMG_PATH" "$APP_BUNDLE"

# Cleanup intermediate dirs
rm -rf "$PUBLISH_DIR"

echo ""
echo "Done! -> $DMG_PATH"
