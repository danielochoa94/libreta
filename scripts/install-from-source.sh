#!/usr/bin/env bash
set -euo pipefail

repository="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
application_directory="${LIBRETA_INSTALL_DIR:-${HOME}/.local/lib/libreta}"
command_directory="${LIBRETA_BIN_DIR:-${HOME}/.local/bin}"

if command -v dotnet >/dev/null; then
  dotnet_command="$(command -v dotnet)"
elif [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  dotnet_command="${HOME}/.dotnet/dotnet"
else
  echo "The .NET 10 SDK was not found on PATH or at ~/.dotnet/dotnet; see https://dot.net/download." >&2
  exit 1
fi

mkdir -p "${application_directory}" "${command_directory}"
"${dotnet_command}" publish "${repository}/src/Libreta" \
  --configuration Release \
  --output "${application_directory}"
ln -sfn "${application_directory}/Libreta" "${command_directory}/libreta"
"${command_directory}/libreta" --help

echo
echo "Installed ${command_directory}/libreta"
if [[ ":${PATH}:" != *":${command_directory}:"* ]]; then
  echo "Add ${command_directory} to PATH to run libreta from any directory."
fi