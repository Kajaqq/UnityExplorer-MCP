# Unity Explorer MCP Integration Plan (Updated)

- Updated: 2025-12-19  
- Owner: Unity Explorer MCP (p-unity-explorer-mcp)  
- Goal: Expose Unity Explorer’s runtime capabilities through an in‑process MCP server with **streamable HTTP** transport, making game state and safe controls available as MCP resources, tools, and streams.

This plan merges the original scope, the current implementation snapshot, and the TODO list into a single up‑to‑date document.

### Latest iteration snapshot (2025-12-19)
- IL2CPP MCP host on the Windows host (`http://127.0.0.1:51477` and `/mcp`) now survives malformed JSON-RPC: invalid requests return HTTP 400 with structured errors, and the host stays alive. MainThread dispatch falls back to UniverseLib main-thread invoke when no `SynchronizationContext` is captured.
- Contract suite passes (90 total: 89 passed, 1 skipped) on Space Shooter IL2CPP + Mono via `UE_MCP_DISCOVERY` (including gated ConsoleScripts/Hooks tests when enabled). Inspector CLI smoke and the repo smoke scripts succeed against both hosts. Optional CI hook: `.github/workflows/mcp-inspector-cli.yml` (manual `workflow_dispatch`) can run inspector CLI smoke against provided MCP base URLs.
- win-dev control plane alias surface validated: `McpProxy8083` (mcp-control) exposes the harness tool names; seed the session after a restart with `initialize` that includes `Accept: application/json, text/event-stream` plus a `clientInfo` payload (e.g., `protocolVersion=2024-11-05`, `capabilities={}`) and the `win-dev-vm-ui/State-Tool` + `win-dev-vm-ui/Powershell-Tool` succeed. Logs: `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log`.
- Console scripts parity: `unity://console/scripts` + `unity://console/script?path=...` resources and `ReadConsoleScript` + guarded `WriteConsoleScript`/`DeleteConsoleScript` + `RunConsoleScript` + startup controls (`Get/Write/Set/RunStartupScript`) on both hosts (BOM normalized; 256KB cap). Hooks parity (advanced): discovery + source read/write + enable/disable + HookAdd signature support.
- Clipboard parity: `unity://clipboard` resource + guarded `SetClipboardText`/`SetClipboardObject`/`ClearClipboard` tools on both hosts; contract tests added.
- Freecam parity: `unity://freecam` resource + guarded `SetFreecamEnabled`/`SetFreecamSpeed`/`SetFreecamPose` tools on both hosts; contract tests added.
- Search parity: `SearchSingletons` (`unity://search/singletons`, ids `singleton:{declaringTypeFullName}`) and `SearchStaticClasses`/`ListStaticMembers` (`unity://search/static-classes`, `unity://type/{typeFullName}/static-members`) on both hosts; contract tests added.
- Reflection inspector (read): `ReadStaticMember` + `ListSingletonMembers`/`ReadSingletonMember` tools on both hosts; contract tests added.
- Streams parity: `stream_events` emits deterministic `scenes` + `selection` snapshot notifications on stream open (both hosts); contract tests added.
- Mono `CallMethod` parity validated: tool available on both hosts with allowlist + confirm gating; smoke + contract tests updated.
- Inspector validation is CLI-only: use `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl <url>` (accepts bases with or without `/mcp`) or direct `npx @modelcontextprotocol/inspector --cli` one-liners; the Inspector UI helper is deprecated (see `README-mcp.md`).

---

### Decisions (2025-12-13)

- Mono now ships guarded writes (SetConfig/SetActive/SelectObject/TimeScale) behind `allowWrites` + `requireConfirm`; use the opt-in Mono write smoke when validating on hosts.
- Dropdown Il2Cpp cast crash is guarded; keep watching logs across titles and leave `UeMcpHeadless.dll` disabled.
- Treat inspector validation as a first-class gate: run `tools/Run-McpInspectorCli.ps1` early on both hosts for any wire/schema change.

### Roadmap to Completion (MCP v1)

Single source of truth for open tasks: `plans/unity-explorer-mcp-todo.md`.

Gates (always)
- Inspector validation is CLI-only (`tools/Run-McpInspectorCli.ps1` or direct `npx @modelcontextprotocol/inspector --cli <baseUrl>/mcp ...`).
- Windows host validation happens in the same iteration as any behavior change.
- If a change touches shared query/DTO code, run IL2CPP regression (inspector CLI + smoke + contract tests).

Near-term (next ~10 iterations)
0) DONE: Parallel-work scalability refactor: split `src/Mcp/Dto.cs` into per-feature DTO files.
0.2) DONE: Split MCP code into feature-based folders/files (reduce shared hotspots: `UnityReadTools`/`UnityWriteTools`, `MonoReadTools`/`MonoWriteTools`, `MonoMcpHandlers`).
0.3) DONE: Split large MCP hotspots: `src/Mcp/McpSimpleHttp.cs` (transport) and `tests/dotnet/UnityExplorer.Mcp.ContractTests/JsonRpcContractTests.cs` (contract tests).
1) DONE: Console scripts parity: list + read/write/delete + run + startup controls implemented and validated (guarded; 256KB cap).
2) DONE: Hooks parity (advanced): discovery + source read/write + enable/disable + HookAdd signature support (guarded).
3) DONE: Mono `CallMethod` parity implemented with allowlist gating; smoke + contract tests updated.
3.1) DONE: Harden Test‑VM deploy script (`tools/Update-Mod-Remote.ps1`) with remote staging + IL2CPP/Mono target selection.
4) Stream robustness: add IL2CPP stream write serialization (mirror Mono broadcast gate) and add a stress test.
5) Object Explorer parity: pseudo-scenes (DontDestroyOnLoad/HideAndDontSave DONE; Resources/Assets pending) + hierarchical tree browsing DONE (ListChildren).
6) DONE: Inspector parity (read): component member listing + safe member value reads (bounded).
6.1) Inspector parity (read): reflection inspector reads for non-GameObject objects.
7) Inspector parity (write): expand `SetMember` value-type support + write audit logging.
8) Search parity: singleton search + static class search surfaces.
9) Freecam + Clipboard parity: expose state + guarded controls.
10) Reliability/Ops: win-dev proxy watchdog + document cross-title IL2CPP regression blocker (no candidate; skipped).

## 1) Context & Assumptions

- UnityExplorer lives under `UnityExplorer/` and is injected into Unity games (BepInEx, MelonLoader, standalone).
- Unity Explorer already provides:
  - Scene/Object Explorer (Scene Explorer, Object Search)
  - Inspectors (components, reflection, static/instance, dnSpy integration)
  - C# Console with editor features (line numbers, syntax highlighting, auto-complete, script load/save/compile)
  - Hook Manager
  - Mouse Inspect / Freecam (including UI Inspector Results panel)
  - Log panel with optional Unity debug forwarding and disk logging
  - Time-scale widget and keybinds (lock/pause/speed change)
  - Settings, Clipboard, etc.
- MCP clients (IDEs, agents, inspector tools) connect over HTTP to inspect and, when allowed, control the running game.
- Target: BepInEx 6 CoreCLR IL2CPP (no Mono).
- This is a single-game focused fork, based on the original UnityExplorer project. 
- The game is running Unity 6000.0.77f and uses CoreCLR IL2CPP via a custom fork of BepInEx 6 with source located in `../BepInEx/`.
---

## 2) Architecture & Transport

- **Placement:** In‑process C# server inside UnityExplorer INTEROP builds (CoreCLR IL2CPP + MelonLoader Mono).
- **Server:** `UnityExplorer.Mcp.McpSimpleHttp`
  - Binds to `bindAddress:port` from `mcp.config.json` (defaults: `0.0.0.0:51477`).
  - Discovery file: `%TEMP%/unity-explorer-mcp.json` with:
    - `pid`, `port`, `baseUrl`, `modeHints: ["streamable-http"]`, `startedAt`.
- **Endpoints:**
  - `POST /message`
    - JSON‑RPC 2.0 requests:
      - `initialize`
      - `ping`
      - `list_tools`
      - `call_tool`
      - `read_resource`
      - `stream_events`
  - `GET /read?uri=unity://...`
    - Convenience wrapper over `read_resource` for quick testing.
- **Threading:**
  - All Unity access is marshalled via `MainThread.Run/RunAsync`.
- **Security:**
  - Configurable `bindAddress` and `port`.
  - Writes disabled by default (`allowWrites=false`); guarded tools require explicit opt‑in and often confirmation.

---

## 3) MCP Surface (Resources, Tools, Streams)

### 3.1 Resources (`unity://…`)

Already implemented:

- `unity://status`  
  Status snapshot (`StatusDto`): Unity version, platform, runtime, Explorer version, scenes loaded, ready flag, selection summary.

- `unity://scenes`  
  Paged `Page<SceneDto>`: `{ Id, Name, Index, IsLoaded, RootCount }`.

- `unity://scene/{sceneId}/objects`  
  Paged `Page<ObjectCardDto>`: object cards with id, name, path, tag, layer, active flag, component count.

- `unity://object/{id}`  
  Object details card (id, name, path, basic transform, components summary).

- `unity://object/{id}/components`  
  Paged `Page<ComponentCardDto>`: `{ Type, Summary }`.

- `unity://search?query=&name=&type=&path=&activeOnly=&limit=&offset=`  
  Object search across scenes using Explorer’s object traversal.

- `unity://selection`  
  `SelectionDto`: `{ ActiveId, Items[] }` from Inspector state.

- `unity://camera/active`  
  `CameraInfoDto`: `{ IsFreecam, Name, Fov, Pos, Rot }`; honors the UE Freecam state (uses the freecam or reused game camera when active, falls back to `Camera.main`/first camera, `<none>` when missing).

- `unity://logs/tail?count=`  
  `LogTailDto`: `{ Items: [{ T, Level, Message }, …] }` from MCP log buffer.

- `unity://console/scripts`  
  `Page<ConsoleScriptDto>`: `{ Total, Items: [{ Name, Path }] }` pulled from the console scripts folder.

- `unity://hooks`  
  `Page<HookDto>`: `{ Total, Items: [{ Signature, Enabled }] }` using Harmony-style method signatures (e.g. `System.Void UnityEngine.GameObject::SetActive(System.Boolean)`).

All payloads are DTO‑based (`Dto.cs`), aiming for compact, LLM‑friendly JSON.

### 3.2 Tools

**Read tools** (`UnityReadTools`, all `[McpServerTool]`):

- `GetStatus(ct)` — returns `StatusDto`.
- `ListScenes(limit?, offset?, ct)` — returns `Page<SceneDto>`.
- `ListObjects(sceneId?, name?, type?, activeOnly?, limit?, offset?, ct)` — returns `Page<ObjectCardDto>`.
- `GetObject(id, ct)` — returns object card.
- `GetComponents(objectId, limit?, offset?, ct)` — returns `Page<ComponentCardDto>`.
- `SearchObjects(query?, name?, type?, path?, activeOnly?, limit?, offset?, ct)` — returns `Page<ObjectCardDto>`.
- `GetCameraInfo(ct)` — returns `CameraInfoDto`.
- `MousePick(mode="world", ct)` — returns `PickResultDto` with optional `obj:<instanceId>`.
- `TailLogs(count=200, ct)` — returns `LogTailDto`.
- `GetSelection(ct)` — returns `SelectionDto`.

**Meta / protocol methods** (implemented in `McpSimpleHttp`):

- `initialize`  
  - Accepts MCP `InitializeRequest`.  
  - Returns:
    - `protocolVersion` (currently `"2024-11-05"`),
    - `capabilities` (tools/resources + experimental `streamEvents`),
    - `serverInfo` (`name = "UnityExplorer.Mcp"`, `version`),
    - `instructions` string describing high‑level usage.

- `notifications/initialized`  
  - Accepted and ignored; returns a trivial `result` so inspector doesn’t error.

- `ping`  
  - Simple liveness check; returns empty `{}` result.

- `list_tools`  
  - Returns `{ tools: [{ name, description, inputSchema }, …] }`.  
  - `inputSchema` is a JSON Schema per-tool and per-argument, including enums for constrained arguments such as `MousePick.mode`; inspector should be able to render call forms from this shape.

- `call_tool`  
  - Dispatches to `UnityReadTools` / `UnityWriteTools` via `McpReflection.InvokeToolAsync` using reflection and argument coercion.

- `read_resource`  
  - Dispatches to `McpReflection.ReadResourceAsync` for `unity://...` URIs.

- `stream_events`  
  - Opens a chunked JSON line stream.  
  - Publishes:
    - `notification` events: `log`, `selection`, `scenes`, `scenes_diff`, `inspected_scene`, `tool_result`, etc.
    - Mirrored `result`/`error` payloads for other requests.
  - Stream connections now watch for client disconnects and clean up their slots to avoid leaked streams; reconnect flow covered by contract tests.

Legacy `/sse` endpoint has been removed from the active surface; new clients use only `stream_events`.

### 3.3 Guarded Write Tools

Implemented in `UnityWriteTools` (behind `allowWrites` + `requireConfirm`):

- `SetConfig(allowWrites?, requireConfirm?, restart)`  
  Update `mcp.config.json` and optionally restart `McpHost`.

- `SetActive(objectId, active, confirm = false, ct)`  
  Toggle GameObject active state (`obj:<instanceId>`), with confirmation semantics and error codes (`PermissionDenied`, `ConfirmationRequired`, etc.).

- `SetMember(objectId, componentType, member, jsonValue, confirm = false, ct)`  
  Allowlisted reflection writes for component members (uses `reflectionAllowlistMembers`).

Planned guarded writes (Phase 2+):

- Selection change, basic transform tools, component add/remove, hook operations, console eval, and exporters — all strictly opt‑in and allowlist‑controlled.

---

## 4) Config & Options

Config file: `{ExplorerFolder}/mcp.config.json`:

- `enabled`: start/stop server.
- `bindAddress`: default `"0.0.0.0"`.
- `port`: default `51477` (0 = ephemeral).
- `transportPreference`: `"auto"` (currently effectively `"streamable-http"`).
- `allowWrites`, `requireConfirm`.
- `reflectionAllowlistPath`, `reflectionAllowlistMembers`, `componentAllowlist`.
- `exportRoot`, `logLevel`.

Options panel (planned / partial wiring):

- Toggles:
  - Enable MCP, Allow Writes, Require Confirm.
- Controls:
  - Restart MCP, Copy Endpoint.
- Status:
  - Endpoint and “MCP Status” (Disabled / Enabled & Running).

---

## 5) Testing Strategy & Contract Tests

### 5.1 Dotnet Contract Tests

Project: `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` — these form the technical contract MCP changes must respect.

- JSON‑RPC methods:
  - `ListTools_JsonRpc_Response_If_Server_Available`
  - `ListTools_Includes_InputSchema_For_All_Tools`
  - `CallTool_GetStatus_JsonRpc_Response_If_Server_Available`
  - `ReadResource_Status_JsonRpc_Response_If_Server_Available`
  - `Initialize_JsonRpc_Response_If_Server_Available`
  - `Ping_JsonRpc_Response_If_Server_Available`
  - `StreamEvents_Endpoint_Responds_With_Chunked_Json_When_Server_Available`
  - `StreamEvents_Emits_ToolResult_Notification_When_Tool_Called`
  - `StreamEvents_Notification_Has_Generic_Shape_When_Present`

- HTTP convenience endpoints:
  - `Read_Status_Resource_If_Server_Available` (`/read?uri=unity://status`).
  - `Read_Scenes_If_Server_Available` (`/read?uri=unity://scenes`).

- Resource tests:
  - `Read_Scene_Objects_If_Server_Available`
  - `Read_Selection_If_Server_Available`
  - `Read_Camera_Active_If_Server_Available`
  - `Read_Logs_Tail_If_Server_Available`
  - `Read_Search_If_Server_Available`
  - `Hook_Lifecycle_Add_List_Remove_When_Flag_Enabled` (runs only when `UE_MCP_HOOK_TEST_ENABLED=1`; set `hookAllowlistSignatures` to include a safe type such as `UnityEngine.GameObject` and keep `requireConfirm=true`)

These tests must stay green whenever MCP code is changed; use `pwsh ./tools/Run-McpContractTests.ps1` (Release) locally/CI to run them after a CoreCLR build.

### 5.2 Manual & MCP Inspector Flows

- Build + deploy:
  - `cd UnityExplorer && pwsh ./build-ml-coreclr.ps1`
- Launch game
- Inspector CLI:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477`
  - Or: `npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/list`
- Smoke CLI:
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 20`
  - Falls back to `%TEMP%/unity-explorer-mcp.json` or `UE_MCP_DISCOVERY` when `-BaseUrl` is omitted; runs initialize → notifications/initialized → list_tools → GetStatus/TailLogs → read status/scenes/logs and exits non-zero on errors.
- Test MCP:
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario search|camera|mouse-world|mouse-ui|status|logs|selection|initialize|list_tools|events`
  - `-Scenario custom` reads `req.json`; when run, BaseUrl resolves from `-BaseUrl` or discovery (default `http://127.0.0.1:51477`); from the Linux dev machine, target `http://127.0.0.1:51477` instead; `-StreamLines` controls how many `stream_events` chunks are printed.

---

## Release Checklist

Single source of truth for release gating. Run from repo root unless noted.

- Clean working tree: `git status` should be clean; stash/commit local WIP first.
- Restart the games on the VM after deploy
- Gates (Linux dev box, against Windows host):
  - Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477` and `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51478`.
  - Smokes (with guarded writes enabled): `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 20 -EnableWriteSmoke`.
  - Contract tests (run once per host with matching discovery): `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json pwsh ./tools/Run-McpContractTests.ps1`
- Optional CI: `.github/workflows/mcp-inspector-cli.yml` supports `workflow_dispatch` to re-run Inspector CLI smoke against provided base URLs.
- Tagging (documented only): suggested tag `ue-mcp-0.1.0` via `git tag -a ue-mcp-0.1.0 -m "UnityExplorer MCP 0.1.0"` then `git push origin ue-mcp-0.1.0`.

## 6) Roadmap / Remaining Work

Fine‑grained TODOs live in `plans/unity-explorer-mcp-todo.md`. High‑level themes:

- CoreCLR IL2CPP and Mono (`UnityExplorer.MelonLoader.Mono`) are first-class MCP hosts; parity work is tracked in TODO sections 11–14. Mono host work followed a three-phase path (A/B/C) and is now in Phase C parity + tests; remaining gaps should be closed with the same contract-test + inspector-CLI gates as IL2CPP.

1. **Transport & protocol polish**
   - Remove SSE leftovers, refine error codes, add light rate limiting.
2. **Read‑only parity**
   - Finish any gaps between implemented DTOs and the conceptual spec in `mcp-interface-concept.md`, including live validation of Freecam camera info and harness coverage for SearchObjects/MousePick on Space Shooter.
3. **Streams & notifications**
   - Document events, add tests for non‑tool notifications (logs, scenes, selection).
4. **Guarded write tools**
   - Solidify `SetActive`/`SetMember` and design next tier of safe mutations.
5. **Console & hooks**
   - ConsoleEval and hook list/add/remove are implemented; hook lifecycle contract tests are gated by `UE_MCP_HOOK_TEST_ENABLED` and expect `hookAllowlistSignatures` entries.
6. **Config**
   - MCP tools around `McpConfig` and recommended configs per environment.
7. **Inspector & DX**
   - Ensure all tools/resources have stable schemas; add CLI smoke scripts.
8. **Docs & AGENTS.md**
   - Finalize `README-mcp.md` and a short MCP quick‑start in `UnityExplorer/AGENTS.md`.

---

## 7) References

- Concept draft: `plans/mcp-interface-concept.md`
- TODOs: `plans/unity-explorer-mcp-todo.md`
- Non-MCP runtime feature summary: `docs/unity-explorer-game-interaction.md`
- Code:
  - `UnityExplorer/src/Mcp/*`
  - `UnityExplorer/src/ExplorerCore.cs`
  - `UnityExplorer/tools/*`
  - `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests/*`

This plan is now the single source of truth for current design and future work; other `plans/*` files provide historical context and deeper detail.
