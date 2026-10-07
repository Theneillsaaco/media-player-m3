#!/usr/bin/env bash
# Run once if you already ran bootstrap.sh with the C# Player.Core: swaps it for the F# one.
set -euo pipefail
cd "$(dirname "$0")/.."

old=src/Player.Core/Player.Core.csproj
new=src/Player.Core/Player.Core.fsproj

dotnet sln remove "$old" 2>/dev/null || true
for p in src/Player.Audio src/Player.App; do
  dotnet remove "$p" reference "$old" 2>/dev/null || true
  dotnet add "$p" reference "$new" 2>/dev/null || true
done
rm -f "$old" src/Player.Core/IAudioEngine.cs
dotnet sln add "$new"
echo "Core is F# now. Run: dotnet run --project src/Player.App"
