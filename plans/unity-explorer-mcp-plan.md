# Unity Explorer MCP Integration Plan - BepInEx 6 Unity IL2CPP Fork

Updated: 2026-07-07

## Goal

Improve compatibility for a specific Unity 6 game running UnityExplorer through BepInEx 6 CoreCLR IL2CPP, while keeping the in-process MCP surface useful for inspection and safe runtime control.

This fork does not aim for cross-loader parity. Mono, net35, and MelonLoader are out of scope and may lag or break while improving the target IL2CPP game.

## Target

- Unity: `6000.0.77f`
- Loader/runtime: BepInEx 6 CoreCLR IL2CPP
- BepInEx source: `../BepInEx/`
- Solution build target: `Release_BIE_Unity_Cpp`
- Project configuration behind that target: `BIE_Unity_Cpp_CoreCLR`
- MCP endpoint: `http://127.0.0.1:51477/mcp`

## Current Snapshot

The IL2CPP MCP host exposes the main UnityExplorer runtime surface:

- Status, version, scenes, build scenes, objects, children, components, and component members.
- Search surfaces for objects, singletons, static classes, and static members.
- Selection, logs, camera, Freecam, Clipboard, time scale, console scripts, hooks, and test UI helpers.
- Guarded writes through config, object state, reflection/member writes, components, hierarchy, scenes, clipboard, Freecam, console, hooks, and time scale.
- Streamable HTTP with JSON-RPC on `POST /message` and `POST /mcp`, convenience `GET /read?uri=...`, and `stream_events`.
- Structured JSON-RPC errors and tool-level `{ ok: false, error: ... }` failures.

Use `docs/mcp-interface-specs.md` as the contract source for exact DTOs, resources, tools, stream events, and error shapes.

## Architecture

- The MCP server runs in-process inside the UnityExplorer IL2CPP build.
- Server implementation: `UnityExplorer.Mcp.McpSimpleHttp`.
- IL2CPP transport files live under `src/Mcp/Transport/Interop/McpSimpleHttp.*.cs`.
- IL2CPP feature implementations live under `src/Mcp/Features/<FeatureName>/Interop/`.
- DTOs live under `src/Mcp/Dto/`.
- Unity API access must be marshalled through the main-thread helpers.

## Security And Safety

- Reads are enabled by default.
- Writes are disabled by default with `allowWrites=false`.
- Mutating tools must respect `allowWrites`; tools that support confirmation must respect `requireConfirm`.
- Console execution remains guarded by `enableConsoleEval`.
- Allowlists must be enforced for reflection, component, and hook-style operations where applicable.
- Any write smoke that enables unsafe options must reset them before finishing.

## Build

Preferred fork build:

```bash
./build.sh
```

Direct solution build:

```bash
dotnet build src/UnityExplorer.sln -c Release_BIE_Unity_Cpp
```

`Release_BIE_Unity_Cpp` is the solution configuration. It maps to the project configuration `BIE_Unity_Cpp_CoreCLR`.

## Validation

Run against the target IL2CPP host when the game is running:

```powershell
pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477
pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 20
pwsh ./tools/Run-McpContractTests.ps1
```

Release/compatibility gate:
- Clean or understood working tree.
- Build succeeds for `Release_BIE_Unity_Cpp`.
- Target Unity 6 game launches with UnityExplorer loaded.
- MCP handshake succeeds.
- Inspector CLI and smoke pass on `51477`.
- Contract tests pass when the target host is available.

## Active Roadmap

Open work is tracked in `plans/unity-explorer-mcp-todo.md`.

Current priorities:
1. Fix target-game compatibility regressions on Unity 6 + BepInEx 6 IL2CPP.
2. Preserve IL2CPP MCP protocol stability.
3. Keep docs, DTOs, examples, and tests synchronized for MCP contract changes.

## References

- Interface contract: `docs/mcp-interface-specs.md`
- Active TODOs: `plans/unity-explorer-mcp-todo.md`
- User quickstart: `README-mcp.md`
- Native UnityExplorer capability summary: `docs/unity-explorer-game-interaction.md`
- IL2CPP MCP code:
  - `src/Mcp/Dto/*`
  - `src/Mcp/Features/*/Interop/*`
  - `src/Mcp/Transport/Interop/*`
  - `src/Mcp/UnityReadTools.cs`
  - `src/Mcp/UnityWriteTools.cs`
  - `tests/dotnet/UnityExplorer.Mcp.ContractTests/*`
