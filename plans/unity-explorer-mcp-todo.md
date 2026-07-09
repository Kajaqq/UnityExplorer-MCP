# Unity Explorer MCP TODOs - BepInEx 6 Unity IL2CPP Fork

Date: 2026-07-07

## Scope

This fork targets one Unity 6 game through BepInEx 6 CoreCLR IL2CPP.

- Unity: `6000.0.77f`
- Loader/runtime: BepInEx 6 CoreCLR IL2CPP
- Solution build target: `Release_BIE_Unity_Cpp`
- Project configuration behind that solution target: `BIE_Unity_Cpp_CoreCLR`
- MCP host: IL2CPP on `http://127.0.0.1:51477`

Mono, net35, MelonLoader, and cross-loader feature parity are out of scope. Do not spend time preserving those paths unless the user explicitly changes the fork scope.

## Current Status

The broad MCP surface is implemented for the IL2CPP target: status, scenes, objects, search, selection, logs, camera/freecam, clipboard, console scripts, hooks, reflection reads/writes, streams, and guarded write tools.

Progress should be tracked as compatibility issues for the target Unity 6 BepInEx 6 IL2CPP game.

## Definition of Done

- `Release_BIE_Unity_Cpp` builds.
- The target Unity 6 game launches with UnityExplorer under BepInEx 6 IL2CPP.
- MCP starts and responds on the IL2CPP host.
- Inspector CLI works end to end:
  - `initialize`
  - `notifications/initialized`
  - `list_tools`
  - `call_tool` for at least `GetStatus` and `TailLogs`
  - `read_resource` for status/scenes/objects/search/selection/logs
  - `stream_events` receives deterministic startup snapshots.
- Smoke CLI succeeds against the running game.
- Contract tests pass where a running IL2CPP host is available.
- Docs and DTO contract stay in sync when MCP shapes change.

## Validation Commands

```powershell
pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477
pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 20
pwsh ./tools/Run-McpContractTests.ps1
```

Build/package:

```bash
./build.sh
```

The `mono` command used by `build.sh` is only for running the Windows `lib/ILRepack.exe` build tool on Linux. It is not a supported runtime target for this fork.

## Active IL2CPP Work Areas

- [ ] Verify `Release_BIE_Unity_Cpp` remains the documented build target everywhere.
- [ ] Reproduce and fix compatibility issues in the target Unity 6 game.
- [ ] Keep IL2CPP MCP transport stable after malformed JSON-RPC and client disconnects.
- [ ] Keep guarded writes safe: default `allowWrites=false`, confirmation required for mutating tools, reset config after write smoke.
- [ ] Keep `docs/mcp-interface-specs.md`, DTOs, README examples, and contract tests synchronized for any MCP shape changes.

## Agent Reminders
- If touching shared files that also compile in unused runtimes, prioritize IL2CPP correctness over Mono compatibility.
