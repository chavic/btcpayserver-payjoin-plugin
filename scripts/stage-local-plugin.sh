#!/usr/bin/env bash
set -euo pipefail

PLUGIN_ID="BTCPayServer.Plugins.Payjoin"
CONFIGURATION="Debug"
PLUGINS_ROOT="${BTCPAY_PLUGIN_DIR:-}"
SETTINGS_CONFIG="${BTCPAY_SETTINGS_CONFIG:-}"

usage() {
    cat <<'EOF'
Stage the current BTCPay Payjoin plugin build into BTCPay's external plugin directory.

Usage:
  bash ./scripts/stage-local-plugin.sh [--configuration Debug|Release] [--plugins-root PATH] [--settings-config PATH]

Options:
  --configuration VALUE   Build configuration to stage. Default: Debug
  --plugins-root PATH     BTCPay external plugins root directory (the value of BTCPay's plugindir setting)
  --settings-config PATH  Path to a BTCPay settings.config file to read plugindir from
  --help                  Show this message

Environment:
  BTCPAY_PLUGIN_DIR       Overrides the plugins root directory
  BTCPAY_SETTINGS_CONFIG  Overrides the settings.config path
EOF
}

fail() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

normalize_path() {
    local path="$1"

    if [[ $path == "~"* ]]; then
        path="${HOME}${path#\~}"
    fi

    if command -v cygpath >/dev/null 2>&1 && [[ $path =~ ^[A-Za-z]:\\ ]]; then
        cygpath -u "$path"
        return
    fi

    printf '%s\n' "${path//\\//}"
}

extract_plugins_root_from_settings() {
    local config_path="$1"

    [[ -f $config_path ]] || fail "settings.config not found: $config_path"

    awk '
        /^[[:space:]]*#/ { next }
        /^[[:space:]]*plugindir[[:space:]]*=/ {
            sub(/^[[:space:]]*plugindir[[:space:]]*=/, "")
            gsub(/^[[:space:]]+|[[:space:]]+$/, "")
            print
            exit
        }
    ' "$config_path"
}

default_plugins_root() {
    local os
    os="$(uname -s)"

    case "$os" in
        Linux|Darwin)
            [[ -n ${HOME:-} ]] || fail "HOME is not set"
            printf '%s\n' "$HOME/.btcpayserver/Plugins"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            [[ -n ${APPDATA:-} ]] || fail "APPDATA is not set"
            normalize_path "${APPDATA}\\BTCPayServer\\Plugins"
            ;;
        *)
            fail "Unsupported OS: $os"
            ;;
    esac
}

hash_file() {
    local path="$1"

    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$path" | awk '{ print $1 }'
        return
    fi

    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$path" | awk '{ print $1 }'
        return
    fi

    fail "Neither sha256sum nor shasum is available"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            [[ $# -ge 2 ]] || fail "--configuration requires a value"
            CONFIGURATION="$2"
            shift 2
            ;;
        --plugins-root)
            [[ $# -ge 2 ]] || fail "--plugins-root requires a value"
            PLUGINS_ROOT="$2"
            shift 2
            ;;
        --settings-config)
            [[ $# -ge 2 ]] || fail "--settings-config requires a value"
            SETTINGS_CONFIG="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: $1"
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_DIR="$REPO_ROOT/$PLUGIN_ID/bin/$CONFIGURATION/net8.0"

OS="$(uname -s)"
case "$OS" in
    Linux)
        LIBNAME="libpayjoin_ffi.so"
        ;;
    Darwin)
        LIBNAME="libpayjoin_ffi.dylib"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        LIBNAME="payjoin_ffi.dll"
        ;;
    *)
        fail "Unsupported OS: $OS"
        ;;
esac

if [[ -n $PLUGINS_ROOT ]]; then
    PLUGINS_ROOT="$(normalize_path "$PLUGINS_ROOT")"
elif [[ -n $SETTINGS_CONFIG ]]; then
    PLUGINS_ROOT="$(extract_plugins_root_from_settings "$SETTINGS_CONFIG")"
    if [[ -n $PLUGINS_ROOT ]]; then
        PLUGINS_ROOT="$(normalize_path "$PLUGINS_ROOT")"
    else
        PLUGINS_ROOT="$(default_plugins_root)"
    fi
else
    PLUGINS_ROOT="$(default_plugins_root)"
fi

TARGET_PLUGIN_DIR="$PLUGINS_ROOT/$PLUGIN_ID"

[[ -d $SOURCE_DIR ]] || fail "Build output directory not found: $SOURCE_DIR"

required_files=(
    "$PLUGIN_ID.dll"
    "$PLUGIN_ID.deps.json"
    "$LIBNAME"
)

optional_files=(
    "$PLUGIN_ID.pdb"
)

for file in "${required_files[@]}"; do
    [[ -f "$SOURCE_DIR/$file" ]] || fail "Required build artifact is missing: $SOURCE_DIR/$file"
done

rm -rf "$TARGET_PLUGIN_DIR"
mkdir -p "$TARGET_PLUGIN_DIR"

for file in "${required_files[@]}"; do
    cp "$SOURCE_DIR/$file" "$TARGET_PLUGIN_DIR/$file"
done

for file in "${optional_files[@]}"; do
    if [[ -f "$SOURCE_DIR/$file" ]]; then
        cp "$SOURCE_DIR/$file" "$TARGET_PLUGIN_DIR/$file"
    fi
done

printf 'Staged %s\n' "$PLUGIN_ID"
printf '  Configuration: %s\n' "$CONFIGURATION"
printf '  Source: %s\n' "$SOURCE_DIR"
printf '  Plugins root: %s\n' "$PLUGINS_ROOT"
printf '  Target: %s\n' "$TARGET_PLUGIN_DIR"
printf '  Files:\n'

for file in "${required_files[@]}" "${optional_files[@]}"; do
    if [[ -f "$TARGET_PLUGIN_DIR/$file" ]]; then
        printf '    - %s\n' "$file"
    fi
done

printf '  Hashes:\n'
printf '    - %s: %s\n' "$PLUGIN_ID.dll" "$(hash_file "$TARGET_PLUGIN_DIR/$PLUGIN_ID.dll")"
printf '    - %s: %s\n' "$LIBNAME" "$(hash_file "$TARGET_PLUGIN_DIR/$LIBNAME")"
printf 'Restart BTCPay to load the staged plugin.\n'
