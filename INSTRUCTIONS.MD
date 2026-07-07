# Next iteration instructions (Iteration 22)

Focus (do in this order)
1) Console scripts parity (TOP): implement file read/write/delete over MCP on IL2CPP
2) Add contract tests for the new console-script surface (round-trip + cleanup).

Rules / gates
- Inspector validation is CLI-only (no UI).
- IL2CPP regression is required when shared query/DTO code changes: run inspector CLI + smoke + contract tests.
- Keep writes off by default; any write-smoke must reset config at the end.

Targets
- IL2CPP host: `http://127.0.0.1:51477/mcp`

Tasks

1) Console scripts (read)
- Implement `ReadConsoleScript(path)` tool.
- Implement resource `unity://console/script?path=...`.
- Contract must match `plans/mcp-interface-concept.md` (planned section).
- Path handling:
  - Accept `path` as a relative or absolute path.
  - Validate it stays inside `ConsoleController.ScriptsFolder`.
  - Require `.cs` extension.
  - Enforce a fixed max read size and set `Truncated=true` when needed.

2) Console scripts (write)
- Implement `WriteConsoleScript(path, content, confirm=false)` and `DeleteConsoleScript(path, confirm=false)`.
- Gating:
  - require `allowWrites=true`.
  - if `requireConfirm=true`, require `confirm=true`.
- Safety:
  - Same path validation rules as read.
  - Enforce max write size.

Validation (must do)
- IL2CPP regression:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477/mcp`
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 10`

Docs
- Keep in sync if anything changes:
  - `plans/mcp-interface-concept.md` (move items from Planned → Implemented when done)
  - `plans/unity-explorer-mcp-plan.md`
  - `plans/unity-explorer-mcp-todo.md`
  - `README-mcp.md` (summary only; no duplication)

Commit
- Commits are allowed; commit your changes with a clear message.
