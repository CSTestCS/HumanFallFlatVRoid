#!/usr/bin/env bash
# Builds the plugin, the one-click installer and the manual-install zips into ./release
#
# Requirements: .NET SDK 8+ (Linux, macOS or Windows with Git Bash), curl, zip/unzip.
# The game's managed DLLs must be copied into lib/game first (they can't be redistributed):
#   <Steam>/steamapps/common/Human Fall Flat/Human_Data/Managed/{Assembly-CSharp.dll, UnityEngine*.dll}
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"
BEP_VER="5.4.23.2"

if [ ! -f lib/game/Assembly-CSharp.dll ] || [ ! -f lib/game/UnityEngine.CoreModule.dll ]; then
  echo "Missing game assemblies in lib/game. Copy Assembly-CSharp.dll and UnityEngine*.dll from"
  echo "  Human Fall Flat/Human_Data/Managed/  into  lib/game/"
  exit 1
fi
# HumanAPI.dll is optional on newer game versions; create an empty stub reference if absent.
[ -f lib/game/HumanAPI.dll ] || echo "note: lib/game/HumanAPI.dll not found (fine if the build succeeds)"

mkdir -p lib/bepinex
for arch in x86 x64; do
  z="lib/bepinex/BepInEx_win_${arch}_${BEP_VER}.zip"
  [ -f "$z" ] || curl -fsSL -o "$z" "https://github.com/BepInEx/BepInEx/releases/download/v${BEP_VER}/BepInEx_win_${arch}_${BEP_VER}.zip"
done
if [ ! -f lib/bepinex/BepInEx.dll ]; then
  unzip -o -j -q "lib/bepinex/BepInEx_win_x64_${BEP_VER}.zip" "BepInEx/core/BepInEx.dll" "BepInEx/core/0Harmony.dll" -d lib/bepinex
fi

echo "== Building plugin"
dotnet build src/HFFVRoid/HFFVRoid.csproj -c Release -nologo -v q
PLUGIN="src/HFFVRoid/bin/Release/net46/HFFVRoid.dll"

echo "== Building installer"
PAY="src/Installer/obj/payload"
mkdir -p "$PAY"
cp "lib/bepinex/BepInEx_win_x86_${BEP_VER}.zip" "$PAY/BepInEx_x86.zip"
cp "lib/bepinex/BepInEx_win_x64_${BEP_VER}.zip" "$PAY/BepInEx_x64.zip"
cp "$PLUGIN" "$PAY/HFFVRoid.dll"
dotnet build src/Installer/Installer.csproj -c Release -nologo -v q

echo "== Packaging"
rm -rf release && mkdir -p release
cp src/Installer/bin/Release/net472/HFF-VRoid-Installer.exe release/

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
# Plugin-only zip (for people who already have BepInEx)
mkdir -p "$STAGE/plugin/BepInEx/plugins/HFFVRoid" "$STAGE/plugin/BepInEx/VRoid/Import"
cp "$PLUGIN" "$STAGE/plugin/BepInEx/plugins/HFFVRoid/"
cp tools/manual-install.txt "$STAGE/plugin/HFF-VRoid-README.txt"
# Full manual zip: BepInEx (32-bit, same as the Human Fall Flat BepInEx pack) + plugin + config
mkdir -p "$STAGE/full"
unzip -q "lib/bepinex/BepInEx_win_x86_${BEP_VER}.zip" -d "$STAGE/full"
cp -r "$STAGE/plugin/BepInEx" "$STAGE/full/"
mkdir -p "$STAGE/full/BepInEx/config"
cp tools/BepInEx.cfg "$STAGE/full/BepInEx/config/BepInEx.cfg"
cp tools/manual-install.txt "$STAGE/full/HFF-VRoid-README.txt"
(cd "$STAGE/full" && zip -q -r -X "$ROOT/release/HFF-VRoid-manual-install.zip" .)
(cd "$STAGE/plugin" && zip -q -r -X "$ROOT/release/HFF-VRoid-plugin-only.zip" .)

ls -la release
