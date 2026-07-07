## Agent Instructions (UnityExplorer root)

This file is for agents working on this repository, especially when building on Linux but targeting Windows Unity titles.

### Build focus

- Build only CoreCLR IL2CPP (no Mono).
- This is a single-game focused fork, based on the original UnityExplorer project. 
- The game is running Unity 6000.0.77f and uses CoreCLR IL2CPP via a custom fork of BepInEx 6 with source located in `../BepInEx/`.
- CoreCLR IL2CPP build targets:
  - `Release_BIE_Unity_Cpp_CoreCLR`
  - Optionally: `Release_STANDALONE_Cpp_CoreCLR`
- You do **not** need to build other Mono / net35 / MelonLoader targets unless a task explicitly requires them.

### Parallel workers (scalable workflow)

When running multiple Codex workers in parallel, use these rules to reduce merge conflicts:

- Use `git worktree` under `./.worktrees/<name>` (keep it gitignored).
- Each worker uses its own branch and commits there.
- Ensure each worktree has Codex auth available:
  - If `.codex/auth.json` is missing, symlink it to `$HOME/.codex/auth.json`.
- Workers should NOT edit these files in parallel runs:
  - `INSTRUCTIONS.MD`
  - `plans/unity-explorer-mcp-plan.md`
  - `plans/unity-explorer-mcp-todo.md`
  - (Only update `plans/mcp-interface-concept.md` when DTO/tool shapes change.)
- Avoid shared-file hotspots:
  - Add new DTOs under `src/Mcp/Dto/` (one feature per file).
  - Put MCP tool implementations in `src/Mcp/Features/<FeatureName>/Interop/` and `src/Mcp/Features/<FeatureName>/Mono/`.
  - Keep the aggregator/entry files small and stable: `src/Mcp/UnityReadTools.cs`, `src/Mcp/UnityWriteTools.cs`, `src/Mcp/Mono/MonoReadTools.cs`, `src/Mcp/Mono/MonoWriteTools.cs`, `src/Mcp/Mono/MonoMcpHandlers.cs`.
  - Keep Mono host logic under `src/Mcp/Mono/` (avoid feature edits in `src/Mcp/McpSimpleHttp.cs`).
  - Transport stays split under `src/Mcp/Transport/{Interop,Mono}/McpSimpleHttp.*.cs`; keep `McpSimpleHttp` partials in those folders and do not collapse them back into a monolith.

### Required tools on Linux

On a Linux development machine, assume the following tools should be present or installed by the human operator:

- `.NET SDK` 6.0 or newer (for `dotnet build`).
- `git` (for source control).
- For full release packaging and ILRepack usage:
  - `PowerShell` (pwsh) to run `build.ps1`.
  - `Mono` to run `lib/ILRepack.exe` as `mono lib/ILRepack.exe ...`.
- Common CLI tools: `zip`/`unzip` or equivalent (many distros ship these by default).

Agents must **not** assume they can install system packages themselves; instead, document the required commands and let the user run them. See the Linux build docs linked below.

### Linux build documentation

There are two Linux-oriented docs under the repo root:

- `docs/linux-build-basic.md` – building raw DLLs only (no packaging).
- `docs/linux-build-packaging.md` – reproducing the full release packaging with `build.ps1` and ILRepack.

When modifying build steps, update the relevant doc(s) and keep these instructions in sync.

### MCP Quick‑Start (for agents)

- **Build + deploy MCP build:**
  - From repo root: `cd UnityExplorer`
  - `'./build.sh'`
- **Inspector CLI (no UI):**
  - Prefer the repo helper:
    - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://<TestVM-IP>:51477`
  - Or run direct one-liners:
    - `npx @modelcontextprotocol/inspector --cli http://<TestVM-IP>:51477/mcp --method tools/list`
    - `npx @modelcontextprotocol/inspector --cli http://<TestVM-IP>:51477/mcp --method tools/call --tool-name GetStatus`
- **Guarded writes and config:**
  - By default `allowWrites` is `false`; do not enable this casually on shared machines.
  - To experiment with write tools, use the `SetConfig` tool to toggle `allowWrites` / `requireConfirm` (and `enableConsoleEval` / allowlists) and always reset them to safe values when you are done.
