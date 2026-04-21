#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

NEW_VERSION="${1:-}"

if [[ -z "$NEW_VERSION" ]]; then
    CURRENT=$(grep -m1 '<Version>' "$ROOT_DIR/Yottacast/Yottacast.csproj" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')
    echo "Usage: $0 <version>   (e.g. $0 1.2.0)"
    echo "Current version: $CURRENT"
    exit 1
fi

# Validate semver format x.y.z
if ! [[ "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: version must be x.y.z (e.g. 1.2.0)"
    exit 1
fi

CSPROJ_CORE="$ROOT_DIR/Yottacast.Core/Yottacast.Core.csproj"
CSPROJ_APP="$ROOT_DIR/Yottacast/Yottacast.csproj"

update_version() {
    local file="$1"
    sed -i '' "s|<Version>[^<]*</Version>|<Version>$NEW_VERSION</Version>|" "$file"
    echo "  Updated: $file"
}

CURRENT=$(grep -m1 '<Version>' "$CSPROJ_APP" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')

echo "Bumping $CURRENT -> $NEW_VERSION"
update_version "$CSPROJ_CORE"
update_version "$CSPROJ_APP"
echo "Done."
