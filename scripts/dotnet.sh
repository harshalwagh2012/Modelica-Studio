#!/usr/bin/env sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_directory=$(dirname "$script_directory")

if [ -x "$repository_directory/.dotnet/dotnet" ]; then
  AVALONIA_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_HOME="$repository_directory/.dotnet-home" \
    NUGET_PACKAGES="$repository_directory/.nuget/packages" \
    exec "$repository_directory/.dotnet/dotnet" "$@"
fi

AVALONIA_TELEMETRY_OPTOUT=1 \
  DOTNET_CLI_HOME="$repository_directory/.dotnet-home" \
  NUGET_PACKAGES="$repository_directory/.nuget/packages" \
  exec dotnet "$@"
