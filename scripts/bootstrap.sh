#!/usr/bin/env bash
# One-time setup. Run inside `nix develop`. Generates the Qt Bridge project from the
# official template so the csproj is exactly what the bridge expects, then keeps our
# Program.cs / Main.qml on top of it.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet new install QtGroup.Qt.Bridge.CSharp.Templates

tmp=$(mktemp -d)
dotnet new qt -n Player.App -o "$tmp" --Framework net9.0
cp -n -r "$tmp"/. src/Player.App/      # -n: never overwrite our own files
rm -rf "$tmp"

[ -e Player.sln ] || [ -e Player.slnx ] || dotnet new sln -n Player
dotnet sln add src/Player.Core/Player.Core.fsproj src/Player.Audio src/Player.App
dotnet add src/Player.Audio reference src/Player.Core/Player.Core.fsproj
dotnet add src/Player.App reference src/Player.Core/Player.Core.fsproj src/Player.Audio

echo
echo "How the template registers QML files (MButton.qml must be picked up the same way as Main.qml):"
grep -n -i "qml" src/Player.App/Player.App.csproj || true
echo
echo "Done. Run: dotnet run --project src/Player.App"
