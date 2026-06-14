#!/usr/bin/env bash
set -euo pipefail
GAME_DIR="${1:-/mnt/c/Program Files (x86)/Steam/steamapps/common/Casualties Unknown Demo}"
dotnet build ./src/QoLChinesePatchBootstrap/QoLChinesePatchBootstrap.csproj -c Release -p:GameDir="$GAME_DIR"
dotnet build ./src/QoLChinesePatch/QoLChinesePatch.csproj -c Release -p:GameDir="$GAME_DIR"
rm -rf ./release/QoLChinesePatch
mkdir -p ./release/QoLChinesePatch
cp ./src/QoLChinesePatchBootstrap/bin/Release/QoLChinesePatch.Bootstrap.dll ./release/QoLChinesePatch/
cp ./src/QoLChinesePatch/bin/Release/QoLChinesePatch.dll ./release/QoLChinesePatch/
cp ./translations/*.json ./release/QoLChinesePatch/
(cd ./release && zip -qr ../QoLChinesePatch_release.zip QoLChinesePatch)
echo "Built ./QoLChinesePatch_release.zip"
