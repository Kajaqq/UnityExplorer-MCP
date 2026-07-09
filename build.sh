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

normalize_path() {
  local path="$1"
  if command -v cygpath >/dev/null 2>&1 && [[ "$path" =~ ^[A-Za-z]:\\ ]]; then
    cygpath -u "$path"
    return
  fi

  printf '%s\n' "$path"
}

find_dotnet_resolver_dirs() {
  local roots=()
  local selected=()

  if command -v dotnet >/dev/null 2>&1; then
    while IFS='|' read -r _version parent; do
      parent="$(normalize_path "$parent")"
      local root="${parent%/shared/Microsoft.NETCore.App}"
      if [[ "$root" != "$parent" ]]; then
        roots+=("$root")
      fi
    done < <(dotnet --list-runtimes 2>/dev/null | sed -n 's/^Microsoft\.NETCore\.App \([^ ]*\) \[\(.*\)\]$/\1|\2/p')
  fi

  roots+=("$HOME/.dotnet" "/usr/share/dotnet" "/c/Program Files/dotnet")

  local root candidate
  for root in "${roots[@]}"; do
    for candidate in "$root"/packs/Microsoft.NETCore.App.Ref/*/ref/net6.0; do
      if [[ -f "$candidate/System.Text.Json.dll" ]]; then
        selected+=("$candidate")
      fi
    done
  done

  for candidate in "$HOME"/.nuget/packages/microsoft.netcore.app.ref/*/ref/net6.0 "$HOME"/.nuget/packages/system.text.json/*/lib/net6.0; do
    if [[ -f "$candidate/System.Text.Json.dll" ]]; then
      selected+=("$candidate")
    fi
  done

  printf '%s\n' "${selected[@]}" | awk 'NF && !seen[$0]++'
}

# ----------- UniverseLib IL2CPP Interop -----------
dotnet build ./UniverseLib/src/UniverseLib.sln -c Release_IL2CPP_Interop_BIE

# ----------- BepInEx Unity IL2CPP CoreCLR -----------
dotnet build ./src/UnityExplorer.sln -c Release_BIE_Unity_Cpp
path="Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR"

# ----------- ILRepack -----------
ilrepack_args=(/target:library)
dotnet_resolver_found=0
while IFS= read -r dotnet_resolver_dir; do
  [[ -z "$dotnet_resolver_dir" ]] && continue
  ilrepack_args+=(/lib:"$dotnet_resolver_dir")
  dotnet_resolver_found=1
done < <(find_dotnet_resolver_dirs || true)
if [[ "$dotnet_resolver_found" -eq 0 ]]; then
  echo "warning: .NET 6 reference folders not found; ILRepack may fail resolving System.* assemblies" >&2
fi
ilrepack_args+=(/lib:lib/net472/BepInEx/build647+ /lib:lib/net6/ /lib:lib/interop/ /lib:"$path")
mono ./lib/ILRepack.exe "${ilrepack_args[@]}" /internalize /out:"$path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll" "$path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll" "$path/mcs.dll" "$path/Tomlet.dll"

# ----------- Cleanup -----------
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
# Create zip archive
zip_dir_contents "$path" UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip
