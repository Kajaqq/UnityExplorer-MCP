# Agent Instructions (UnityExplorer root)

## Fork scope
- Goal: improve compatibility for a specific mobile Unity 6 game running on Windows.
- Unity version: `6000.0.77f`.
- Loader/runtime: BepInEx 6 CoreCLR IL2CPP through the custom fork in `../BepInEx/`.
- Build target: `Release_BIE_Unity_Cpp` from `src/UnityExplorer.sln`.
- MCP host target: IL2CPP on port `51477`.
- Mono, net35, MelonLoader, and cross-loader feature parity are out of scope. It is acceptable for those code paths to lag or break while improving this BepInEx 6 IL2CPP target.

## Documentation rules
- Keep `plans/unity-explorer-mcp-plan.md` aligned with the current IL2CPP-only architecture and validation flow.
- Keep `plans/unity-explorer-mcp-todo.md` aligned with active IL2CPP-only tasks.
- Keep `docs/mcp-interface-specs.md` as the source of truth for MCP DTO shapes, errors, examples, and agent UX expectations.
- Update `README-mcp.md` when user-facing MCP commands or surfaces change.

## Parallel worker rules
- Use `git worktree` under `./.worktrees/<name>` so each worker has its own branch and working tree.
- Ensure Codex auth exists in the worktree (`.codex/auth.json`); if missing, symlink it to `$HOME/.codex/auth.json`.
- In parallel runs, workers must not edit:
  - `plans/unity-explorer-mcp-plan.md`
  - `plans/unity-explorer-mcp-todo.md`
  - `docs/mcp-interface-specs.md` unless DTO/tool shapes change.
- Avoid shared-file hotspots:
  - Put DTOs in `src/Mcp/Dto/` (one feature per file).
  - Put IL2CPP MCP tool implementations in `src/Mcp/Features/<FeatureName>/Interop/`.
  - Keep `src/Mcp/UnityReadTools.cs` and `src/Mcp/UnityWriteTools.cs` small and stable.
  - Keep transport under `src/Mcp/Transport/Interop/McpSimpleHttp.*.cs`.

## Quality gates
- Build passes for the IL2CPP target.
- Static code analysis finds no critical issues.

### End-to-end validation
If a user specifies that a running host is available, run smoke and contract validation:
- Basic MCP e2e path is demonstrable: start -> handshake -> tool call -> response.
- Inspector validation is CLI-only. Prefer:
  - `npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/list`
- Run smoke and contract validation when a running host is available:
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 20`
  - `pwsh ./tools/Run-McpContractTests.ps1`

## Constraints
- Use existing implementation boundaries; do not refactor beyond scope.
- No API invention; reference actual files, paths, and symbols.
- Prefer PowerShell for user-facing validation commands.
- Keep write tools guarded: `allowWrites=false` by default, `requireConfirm=true` for mutating operations.
- To experiment with write tools, use the `SetConfig` tool to toggle `allowWrites` / `requireConfirm` (and `enableConsoleEval` / allowlists) and always reset them to safe values when you are done.

## Docs map
- `plans/unity-explorer-mcp-plan.md`: IL2CPP-focused architecture, status, and validation.
- `plans/unity-explorer-mcp-todo.md`: active IL2CPP-only checklist.
- `docs/mcp-interface-specs.md`: MCP interface contract.
- `docs/linux-build.md`: reproducing the full release packaging with `build.sh` and ILRepack.
- `README-mcp.md`: user-facing MCP usage and quickstart.
- `docs/unity-explorer-game-interaction.md`: native UnityExplorer runtime capabilities.
