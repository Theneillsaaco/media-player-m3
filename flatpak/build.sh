#!/usr/bin/env bash
# Genera las listas de dependencias offline la primera vez, y luego compila e instala el Flatpak.
# Ejecutar desde cualquier sitio: se mueve solo a flatpak/.
set -euo pipefail
cd "$(dirname "$0")"

APP=io.github.Theneillsaaco.MediaPlayer
KDE=6.11
FDO=26.08   # rama de freedesktop sobre la que va KDE $KDE (y la extensión dotnet10)

flatpak install -y --noninteractive --user flathub \
  org.kde.Platform//$KDE org.kde.Sdk//$KDE org.freedesktop.Sdk.Extension.dotnet10//$FDO

[ -d flatpak-builder-tools ] || git clone --depth 1 https://github.com/flatpak/flatpak-builder-tools.git

# venv con lo que necesitan los generadores (el nix-shell no trae 'packaging')
if [ ! -d .venv ]; then
  python3 -m venv .venv
  .venv/bin/pip install --quiet packaging requirements-parser setuptools aiohttp toml
fi

# Paquetes NuGet (incluido Qt Bridge) para compilar sin red -> flatpak/nuget-sources.json
if [ ! -f nuget-sources.json ]; then
  .venv/bin/python flatpak-builder-tools/dotnet/flatpak-dotnet-generator.py \
    --dotnet 10 --freedesktop "$FDO" \
    nuget-sources.json ../src/Player.App/Player.App.csproj
fi

# Paquetes Python del sidecar -> flatpak/python3-modules.json
if [ ! -f python3-modules.json ]; then
  .venv/bin/python flatpak-builder-tools/pip/flatpak-pip-generator.py \
    --runtime "org.kde.Sdk//$KDE" --output python3-modules ytmusicapi yt-dlp
fi

flatpak-builder --user --install --force-clean build-dir "$APP.yml"
echo "Listo: flatpak run $APP"