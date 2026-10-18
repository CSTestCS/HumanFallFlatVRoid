#!/usr/bin/env bash
# Runs the automated tests:
#  - LoaderTest: the real VRM loader + retargeting code against public sample VRMs (VRM 0.x and 1.0),
#    using a small Unity API stand-in. Checks skinning data, humanoid bones, spring bones and that a VRM
#    follows a posed "ragdoll" skeleton.
#  - InstallerTest: the installer's file logic against fake 32/64-bit game folders (needs tools/build.sh first).
set -euo pipefail
cd "$(dirname "$0")/.."
SAMPLES="tests/samples"
mkdir -p "$SAMPLES"
if [ ! -d "$SAMPLES/univrm" ]; then
  git clone -q --depth 1 --filter=blob:none --sparse https://github.com/vrm-c/UniVRM.git "$SAMPLES/univrm"
  git -C "$SAMPLES/univrm" sparse-checkout set Tests/Models
fi
if [ ! -d "$SAMPLES/spec" ]; then
  git clone -q --depth 1 --filter=blob:none --sparse https://github.com/vrm-c/vrm-specification.git "$SAMPLES/spec"
  git -C "$SAMPLES/spec" sparse-checkout set samples
fi
A="$SAMPLES/univrm/Tests/Models/Alicia_vrm-0.51/AliciaSolid_vrm-0.51.vrm"
B="$SAMPLES/spec/samples/Seed-san/vrm/Seed-san.vrm"
C="$SAMPLES/spec/samples/VRM1_Constraint_Twist_Sample/vrm/VRM1_Constraint_Twist_Sample.vrm"
D="$SAMPLES/univrm/Tests/Models/Materials/EmissionMigration_v0.107.0.vrm"

dotnet build tests/LoaderTest/LoaderTest.csproj -c Release -nologo -v q -o tests/LoaderTest/out
for pair in "$A $B" "$B $A" "$C $D" "$D $C" "$A $C"; do
  dotnet tests/LoaderTest/out/LoaderTest.dll $pair
done

if [ -f src/Installer/obj/payload/HFFVRoid.dll ]; then
  dotnet build tests/InstallerTest/InstallerTest.csproj -c Release -nologo -v q -o tests/InstallerTest/out
  dotnet tests/InstallerTest/out/InstallerTest.dll src/Installer/obj/payload
else
  echo "Skipping InstallerTest (run tools/build.sh first)."
fi
