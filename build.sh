#!/usr/bin/env bash
set -euo pipefail

remove_files() {
  local dir="$1"
  shift

  local file
  for file in "$@"; do
    rm -f "$dir/$file"
  done
}

zip_dir_contents() {
  local dir="$1"
  local zip_name="$2"
  local archive="$(dirname "$dir")/$zip_name"

  rm -f "$archive"
  (cd "$dir" && zip -qr "../$zip_name" .)
}

dotnet build .\src\UniverseLib.sln -c Release_IL2CPP_Interop_BIE

# ----------- BepInEx Unity IL2CPP CoreCLR -----------
dotnet build src/UnityExplorer.sln -c Release_BIE_Unity_Cpp
path="Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR"
# ILRepack
mono lib/ILRepack.exe /target:library /lib:lib/net472/BepInEx/build647+ /lib:lib/net6/ /lib:lib/interop/ /lib:"$path" /internalize /out:"$path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll" "$path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll" "$path/mcs.dll" "$path/Tomlet.dll"
# cleanup and move files
remove_files "$path" \
  Tomlet.dll \
  mcs.dll \
  Iced.dll \
  Il2CppInterop.Common.dll \
  Il2CppInterop.Runtime.dll \
  Microsoft.Extensions.Logging.Abstractions.dll \
  UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.deps.json \
  UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.pdb
mkdir -p "$path/plugins"
mkdir -p "$path/plugins/sinai-dev-UnityExplorer"
mv "$path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll" "$path/plugins/sinai-dev-UnityExplorer"
mv "$path/UniverseLib.BIE.IL2CPP.Interop.dll" "$path/plugins/sinai-dev-UnityExplorer"
# create zip archive
zip_dir_contents "$path" UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip
