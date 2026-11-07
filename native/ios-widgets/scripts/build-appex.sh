#!/usr/bin/env bash
# Builds DiarionWidgets.appex into build/Release-<sdk>, where AdditionalAppExtensions of Diarion.csproj
# expects it (spec 20, R6). The Xcode project is generated from project.yml when missing. Runs on the
# macOS runner only; there is no Mac on the team, so the script has to be the whole story.
#
# Usage: build-appex.sh <iphonesimulator|iphoneos> [SETTING=value ...]
#   Without settings the build is unsigned (CODE_SIGNING_ALLOWED=NO): the unsigned IPA and the Swift loop.
#   For App Store Connect pass manual signing, e.g.
#     build-appex.sh iphoneos CODE_SIGN_STYLE=Manual 'CODE_SIGN_IDENTITY=Apple Distribution: …' \
#       'PROVISIONING_PROFILE_SPECIFIER=Diarion Widgets App Store' DEVELOPMENT_TEAM=9XZ2FQC6B7
set -euo pipefail

sdk="${1:?usage: build-appex.sh <iphonesimulator|iphoneos> [SETTING=value ...]}"
shift
dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$dir"

case "$sdk" in
  iphoneos) destination="generic/platform=iOS" ;;
  iphonesimulator) destination="generic/platform=iOS Simulator" ;;
  *) echo "Unknown SDK '$sdk'"; exit 1 ;;
esac

if [ ! -d DiarionWidgets.xcodeproj ]; then
  xcodegen generate
fi

settings=("$@")
if [ ${#settings[@]} -eq 0 ]; then
  settings=(CODE_SIGNING_ALLOWED=NO)
fi

xcodebuild \
  -project DiarionWidgets.xcodeproj \
  -scheme DiarionWidgets \
  -configuration Release \
  -sdk "$sdk" \
  -destination "$destination" \
  SYMROOT="$dir/build" \
  OBJROOT="$dir/build/obj" \
  ONLY_ACTIVE_ARCH=NO \
  -quiet \
  "${settings[@]}" \
  build

appex="$dir/build/Release-$sdk/DiarionWidgets.appex"
if [ ! -d "$appex" ]; then
  echo "Expected $appex after the build"
  find "$dir/build" -maxdepth 2 -print | head -40
  exit 1
fi

du -sh "$appex"
/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$appex/Info.plist"
/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$appex/Info.plist"
/usr/libexec/PlistBuddy -c 'Print CFBundleVersion' "$appex/Info.plist"
/usr/libexec/PlistBuddy -c 'Print NSExtension:NSExtensionPointIdentifier' "$appex/Info.plist"

# MSBuild re-signs the embedded .appex with the app's identity and reads its entitlements from
# <dir>/<Name>.entitlements (macios _ExtendAppExtensionReferences). A signed build hands over exactly
# what xcodebuild signed (application-identifier, team, App Groups); an unsigned one the XcodeGen
# entitlements, which only matter for the simulator and the unsigned IPA.
entitlements="$dir/DiarionWidgets.entitlements"
if [ -f "$appex/embedded.mobileprovision" ]; then
  echo "Embedded provisioning profile: $(security cms -D -i "$appex/embedded.mobileprovision" | /usr/libexec/PlistBuddy -c 'Print Name' /dev/stdin)"
  codesign -d --entitlements - --xml "$appex" > "$entitlements"
else
  echo "Unsigned build (no embedded provisioning profile)"
  cp "$dir/DiarionWidgets/DiarionWidgets.entitlements" "$entitlements"
fi
plutil -lint "$entitlements"
echo "Entitlements for the MSBuild re-sign ($entitlements):"
/usr/libexec/PlistBuddy -c 'Print' "$entitlements"
