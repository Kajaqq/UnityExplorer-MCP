# UniverseLib IL2CPP Interop for BepInEx
function Find-DotNetResolverDirs
{
    $dirs = New-Object System.Collections.Generic.List[string]

    try
    {
        $runtimes = dotnet --list-runtimes 2>$null
        foreach ($line in $runtimes)
        {
            if ($line -match '^Microsoft\.NETCore\.App\s+([^\s]+)\s+\[(.+)\]$')
            {
                $sharedParent = $Matches[2]
                $sharedDir = Split-Path $sharedParent -Parent
                $dotnetRoot = Split-Path $sharedDir -Parent
                $refRoot = Join-Path $dotnetRoot "packs/Microsoft.NETCore.App.Ref"
                if (Test-Path $refRoot)
                {
                    Get-ChildItem $refRoot -Directory |
                        ForEach-Object {
                            $candidate = Join-Path $_.FullName "ref/net6.0"
                            if (Test-Path (Join-Path $candidate "System.Text.Json.dll"))
                            {
                                $dirs.Add($candidate)
                            }
                        }
                }
            }
        }
    } catch
    {
    }

    $nugetRoot = Join-Path $HOME ".nuget/packages"
    foreach ($pattern in @(
            "microsoft.netcore.app.ref/*/ref/net6.0",
            "system.text.json/*/lib/net6.0"
        ))
    {
        $root = Join-Path $nugetRoot $pattern
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                if (Test-Path (Join-Path $_.FullName "System.Text.Json.dll"))
                {
                    $dirs.Add($_.FullName)
                }
            }
    }

    $dirs | Select-Object -Unique
}

dotnet build UniverseLib\src\UniverseLib.sln -c Release_IL2CPP_Interop_BIE

# ----------- BepInEx Unity IL2CPP CoreCLR -----------
dotnet build src/UnityExplorer.sln -c Release_BIE_Unity_Cpp
$Path = "Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR"

# ----------- ILRepack -----------
$IlRepackArgs = @("/target:library")
foreach ($ResolverDir in Find-DotNetResolverDirs)
{
    $IlRepackArgs += "/lib:$ResolverDir"
}
if ($IlRepackArgs.Count -eq 1)
{
    Write-Warning ".NET 6 reference folders not found; ILRepack may fail resolving System.* assemblies"
}
$IlRepackArgs += @(
    "/lib:lib/net472/BepInEx/build647+",
    "/lib:lib/net6/",
    "/lib:lib/interop/",
    "/lib:$Path"
)
$IlRepackArgs += @(
    "/internalize",
    "/out:$Path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll",
    "$Path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll",
    "$Path/mcs.dll",
    "$Path/Tomlet.dll"
)
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows))
{
    & lib/ILRepack.exe @IlRepackArgs
} else
{
    & mono lib/ILRepack.exe @IlRepackArgs
}

# ----------- Cleanup -----------
Remove-Item $Path/Tomlet.dll
Remove-Item $Path/mcs.dll
Remove-Item $Path/Iced.dll
Remove-Item $Path/Il2CppInterop.Common.dll
Remove-Item $Path/Il2CppInterop.Runtime.dll
Remove-Item $Path/Microsoft.Extensions.Logging.Abstractions.dll
Remove-Item $Path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.deps.json
Remove-Item $Path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.pdb
New-Item -Path "$Path" -Name "plugins" -ItemType "directory" -Force
New-Item -Path "$Path" -Name "plugins/sinai-dev-UnityExplorer" -ItemType "directory" -Force
Move-Item -Path $Path/UnityExplorer.BIE.Unity.IL2CPP.CoreCLR.dll -Destination $Path/plugins/sinai-dev-UnityExplorer -Force
Move-Item -Path $Path/UniverseLib.BIE.IL2CPP.Interop.dll -Destination $Path/plugins/sinai-dev-UnityExplorer -Force
# (create zip archive)
Remove-Item $Path/../UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip -ErrorAction SilentlyContinue
compress-archive .\$Path\* $Path/../UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip
