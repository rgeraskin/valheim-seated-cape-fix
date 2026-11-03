#!/usr/bin/env bash
# Makes the Thunderstore package dist/SeatedCapeFix-<version>.zip from the Release build (mise run package).
set -euo pipefail
cd "$(dirname "$0")/.."
version=$(python3 -c 'import json;print(json.load(open("package/manifest.json"))["version_number"])')
plugin=$(sed -n 's/.*public const string Version = "\(.*\)";.*/\1/p' SeatedCapeFix/src/Plugin.cs)
if [ "$version" != "$plugin" ]; then
  echo "package/manifest.json says $version, Plugin.cs says $plugin" >&2
  exit 1
fi
rm -rf dist/pkg dist/SeatedCapeFix-*.zip && mkdir -p dist/pkg/plugins
cp package/manifest.json package/icon.png README.md CHANGELOG.md dist/pkg/
cp SeatedCapeFix/bin/Release/SeatedCapeFix.dll dist/pkg/plugins/
(cd dist/pkg && zip -qr "../SeatedCapeFix-$version.zip" .)
echo "dist/SeatedCapeFix-$version.zip"
