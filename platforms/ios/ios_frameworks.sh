# Shared by platforms/ios/bazel_build_tflite_ios_frameworks and platforms/ios/bazel_build_litert_lm_ios (sourced, not
# run): Bazel/Xcode setup and wrapping a built dylib into a framework for an xcframework slice.
#
# Expects to be sourced from the repository root.

if command -v python3.12 >/dev/null 2>&1; then
    export HERMETIC_PYTHON_VERSION=3.12
fi

REPO_ROOT=$(pwd)
# Keep Bazel's output tree in a subfolder of this repo, like the other Bazel build scripts.
BAZEL_OUTPUT_USER_ROOT="$REPO_ROOT/bazel_output"
WORKSPACE_DIR=LiteRT-LM
OUT_DIR=lib/ios
mkdir -p $OUT_DIR

# The iOS slices built: Bazel config : xcframework slice folder : CFBundleSupportedPlatforms
IOS_SLICES="ios_arm64:ios-arm64:iPhoneOS ios_sim_arm64:ios-arm64-simulator:iPhoneSimulator"

# Every framework targets the same minimum iOS version as LiteRT-LM's own CLiteRTLM framework (LiteRT-LM's .bazelrc
# sets 13.0, which the toolchain raises to 14.0).
IOS_MINIMUM_OS=15.0

# If Xcode is installed outside /Applications (e.g. on a separate volume), Bazel's sandbox won't use it by default.
XCODE_DEVELOPER_DIR="$(xcode-select -p 2>/dev/null)"
case "$XCODE_DEVELOPER_DIR" in
    /Volumes/*)
        echo "Xcode is on a non-root volume - setting BAZEL_ALLOW_NON_APPLICATIONS_XCODE=1 and DEVELOPER_DIR"
        export BAZEL_ALLOW_NON_APPLICATIONS_XCODE=1
        export DEVELOPER_DIR="$XCODE_DEVELOPER_DIR"
        ;;
esac

# run_bazel <bazel arguments>: runs bazel in the LiteRT-LM workspace.
run_bazel() {
    (cd $WORKSPACE_DIR && bazel --output_user_root="$BAZEL_OUTPUT_USER_ROOT" "$@")
}

# check_bound <dylib>: fails unless every symbol the dylib imports is bound to a library. LiteRT-LM's .bazelrc links with
# -undefined dynamic_lookup (its build:macos config applies on a macOS host, also to iOS builds), so a missing library
# or symbol doesn't fail the link, only the app at run time.
check_bound() {
    local UNBOUND
    UNBOUND=$(nm -m -u "$1" | grep -v "(from " | grep -v "dyld_stub_binder")
    if [ -n "$UNBOUND" ]; then
        echo "$1 has symbols no library provides:"
        echo "$UNBOUND"
        exit 1
    fi
}

# wrap_framework <dylib> <framework folder> <supported platform> <minimum iOS version>
# Copies a dylib into <framework folder>/<name> (the folder's name without .framework), sets its install name to
# @rpath/<name>.framework/<name>, points its references to the other libraries built here at their frameworks, replaces
# Bazel's rpaths (into its own output folders) with @loader_path/.. - the app's Frameworks folder, where the frameworks
# it depends on are - writes the Info.plist and re-signs it ad hoc (the app build signs it properly).
wrap_framework() {
    local DYLIB=$1 FRAMEWORK=$2 PLATFORM=$3 MIN_OS=$4
    local NAME
    NAME=$(basename "$FRAMEWORK" .framework)
    rm -rf "$FRAMEWORK"
    mkdir -p "$FRAMEWORK"
    cp -f "$DYLIB" "$FRAMEWORK/$NAME" || exit 1
    chmod u+w "$FRAMEWORK/$NAME"
    install_name_tool -id "@rpath/$NAME.framework/$NAME" "$FRAMEWORK/$NAME" || exit 1
    local DEP
    for DEP in libLiteRt:LiteRt libGemmaModelConstraintProvider:GemmaModelConstraintProvider; do
        if otool -L "$FRAMEWORK/$NAME" | grep -q "@rpath/${DEP%%:*}.dylib "; then
            install_name_tool -change "@rpath/${DEP%%:*}.dylib" "@rpath/${DEP##*:}.framework/${DEP##*:}" "$FRAMEWORK/$NAME" || exit 1
        fi
    done
    local RPATH
    for RPATH in $(otool -l "$FRAMEWORK/$NAME" | awk '/cmd LC_RPATH/ {getline; getline; print $2}'); do
        install_name_tool -delete_rpath "$RPATH" "$FRAMEWORK/$NAME" || exit 1
    done
    install_name_tool -add_rpath @loader_path/.. "$FRAMEWORK/$NAME" || exit 1
    cat > "$FRAMEWORK/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$NAME</string>
  <key>CFBundleIdentifier</key>
  <string>com.emgu.litert.$NAME</string>
  <key>CFBundleName</key>
  <string>$NAME</string>
  <key>CFBundlePackageType</key>
  <string>FMWK</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>CFBundleSupportedPlatforms</key>
  <array>
    <string>$PLATFORM</string>
  </array>
  <key>MinimumOSVersion</key>
  <string>$MIN_OS</string>
</dict>
</plist>
EOF
    codesign -f -s - "$FRAMEWORK/$NAME" || exit 1
}
