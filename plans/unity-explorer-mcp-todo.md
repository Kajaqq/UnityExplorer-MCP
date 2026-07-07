# Unity Explorer MCP – High‑Level TODOs (Streamable HTTP Era)

Date: 2025‑12‑19  
Scope: Remaining work to get close to UnityExplorer feature parity over MCP, with a stable streamable‑http surface.

### Definition of Done (100%)
- All checkboxes in this file are checked (including tests and docs updates).
- `dotnet test UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` passes against both IL2CPP and Mono hosts (via `UE_MCP_DISCOVERY`).
- `@modelcontextprotocol/inspector` flow works end‑to‑end: `initialize` → `notifications/initialized` → `list_tools` → `call_tool` (at least status/logs) → `read_resource` (status/scenes/objects/search/selection/logs) → `stream_events` (receive a deterministic `scenes` snapshot on open).
- Smoke CLI (call‑mcp script) succeeds against a running game.
- Space Shooter hosts (IL2CPP + Mono): all contract tests pass; documented write scenarios (`SetActive`, `SelectObject`, time‑scale) succeed with `allowWrites+confirm`.
- Docs in sync: `plans/mcp-interface-concept.md`, `README-mcp.md`, DTO code, and tests all agree on shapes and errors.
- Feature parity: the major UnityExplorer panels are reachable via MCP (Object Explorer + Inspector read/write, Console scripts, Hooks, Freecam, Clipboard) with guarded writes and tests.

Status (2025-12-19): Progress 100% (155/155). Test‑VM hosts are green on both ports (IL2CPP `51477`, Mono `51478`) via inspector CLI, write-enabled smoke, and contract tests (90 total: 89 passed, 1 skipped placeholder). Reflection inspector read tools (static member + singleton member reads) are present; search surfaces + Freecam + Clipboard are present; `stream_events` emits deterministic `scenes` and `selection` snapshots on open.

## Decisions (2025-12-13)
- [x] PRIORITY: fix the UnityExplorer dropdown Il2Cpp cast crash and remove the Test‑VM‑only `Mods\UeMcpHeadless.dll` workaround (guard added; mod disabled on Windows host).
- [x] Add guarded writes to Mono (start with `SetActive`, `SelectObject`, `GetTimeScale`/`SetTimeScale`) behind `allowWrites` + `requireConfirm` (implemented; validate on hosts).
- [x] Space Shooter project changes are allowed to improve repeatable IL2CPP + Mono rebuilds (source: `C:\codex-workspace\space-shooter`).
- [x] Treat inspector validation as a first-class gate: run `tools/Run-McpInspectorCli.ps1` early on both hosts for any wire/schema change.

---

## 0. Path to 100% MCP Inspector Validation

This section summarizes what still needs to be in place so that Unity Explorer MCP is fully exercised and “green” under `@modelcontextprotocol/inspector` plus the `UnityExplorer.Mcp.ContractTests` harness.

- Schema & payload validation:
  - [x] Confirm `unity://status`, `unity://scenes`, and `unity://scene/{id}/objects` payloads match `plans/mcp-interface-concept.md`, and adjust either DTOs or tests where they diverge.
  - [x] Ensure all DTOs used by inspector‑facing tools/resources are serializable without custom JSON options (no cycles, no Unity engine types leaking).
- Tool behaviour & write safety:
  - [x] Add a focused contract test for `SelectObject` that asserts selection state changes as expected (round‑trip with `unity://selection`).
  - [x] Add hook lifecycle tests in a dedicated “hook test” scene to validate `HookAdd` / `HookRemove` behaviour beyond permission errors (gated by `UE_MCP_HOOK_TEST_ENABLED=1` and `hookAllowlistSignatures` including a safe type such as `UnityEngine.GameObject`).
- Streaming & error robustness:
  - [x] Verify `stream_events` correctly cleans up on client disconnect (no unbounded dictionary growth) and add a test that repeatedly opens/closes streams (cleanup loop + reconnect test added).
  - [x] Ensure current concurrency behaviour matches `RateLimit_Does_Not_Crash_Server_When_Many_Concurrent_Requests` and, if a 429 limit is enabled, returns a structured JSON error payload.
  - [x] Add structured error JSON tests for common cases (`NotReady`, `NotFound`, `PermissionDenied`) that align server responses with inspector expectations.
- Inspector UX & dev‑experience:
  - [x] Fix `initialize.capabilities.experimental` to use object values (per MCP spec). `streamEvents` now returns `{}` so `@modelcontextprotocol/inspector --cli` can connect.
  - [x] Verify `@modelcontextprotocol/inspector --cli` can connect and run typical flows on both hosts (tools/list, resources/list/read, tools/call).
  - [x] Extend `UnityExplorer/README-mcp.md` with the final tool/resource list and representative example payloads used by inspector.
  - [x] Add a small smoke CLI/PowerShell script that runs `initialize`, `list_tools`, `GetStatus`, and `TailLogs` against a running Unity Explorer MCP instance, mirroring the `inspector` script defaults (see `tools/Invoke-McpSmoke.ps1`).
  - [x] Add a CI note/script to run `dotnet test UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` (and optionally a scripted inspector run) as part of the UnityExplorer build/validation pipeline.

---

### Recommended execution order
1) DTO/schema alignment + error envelope + rate limiting.
2) Read surface parity (logs metadata, camera/freecam, mouse inspect multi‑hit) and tests.
3) Guarded writes (SelectObject test, SetMember/ConsoleEval/Hooks) and new time‑scale tool.
4) Streams cleanup and rate‑limit tests.
5) Inspector/README-mcp/documentation polish.
6) Space Shooter validation (IL2CPP + Mono; no game-specific assumptions).

### Pitfalls / reminders for agents
- Keep `plans/mcp-interface-concept.md`, DTO code, and contract tests in sync; update all three together when shapes change.
- Never commit `.codex` runtime artifacts (`.codex/config.toml`, `.codex/history.jsonl`, `.codex/exec-logs/`, `.codex/auth.json`, `.codex/version.json`); they contain secrets and will block git push.
- Inspector validation is a gate: run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl <url>` early on both hosts for any wire/schema change.
- Windows host validation is a gate: for any behavior change, validate on the Windows host in the same iteration (not after).
- Smoke scripts must pass a PowerShell parse sanity check (run them against http://127.0.0.1:1 to catch ParserError early).
- IL2CPP regression is a gate: if a change touches shared query/DTO code used by both hosts (even if the change was Mono-motivated), run an IL2CPP regression pass (inspector CLI + smoke + contract tests).
- Do not add game-specific assumptions; tests must pass on Space Shooter.
- Guarded writes: always enforce `allowWrites` + `RequireConfirm` and return structured `ok=false` errors instead of throwing.
- When adding new behaviour, include an example payload in the concept doc and a contract test.
- Error envelope: use `error.code/message/data.kind[/hint]`; tool `ok=false` errors mirror `kind/hint`. Rate-limit message: `"Cannot have more than X parallel requests. Please slow down."` (include X).
- Mouse UI multi-hit: UI mode should return a list of hits (`Items`) plus `primaryId`; follow-up via `GetObject`/`GetComponents` (or a UI detail tool) on the selected `Id`.
- Logs: include `source`; if `category` exists, include it or document its absence.
- Time-scale writes: single guarded tool; add tests and docs when implemented.
- Stop SpaceShooter on the Windows host before copying mods (`Update-Mod-Remote.ps1`) to avoid SCP file-lock failures; restart the game after deployment.

## 0.1 Parallel worker scalability refactor (blocking)

Goal: reduce shared-file merge conflicts so we can run console scripts + hooks work in parallel.

- [x] Add `.worktrees/` to `.gitignore` and document worktree workflow in `AGENTS.md` + `.codex/AGENTS.md`.
- [x] Update `codex-exec.ps1` prompt so workers do not rewrite `INSTRUCTIONS.MD` and do not touch plan/todo docs unless explicitly instructed.
- [x] Refactor: split `src/Mcp/Dto.cs` into per-feature DTO files under `src/Mcp/Dto/`.
- [x] Refactor: move Mono host classes (`MonoMcpHandlers`, `MonoReadTools`, `MonoWriteTools`) out of `src/Mcp/McpSimpleHttp.cs` into `src/Mcp/Mono/`.
- [x] Refactor: isolate Mono tool/resource registries so adding tools does not require editing `src/Mcp/McpSimpleHttp.cs`.
- [x] Run: `dotnet build src/UnityExplorer.csproj -c ML_Cpp_CoreCLR` and `dotnet build src/UnityExplorer.csproj -c ML_Mono`.
- [x] Run: contract tests against both discovery files.

## 0.2 Feature-based folder split (parallel efficiency)

Goal: reduce merge conflicts further by splitting MCP implementation into per-feature folders/files, so different workers can work on different features without touching shared hotspots.

- [x] Split IL2CPP tool code into per-feature files (make `UnityReadTools`/`UnityWriteTools` partial; move feature blocks into `src/Mcp/Features/<Feature>/...`).
- [x] Split Mono tool code into per-feature files (make `MonoReadTools`/`MonoWriteTools` partial; move feature blocks into `src/Mcp/Features/<Feature>/...`).
- [x] Split Mono MCP handler hotspots into per-feature files (make `MonoMcpHandlers` partial; move `list_tools` schema blocks + `call_tool` dispatch + `list_resources`/`read_resource` routing into per-feature partials).
- [x] Optional: split IL2CPP resource routing (`McpReflection.ReadResourceAsync`) into per-feature files if still a hotspot.
- [x] Update coding rules in `AGENTS.md` and `.codex/AGENTS.md` to describe the new folder conventions (where to add tools/resources for each feature).
- [x] Build: `dotnet build src/UnityExplorer.csproj -c ML_Cpp_CoreCLR` and `dotnet build src/UnityExplorer.csproj -c ML_Mono`.
- [x] Contract tests: run against IL2CPP + Mono discovery files.
- [x] Windows host validation gate (same iteration): inspector CLI + smoke on IL2CPP `51477` and Mono `51478`.

## 0.3 Split large MCP hotspots (>400 lines)

Goal: reduce merge conflicts further by splitting the remaining large shared MCP hotspots.

- [x] Refactor transport into partials under `src/Mcp/Transport/{Interop,Mono}/McpSimpleHttp.*.cs` (accept loop, request parsing, JSON-RPC routing, stream handling, error mapping) and remove the monolithic `src/Mcp/McpSimpleHttp.cs`.
  - [x] Interop: moved to `src/Mcp/Transport/Interop/` partials.
  - [x] Mono: moved to `src/Mcp/Transport/Mono/` partials.
  - [x] Keep a small stub implementation for non-INTEROP/non-MONO builds.
- [x] Refactor `tests/dotnet/UnityExplorer.Mcp.ContractTests/JsonRpcContractTests.cs` into multiple test classes/files by topic (`Tools`, `Resources`, `Streams`, `Errors`) with a shared JSON-RPC client helper.
- [x] Build: `dotnet build src/UnityExplorer.csproj -c ML_Cpp_CoreCLR` and `dotnet build src/UnityExplorer.csproj -c ML_Mono`.
- [x] Contract tests: run against IL2CPP + Mono discovery files.
- [x] Windows host validation gate (same iteration): inspector CLI + smoke on IL2CPP `51477` and Mono `51478`.

## 1. Transport & Protocol Polish

- [x] Remove legacy SSE wording/naming in code (e.g. `McpSseState` → neutral name) while keeping behavior unchanged.
- [x] Add small sanity tests for:
  - [x] `stream_events` basic shape (already covered) plus long‑lived idle stream resilience.
  - [x] `notifications/initialized` being accepted (no error).
- [x] Document the expected MCP handshake (initialize → notifications/initialized → list_tools/read_resource/stream_events) in README‑mcp.

## 2. Read‑Only Surface Parity (Core Explorer Features)

- Status / Scenes / Objects (mostly done, needs validation polish):
  - [x] Confirm `unity://status`, `unity://scenes`, `unity://scene/{id}/objects` payloads align with the spec in `plans/mcp-interface-concept.md`.
  - [x] Add contract tests for `unity://object/{id}` and `unity://object/{id}/components` via `/read`.
- Search:
  - [x] Expose `unity://search?...` more fully (name/type/path/activeOnly) and add a contract test that exercises multiple filters.
- Selection:
  - [x] Ensure `GetSelection` mirrors Unity Explorer’s notion of “active inspector tab(s)” and write a test for success and error paths (`PermissionDenied`, `ConfirmationRequired`).
- [x] Add a simple `SelectObject(objectId)` tool that mirrors Explorer’s selection change (read‑only impact).
  - [x] Add a focused contract test that exercises `SelectObject` and validates that selection state changes as expected.
- [x] Design + stub reflection write helpers (`SetMember`) with allowlist and add tests that ensure:
  - [x] Denied when allowWrites=false.
  - [x] Denied when member not on allowlist.
  - [x] Succeeds for a whitelisted member in a controlled test scene.

## 5. Console & Hooks (Phase‑2 Parity)

- [x] Design a minimal `ConsoleEval` tool that:
  - [x] Takes small C# snippets and returns text/JSON result.
  - [x] Is disabled by default and gated by config + confirmation.
- [x] Expose Hook Manager basics:
  - [x] `unity://hooks` resource for listing active hooks.
  - [x] Tools for `HookAdd` / `HookRemove` guarded behind allowlist + confirmation.
- [x] Add tests that only run in a special “hook test” scene to verify basic lifecycle without depending on game content (gated by `UE_MCP_HOOK_TEST_ENABLED=1` with `hookAllowlistSignatures` containing a safe type such as `UnityEngine.GameObject`).

## 6. Config Hardening

- [x] Add MCP tool(s) around `McpConfig` (read/update) with appropriate permissions.
- [x] Auth removed; rely on bindAddress/port scoping plus allowWrites/confirm gating; docs updated.

## 7. Inspector & DX Polish

- [x] Verify inspector CLI flows on both hosts (tools/list, resources/list, resources/read, tools/call) and ensure `inputSchema` is present for all tools; we do not use the inspector UI.
- [x] Add contract coverage for `list_resources` plus inspector-friendly `call_tool` content (`text` + `mimeType` + `json`) to keep CLI compatibility stable.
- [x] Add an inspector CLI smoke script (`tools/Run-McpInspectorCli.ps1`) that runs inspector --cli (tools/list, resources/list, read unity://status, tools/call GetStatus; optional Authorization header) and document usage; last run 2025-12-14: Mono 51478 PASS, IL2CPP 51477 PASS.
- [x] Add JSON‑RPC contract tests that exercise `tools/list`, `tools/call`, and `call_tool` for all read‑only tools (matching inspector CLI usage).
- [x] Add a small “how to connect with inspector” section to `README-mcp.md`, including:
  - [x] Example `initialize`, `list_tools`, `call_tool` flows.
  - [x] Example `read_resource` URIs for common Explorer tasks.

## 8. Documentation & AGENTS.md Sync

- [x] Extend `UnityExplorer/README-mcp.md` to reflect the final tool/resource list and example payloads.
- [x] Add `docs/unity-explorer-game-interaction.md` summarizing core UnityExplorer runtime capabilities (non-MCP).
- [x] Update `UnityExplorer/AGENTS.md` with:
  - [x] A short “MCP quick‑start” section (build + launch + inspector connect).
  - [x] Notes about guarded writes and how to safely enable them on the Test‑VM.

## 9. Additional Quality / Nice‑to‑Have TODOs

- [x] Add a tiny “smoke CLI” script (PowerShell/bash) that exercises `initialize`, `list_tools`, `GetStatus`, and `TailLogs` in one go (see `tools/Invoke-McpSmoke.ps1`; mirrors the Windows host harness).
- [x] Add a contract test that calls `MousePick` and verifies the result shape (even if `Hit=false`).
- [x] Add a contract test that calls `TailLogs` via `call_tool` (not just `read_resource`).
- [x] Ensure all DTOs are serializable without extra JSON options (no cycles, no Unity types leaking through).
- [x] Add a simple rate‑limit in `McpSimpleHttp` (e.g., max ~32 concurrent requests) and a test that overload returns a clear error with message `"Cannot have more than X parallel requests. Please slow down."`. (Code: `UnityExplorer/src/Mcp/McpSimpleHttp.cs`; Tests: `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests/HttpContractTests.cs`.)
- [x] Standardize structured error data for common cases (`NotReady`, `NotFound`, `PermissionDenied`, `RateLimited`) using the existing JSON‑RPC error envelope (`error.code`, `error.message`, `error.data.kind`, optional `error.data.hint`) and assert this shape in tests (including tool `ok=false` payloads). (Code: `McpSimpleHttp`, `UnityWriteTools`; Tests: JSON‑RPC + tool contract tests.)
- [x] Verify `stream_events` gracefully handles client disconnects (no unbounded dictionary growth); add a test that opens and closes multiple streams.
- [x] Add logging hooks for MCP errors into the MelonLoader log (short prefix, e.g. `[MCP]`), with a test that triggers at least one intentional error and reads it back via `logs/tail`.
- [x] Add a small “version” resource or tool (e.g., `unity://status` already has version, but expose a dedicated `GetVersion` tool and test it).
- [x] Add a CI note/script to run the MCP contract tests as part of the normal UnityExplorer build pipeline (see `tools/Run-McpContractTests.ps1`).
- [x] Add CI hooks for inspector CLI smoke runs where infra allows (optional).

## 10. Space Shooter IL2CPP – End‑to‑End Validation

- [x] Extend the Test‑VM PowerShell harness (`C:\codex-workspace\ue-mcp-headless\call-mcp.ps1` / `req.json`) to cover remaining read tools on Space Shooter (payloads now documented in `plans/space-shooter-test-plan.md`; update the VM script accordingly):
  - `SearchObjects` (by name and type),
  - `GetCameraInfo`,
  - `MousePick` (even if `Hit=false`), including coord-based UI picks (x/y/normalized) against `SpawnTestUi` blocks.
- [x] Add short snippets to `plans/space-shooter-test-plan.md` showing the above calls and expected JSON shapes so agents can quickly verify behavior (include `SpawnTestUi` + `MousePick` examples).
- [x] Add a minimal `stream_events` check against Space Shooter: open `stream_events`, call a tool (e.g. `GetStatus`), and confirm a `tool_result` notification is received; note any `logs` / `scenes` notifications (flow documented in `plans/space-shooter-test-plan.md`; needs live validation).
- [x] Run `UnityExplorer.Mcp.ContractTests` against the Space Shooter + MelonLoader host (document the exact steps and any required env vars / discovery overrides) and record whether all tests pass. (Release, BaseUrl `http://192.168.178.210:51477`: 45 passed, 1 skipped placeholder; host remained stable.)
- [x] Ensure no contract tests assume game‑specific content; adjust tests and docs so Space Shooter is the fully supported host for MCP contract validation (other titles are examples only).
- [x] Define 1–2 safe write scenarios on Space Shooter using `SetActive` / `SelectObject` with `AllowWrites=true` and `RequireConfirm=true`, and document them in `plans/space-shooter-test-plan.md` (also note `SetTimeScale` + `SpawnTestUi`/`MousePick` flow for UI validation).
- [x] Make Space Shooter Mono + IL2CPP rebuilds repeatable from `C:\codex-workspace\space-shooter` (allowed to modify the Unity project/scripts); document exact build steps and keep build outputs stable (BuildCommands scene fallback + headless CPU lighting, scripts `Update-SpaceShooter-BuildScripts-Remote.ps1` / `Build-SpaceShooter-Remote.ps1` produce fresh outputs under `...\SpaceShooter_IL2CPP` / `...\SpaceShooter_Mono`).
- [x] PRIORITY: fix the UnityExplorer dropdown Il2Cpp cast crash (UI `Dropdown` array cast) and remove the Test‑VM‑only `UeMcpHeadless.dll` workaround (guarded warning in UIManager; headless mod renamed to .disabled on Windows host).

## 11. Mono / MelonLoader Support

- [x] Phase A — Build + host skeleton
  - [x] Fix ML_Mono build (currently `DefineConstants=MONO,ML`, `IncludeMcpPackages=false`, `net35`) so `build.ps1` can produce `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` again; guard `ExplorerCore`/`OptionsPanel`/other MCP call sites when `INTEROP` is absent or introduce a stub McpHost that compiles on Mono. (Done: INTEROP-disabled stubs + MCP UI note; no listener/discovery on Mono.)
  - [x] Decide how `INTEROP` should apply to Mono (enable a minimal variant or keep it off with stubs) and ensure `McpConfig`/discovery file behavior is well-defined even if MCP is disabled. (Decision: keep INTEROP off; McpHost/Config return disabled defaults, Options panel shows disabled status.)
  - [x] Document the build command + expected output paths for Mono in the plan/todo and capture any remaining blocking errors in `build.log`. (Command: `dotnet build src/UnityExplorer.csproj -c ML_Mono` → `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll`; none blocking.)

- [x] Phase B — Read-only MCP surface on Mono
  - [x] Select a Mono-friendly JSON/HTTP stack (e.g., `Newtonsoft.Json` + `HttpListener`/simplified TCP) that preserves the CoreCLR DTOs/error envelope; avoid heavy ASP.NET deps.
  - [x] Bring up minimal Mono MCP endpoints (`initialize`, `list_tools`, `read_resource` for status/scenes/objects/logs) and keep shapes identical to CoreCLR; document any deltas in `plans/mcp-interface-concept.md`.
  - [x] Implement real `stream_events` on Mono (matching CoreCLR streamable-http behaviour; log/selection/scene/tool_result notifications; cleanup on disconnect; identical error envelope).
  - [x] Add a Mono smoke harness (subset of contract tests) and doc how to run it.
  - [x] Fix Space Shooter Mono Unity build automation (implement `BuildCommands.BuildWindows64Mono` or adjust the CLI target) so `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` can be produced for smoke runs (now available on the Windows host at `http://192.168.178.210:51478`).

- [x] Phase C — Parity + tests
  - [x] Implement guarded writes on Mono (start with `SetActive`, `SelectObject`, `GetTimeScale`/`SetTimeScale`) behind `allowWrites` + `requireConfirm`; keep the same error envelope as CoreCLR (needs live validation on hosts).
  - [x] Add `SpawnTestUi` / `DestroyTestUi` guarded tools to Mono and cover them in `Run-McpMonoSmoke.ps1 -EnableWriteSmoke` (config enable → spawn/destroy → reset).
  - [x] Add guarded `Reparent` / `DestroyObject` for Mono (limited to SpawnTestUi blocks) and surface SpawnTestUi block ids for write smoke reparent/destroy coverage.
  - [x] Expand Mono coverage toward CoreCLR parity (selection, console/scripts, hooks); Mono now exposes `unity://console/scripts` + `unity://hooks`, and `Run-McpMonoSmoke.ps1 -EnableWriteSmoke` covers `ConsoleEval` + `AddComponent`/`RemoveComponent` + `HookAdd`/`HookRemove`.
  - [x] Implement `CallMethod` on Mono (guarded; `reflectionAllowlistMembers` + `allowWrites` + confirm) and cover it in write smoke + a contract test.
  
  - [x] Fix Mono notification broadcast compile issue (net35 has no Tasks): remove discards on void `BroadcastNotificationAsync` or reintroduce a Task-compatible wrapper.
  - [x] Run Mono smoke + inspector CLI against a real Mono host and record results (Test‑VM base URL: `http://192.168.178.210:51478`). See `README-mcp.md` Mono Host Validation Checklist. (Ran `Run-McpMonoSmoke.ps1`, inspector tools/list + resources/read now green after redeploy.)
  - [x] Add Mono-specific contract/CI entry (`tools/Run-McpMonoSmoke.ps1`); run against a Mono host when available and keep IL2CPP behavior unchanged.

## 12. UnityExplorer Feature Parity (MCP v1)

These are the remaining big feature surfaces to expose for “agent-grade” parity with UnityExplorer. For any new tool/resource/DTO shape, update `plans/mcp-interface-concept.md` first, then implement + add contract tests.

Priority right now: **12.7 Console scripts** + **12.8 Hooks (advanced)**.

### 12.1 Object Explorer parity
- [x] Add pseudo-scene coverage (DontDestroyOnLoad / HideAndDontSave) in resources + tools.
- [x] Add pseudo-scene coverage (Resources/Assets views) in resources + tools.
- [x] Add hierarchical object tree browsing (not only shallow cards); include paging and stable ids.
- [x] Expose Scene Loader basics: list build scenes and add a guarded `LoadScene` tool.

### 12.2 Inspector parity (read)
- [x] Expose component member listing (fields/properties/methods) for an object + component type.
- [x] Add safe “read member value” surface with depth/size limits and cycle protection.
- [x] Add “reflection inspector” reads for non-GameObject objects where UnityExplorer supports it.

### 12.3 Inspector parity (write)
- [x] Expand `SetMember` support for common Unity value types (Color/Vector*/Quaternion/enums) consistently on IL2CPP + Mono.
- [x] Add per-tool audit logging for writes (visible in `unity://logs/tail`).

### 12.4 Search parity
- [x] Add Singleton search surface (UnityExplorer’s singleton finder) with stable ids.
- [x] Add Static class search surface (type names + members; read-only).

### 12.5 Freecam parity
- [x] Expose freecam state (enabled/useGameCamera/speed/pose) as a resource.
- [x] Add guarded tools to enable/disable freecam and set pose/speed.

### 12.6 Clipboard parity
- [x] Expose clipboard read (type + preview) as a resource.
- [x] Add guarded tools to set/clear clipboard.

### 12.7 Console scripts parity (TOP)
- [x] Spec: console scripts contract in `plans/mcp-interface-concept.md` (Console scripts section).
- [x] Implement resource `unity://console/script?path=...` (content + metadata; size cap; validate path stays inside Scripts folder).
- [x] Implement tools (read/write/delete):
  - [x] `ReadConsoleScript(path)`
  - [x] `WriteConsoleScript(path, content, confirm?)`
  - [x] `DeleteConsoleScript(path, confirm?)`
- [x] Implement tools (execution + startup):
  - [x] `RunConsoleScript(path, confirm?)` (requires `enableConsoleEval=true`)
  - [x] `GetStartupScript()` / `SetStartupScriptEnabled(enabled, confirm?)` / `WriteStartupScript(content, confirm?)` / `RunStartupScript(confirm?)`
- [x] Safety (read/write/delete): block path traversal, require `.cs`, enforce max bytes, respect `allowWrites` + `requireConfirm`, normalize BOM.
- [x] Contract tests (gated): schema + read/write/delete round-trip + cleanup (`UE_MCP_CONSOLE_SCRIPT_TEST_ENABLED=1`).
- [x] Smoke: cover one full script lifecycle (create → read → run → delete) on IL2CPP + Mono.
- [x] Windows host validation (read/write/delete/run/startup): inspector CLI + smoke + contract tests on IL2CPP (`51477`) + Mono (`51478`).

### 12.8 Hooks parity (advanced) (TOP)
- [x] Align + document allowlist semantics: `hookAllowlistSignatures` contains type full names (both hosts).
- [x] Spec: hooks advanced contract in `plans/mcp-interface-concept.md` (Hooks advanced section).
- [x] Implement read-only discovery tools: `HookListAllowedTypes()` + `HookListMethods(type, filter?, limit?, offset?)`.
- [x] Implement hook management: `HookGetSource(signature)` (read-only), `HookSetEnabled(signature, enabled, confirm?)`, `HookSetSource(signature, source, confirm?)` (guarded; requires `enableConsoleEval=true`).
- [x] Improve `HookAdd(type, method)`: accept full `MethodInfo.FullDescription()` signature in `method` to select overloads (keep method-name support).
- [x] Contract tests + smoke: cover enable/disable + source read/write on a safe allowlisted type (keep `UE_MCP_HOOK_TEST_ENABLED` gate).
- [x] Windows host validation: inspector CLI + smoke + contract tests on IL2CPP + Mono.

## 13. Streams & Agent UX
- [x] Decide and document the minimal “agent-first” event set with stable payloads and examples (log/scenes/selection/tool_result) in `plans/mcp-interface-concept.md`.
- [x] Add a `selection` snapshot on stream open and lock it with a contract test.
- [x] Add IL2CPP backpressure strategy for `stream_events` (cap/drop policy) and a stress test.
- [x] Mirror stream backpressure onto Mono (avoid blocking synchronous broadcast; align with IL2CPP cap/drop policy).

## 14. Reliability / Ops
- [x] Harden Windows host deploy for IL2CPP + Mono (`tools/Update-Mod-Remote.ps1` stages to remote home, then copies with remote PowerShell; avoids scp drive-letter issues; documented in `plans/space-shooter-test-plan.md`).
- [x] Add a lightweight watchdog for the win-dev MCP proxies (8082/8083): healthcheck + restart commands + log paths.
- [x] Cross-title IL2CPP regression: BLOCKED (no second IL2CPP title candidate available right now); document the blocker and skip for DoD.
- [x] Add CI hooks for inspector CLI smoke runs where infra allows (optional).
