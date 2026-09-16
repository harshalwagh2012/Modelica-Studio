#!/usr/bin/env sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_directory=$(dirname "$script_directory")
runtime_identifier=${1:-osx-arm64}
configuration=${2:-Release}
publish_directory="$repository_directory/artifacts/publish/$runtime_identifier"
bundle_directory="$repository_directory/artifacts/Modelica Studio.app"

"$script_directory/dotnet.sh" restore "$repository_directory/src/ModelicaStudio.Desktop" \
  --runtime "$runtime_identifier" \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -m:1 \
  -nr:false

"$script_directory/dotnet.sh" publish "$repository_directory/src/ModelicaStudio.Desktop" \
  --configuration "$configuration" \
  --runtime "$runtime_identifier" \
  --self-contained true \
  --output "$publish_directory" \
  --no-restore \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -m:1 \
  -nr:false

mkdir -p "$bundle_directory/Contents/MacOS" "$bundle_directory/Contents/Resources"
ditto "$publish_directory" "$bundle_directory/Contents/MacOS"
cp "$repository_directory/packaging/macos/Info.plist" "$bundle_directory/Contents/Info.plist"
chmod +x "$bundle_directory/Contents/MacOS/ModelicaStudio.Desktop"

printf '%s\n' "$bundle_directory"
