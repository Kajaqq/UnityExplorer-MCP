# Linux Build Guide 

This guide explains how to reproduce the upstream Windows-style release packages on Linux, including ILRepack and zipped artifacts.

> Goal: run `build.sh` on Linux and produce release `.zip` files under `Release/`, suitable for direct distribution or installation on Windows.

## 1. Prerequisites

On the Linux machine, the following prerequisites are required:
- `.NET SDK` 8.0 or newer  
- `git`
- `PowerShell` (pwsh) –  only for helper scripts
- Mono runtime – only to execute the Windows ILRepack binary (`lib/ILRepack.exe`) during packaging
- `zip` 

Agents should document commands (e.g., `sudo apt-get install dotnet-sdk-8.0 powershell mono-complete` on Ubuntu) for the user to execute.

## 2. Running the PowerShell build script

The root `build.sh` drives the BepInEx 6 Unity IL2CPP package:

- `UniverseLib` builds 
- UnityExplorer solution build for `Release_BIE_Unity_Cpp`
- ILRepack runs to merge support assemblies into single DLLs
- Cleanup and layout of the BepInEx `plugins/` folder
- Zipping into release archives in `Release/`


### 2.1 ILRepack on Linux

`build.sh` uses Mono to execute `lib/ILRepack.exe` as it is a Windows/.NET PE file.

## 3. Expected outputs

After a successful run, you should see a release archive named:

- `Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip`

The zip contains:

- A `plugins/` folder with the primary UnityExplorer DLL for BepInEx.
- Within the `plugins/` folder, supporting libraries (UniverseLib, etc.) are already arranged as expected by the loader.

These archives should be directly usable on Windows following the installation instructions in `README.md`.

## 4. Testing Linux-packaged releases on Windows

1. Copy a generated `.zip` from `Release/` to a Windows machine.
2. Extract it.
3. Follow the relevant section of `README.md` to install.
4. Launch a Unity game and verify that:
   - UnityExplorer UI appears.
   - MCP server starts and writes its discovery file.

If the release works as expected, your Linux-built packages are functionally equivalent to Windows-built ones.

## 5. Notes and maintenance tips for agents

- Do not remove or rename existing configurations without updating:
  - `src/UnityExplorer.csproj`
  - `src/UnityExplorer.sln`
- If new build configurations are added in the future, extend this document with the extra build and packaging steps.
- After the build completes, ask the human operator to verify the output and run the release on a Windows machine to confirm functionality.
