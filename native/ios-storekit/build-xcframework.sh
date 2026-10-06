#!/usr/bin/env bash
#
# Builds DiarionStoreKit.xcframework from the Swift facade. Needs a Mac with Xcode 16+; in this
# repository that Mac is a GitHub runner (swift-storekit.yml and build-ios.yml), never a developer
# machine, so the output is not committed and the script has to be the whole story.
#
# The product is STATIC on purpose (spec 15, R3.3): the symbols end up inside the Diarion binary,
# which is what DllImport("__Internal") resolves against, and no second embedded framework with
# its own Info.plist ever appears in the bundle. Archiving a static SwiftPM product yields one
# prelinked object per platform; each is wrapped in a minimal static .framework because that is
# the shape a NativeReference of Kind=Framework links, and the two are joined into an xcframework.

set -euo pipefail

cd "$(dirname "$0")"
DERIVED="$PWD/.build-xcframework"
OUTPUT="$PWD/DiarionStoreKit.xcframework"
rm -rf "$DERIVED" "$OUTPUT"

build() {
  local sdk="$1" destination="$2"
  xcodebuild archive \
    -workspace . \
    -scheme DiarionStoreKit \
    -destination "$destination" \
    -sdk "$sdk" \
    -archivePath "$DERIVED/$sdk" \
    -quiet \
    SKIP_INSTALL=NO \
    BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
    OTHER_SWIFT_FLAGS='-enable-library-evolution'
}

wrap() {
  local sdk="$1"
  local framework="$DERIVED/$sdk/DiarionStoreKit.framework"
  local object
  object="$(find "$DERIVED/$sdk.xcarchive/Products" -name 'DiarionStoreKit.o' -print -quit)"
  [[ -n "$object" ]] || { echo "No prelinked DiarionStoreKit.o in the $sdk archive" >&2; exit 1; }
  mkdir -p "$framework"
  libtool -static -o "$framework/DiarionStoreKit" "$object"
  cat > "$framework/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key><string>DiarionStoreKit</string>
	<key>CFBundleIdentifier</key><string>org.diarion.storekit</string>
	<key>CFBundleName</key><string>DiarionStoreKit</string>
	<key>CFBundlePackageType</key><string>FMWK</string>
	<key>CFBundleShortVersionString</key><string>1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>MinimumOSVersion</key><string>16.0</string>
</dict>
</plist>
PLIST
}

build iphoneos        'generic/platform=iOS'
build iphonesimulator 'generic/platform=iOS Simulator'
wrap  iphoneos
wrap  iphonesimulator

xcodebuild -create-xcframework \
  -framework "$DERIVED/iphoneos/DiarionStoreKit.framework" \
  -framework "$DERIVED/iphonesimulator/DiarionStoreKit.framework" \
  -output "$OUTPUT"

rm -rf "$DERIVED"

# The one promise the C# side depends on: every entry point it P/Invokes is really exported.
for symbol in dsk_load_products dsk_purchase dsk_current_entitlements dsk_subscription_statuses \
              dsk_sync dsk_app_transaction dsk_manage_subscriptions dsk_start_updates; do
  nm -gU "$OUTPUT/ios-arm64/DiarionStoreKit.framework/DiarionStoreKit" | grep -q " _${symbol}$" \
    || { echo "Symbol _${symbol} is missing from the device slice" >&2; exit 1; }
done

echo "Built $OUTPUT"
