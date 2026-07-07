# Unity Explorer MCP (In‑Process) — Preview

This build hosts a Model Context Protocol (MCP) server inside the Unity Explorer DLL for CoreCLR (IL2CPP Interop) targets. The server exposes read‑only tools and guarded writes over HTTP using the official C# SDK. DTO shapes and error envelopes live in `plans/mcp-interface-concept.md`; this README summarizes how to call the surface.

## Status

- Targets: CoreCLR builds (`BIE_*_Cpp_CoreCLR`, `ML_Cpp_CoreCLR`, `STANDALONE_Cpp_CoreCLR`).
- Console scripts: `unity://console/scripts`, `unity://console/script?path=...`, `ReadConsoleScript` (RO), guarded `WriteConsoleScript`/`DeleteConsoleScript`/`RunConsoleScript`, and startup controls (`GetStartupScript`/`WriteStartupScript`/`SetStartupScriptEnabled`/`RunStartupScript`) on both hosts.
- Hooks advanced: `HookListAllowedTypes`, `HookListMethods`, `HookGetSource`, guarded `HookSetEnabled`/`HookSetSource` (both hosts).
- Transport: lightweight streamable HTTP over a local TCP listener.
- Default mode: Read‑only (guarded writes must be explicitly enabled).
- Release checklist + gates live in `plans/unity-explorer-mcp-plan.md#release-checklist` (source of truth).

## Getting Started

1. Launch a Unity title with Unity Explorer (CoreCLR target).
2. The MCP server starts after Explorer initialization.
3. Discovery file is written to `%TEMP%/unity-explorer-mcp.json` with `{ pid, baseUrl, port, modeHints, startedAt }`.
4. Connect a client via the MCP C# SDK using `HttpClientTransport` (AutoDetect mode), or talk directly to the HTTP endpoints described below.

## Inspector CLI smoke

Run this non-interactive CLI smoke to validate the MCP surface via `@modelcontextprotocol/inspector --cli` (runs tools/list, resources/list, read status, and call GetStatus). Fails fast if `npx` is missing or the host is down.

```powershell
pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477/mcp
```

The helper accepts BaseUrl values with or without `/mcp` and normalizes to the JSON-RPC path. Optional CI: `.github/workflows/mcp-inspector-cli.yml` is a manual (`workflow_dispatch`) workflow that runs this script against provided `baseUrlIl2cpp` inputs (no-op when inputs are empty; host must be reachable from the runner).

## All gates

Run the full IL2CPP test stack (inspector CLI + smoke + contract tests). Fails fast on any failure.

```powershell
pwsh ./tools/Run-McpAllGates.ps1 -BaseUrlIl2cpp http://192.168.178.210:51477 [-EnableWriteSmoke] [-AuthToken <token>]
```

## Inspector CLI (direct one-liners)

We do not use the Inspector UI for validation; use the CLI.

```bash
npx @modelcontextprotocol/inspector --cli http://192.168.178.210:51477/mcp --method tools/list
npx @modelcontextprotocol/inspector --cli http://192.168.178.210:51477/mcp --method tools/call --tool-name GetStatus
```

## Configuration

The config file is created at `{ExplorerFolder}/mcp.config.json` (Explorer folder is typically `sinai-dev-UnityExplorer/`). Example:

```json
{
  "enabled": true,
  "bindAddress": "0.0.0.0",
  "port": 51477,
  "transportPreference": "auto",
  "allowWrites": false,
  "requireConfirm": true,
  "enableConsoleEval": false,
  "componentAllowlist": null,
  "reflectionAllowlistMembers": null,
  "hookAllowlistSignatures": null,
  "exportRoot": null,
  "logLevel": "Information"
}
```

- `port: 0` uses an ephemeral port.

## HTTP Surface

The in‑process server exposes a minimal HTTP protocol over a loopback TCP listener:

- `POST /message` (alias `POST /mcp`, JSON body)  
  - JSON‑RPC‑style envelope:
    - `{"jsonrpc":"2.0","id":1,"method":"list_tools","params":{}}`
    - `{"jsonrpc":"2.0","id":2,"method":"call_tool","params":{"name":"ListScenes","arguments":{...}}}`
    - `{"jsonrpc":"2.0","id":3,"method":"read_resource","params":{"uri":"unity://status"}}`
    - `{"jsonrpc":"2.0","id":4,"method":"stream_events","params":{}}`
  - Non‑streaming methods (`list_tools`, `call_tool`, `read_resource`) return a JSON‑RPC response in the HTTP body:
    - Success: `{"jsonrpc":"2.0","id":1,"result":{...}}`
    - Error: `{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Invalid params",...}}`
  - `stream_events` keeps the HTTP response open and streams JSON‑RPC 2.0 payloads over a chunked HTTP body (one JSON object per line). The stream may contain:
    - Notifications: `{"jsonrpc":"2.0","method":"notification","params":{"event":"log|selection|scenes|scenes_diff|inspected_scene|tool_result|...","payload":{...}}}`
    - Standard results/errors echoed to subscribers for other requests: `{"jsonrpc":"2.0","id":...,"result":{...}}` and `{"jsonrpc":"2.0","id":...,"error":{"code":...,"message":"...", "data":{...}}}`
  - Tool calls are also echoed as streamed `tool_result` notifications on `stream_events`.
- `GET /read?uri=unity://...`  
  - Convenience endpoint for simple read operations.  
  - Returns JSON directly in the HTTP response.
- `GET /` (or `/?…`) with `Accept: text/event-stream`  
  - Server-Sent Events (SSE) receive channel; emits the same JSON-RPC results/errors/notifications as `stream_events` using `data: <json>\n\n` frames until the client disconnects (no chunked encoding). `/mcp` (or `/mcp?...`) accepts the same stream for pathful bases.

### MCP Handshake

When connecting with a generic MCP client (including `@modelcontextprotocol/inspector`), the typical sequence is:

1. `initialize`  
   - Returns `protocolVersion`, `capabilities` (`tools`, `resources`, `experimental.streamEvents`), `serverInfo`, and a short `instructions` string.
2. `notifications/initialized`  
   - Optional client notification; when no `id` is provided the server returns HTTP 202 with an empty body (JSON-RPC notification semantics). If an `id` is present, the server replies with `{ "ok": true }`.
3. `list_tools`, `read_resource`, `call_tool`, and `stream_events`  
   - Use `list_tools` to discover tools, `call_tool` for RPCs, `read_resource` for `unity://...` URIs, and `stream_events` for log/scene/selection/tool_result notifications.

## Resources

Resources are addressed using `unity://` URIs (paged where noted):

- `unity://status` — global status (Unity version, scenes, selection summary, etc.).
- `unity://scenes` — list of loaded scenes.
- `unity://scene/{sceneId}/objects` — objects in a scene (paged).
- `unity://object/{id}` — a single object plus key fields.
- `unity://object/{id}/components` — components on an object (paged).
- `unity://search?...` — search across objects (by name, type, path, activeOnly, etc.).
- `unity://camera/active` — active camera; reports `IsFreecam=true` when the UE Freecam is active (falls back to `Camera.main`/first camera, or `<none>` if absent).
- `unity://selection` — current Unity selection.
- `unity://logs/tail?count=200` — recent log lines (`{ T, Level, Message, Source, Category? }`).
- `unity://console/scripts` — C# console scripts in the Explorer `Scripts` folder (when present).
- `unity://hooks` — currently active method hooks.

### Example payloads (abbreviated)

`unity://logs/tail`:

```json
{
  "Items": [
    { "T": "2025-12-09T12:34:56.789Z", "Level": "info", "Message": "server started", "Source": "mcp" },
    { "T": "2025-12-09T12:35:10.123Z", "Level": "warn", "Message": "camera missing", "Source": "unity", "Category": "Rendering" }
  ]
}
```

`MousePick(mode="ui")` (multi-hit):

```json
{
  "Mode": "ui",
  "Hit": true,
  "Id": "obj:12345",
  "Items": [
    { "Id": "obj:12345", "Name": "McpTestBlock_Left", "Path": "/McpTestCanvas/McpTestBlock_Left" },
    { "Id": "obj:67890", "Name": "McpTestBlock_Right", "Path": "/McpTestCanvas/McpTestBlock_Right" }
  ]
}
```

## Tools

Read‑only tools (no `allowWrites` required):

- `GetStatus`
- `ListScenes`
- `ListObjects`
- `GetObject`
- `GetComponents`
- `SearchObjects`
- `MousePick` (`world` = top-most hit, returns `Items=null` when no world hit; `ui` = ordered `Items` + primary `Id`; optional `x`,`y`, `normalized=true` override the live mouse)
- `GetCameraInfo`
- `GetSelection`
- `TailLogs`
- `GetVersion`

Guarded / write-related tools (most require `allowWrites: true`; many also need `confirm=true` and allowlists):

- Config: `SetConfig`, `GetConfig` (sanitized view).
- Object state: `SetActive`, `SelectObject`, `Reparent`, `DestroyObject`.
- Reflection/component writes: `SetMember`, `AddComponent`, `RemoveComponent`, `CallMethod` (respect allowlists on both hosts).
- Hooks: `HookAdd`, `HookRemove` (hook allowlist enforced).
- Console: `ConsoleEval` (requires `enableConsoleEval` + writes + confirm); console scripts (`WriteConsoleScript`/`DeleteConsoleScript`/`RunConsoleScript`) + startup controls (`GetStartupScript`/`WriteStartupScript`/`SetStartupScriptEnabled`/`RunStartupScript`) require writes; execution also requires `enableConsoleEval` and confirm when configured.
- Time scale: `GetTimeScale` (read), `SetTimeScale` (guarded; optional lock/unlock).
- UI test helpers: `SpawnTestUi` / `DestroyTestUi` (for `MousePick` UI validation).

## Stream Events

All server‑side notifications are delivered over `stream_events` as JSON‑RPC 2.0 notifications:

```jsonc
{ "jsonrpc": "2.0", "method": "notification", "params": { "event": "<name>", "payload": { ... } } }
```

Event types (payloads and examples live in `plans/mcp-interface-concept.md`):

- `log` — log lines (mirrors `unity://logs/tail`).
- `selection` — selection snapshot (mirrors `unity://selection`).
- `scenes` — scenes snapshot (mirrors `unity://scenes`; emitted on stream open).
- `scenes_diff` — scenes added/removed.
- `inspected_scene` — inspected scene marker (if emitted).
- `tool_result` — tool call result/error mirror for `call_tool`.

## Allowlists

Two allowlists are configured in `mcp.config.json` and editable via the Options panel:

- Component allowlist: `componentAllowlist`  
  - Array of `FullTypeName` strings.  
  - `AddComponent` and `RemoveComponent` are restricted to these types (or indices).
- Reflection allowlist: `reflectionAllowlistMembers`  
  - Array of `"Type.Member"` strings, e.g., `"UnityEngine.Light.intensity"`.  
  - `SetMember` requires entries here; `CallMethod` (both hosts) uses the same allowlist.

Empty allowlists disable the corresponding write features (for safety).

## C# stream_events example

For a minimal C# client that connects to the in‑process MCP server and prints streamed events over HTTP:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        // Load discovery file
        var path = Environment.GetEnvironmentVariable("UE_MCP_DISCOVERY");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Discovery file not found; is Unity Explorer MCP running?");
            return;
        }

        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        var root = doc.RootElement;
        var baseUrlStr = root.GetProperty("baseUrl").GetString();
        var baseUri = new Uri(baseUrlStr!, UriKind.Absolute);

        using var http = new HttpClient { BaseAddress = baseUri };

        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var rpcPath = "/mcp"; // alias of /message
        using var req = new HttpRequestMessage(HttpMethod.Post, rpcPath) { Content = content };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            Console.WriteLine(line);
        }
    }
}
```

This matches the `stream_events` behavior and will print JSON‑RPC `notification`, `result`, and `error` objects as one JSON object per line.

## Inspector CLI Quick‑Start (`@modelcontextprotocol/inspector --cli`)

1. Start a CoreCLR Unity title with Unity Explorer and ensure MCP is enabled (`mcp.config.json: { "enabled": true }`).
2. From your dev machine, run:

   ```bash
   npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/list
   npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method resources/read --uri unity://status
   npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/call --tool-name GetStatus
   ```

3. Or use the repo helper:

   ```powershell
   pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://127.0.0.1:51477
   ```

## Smoke CLI (PowerShell)

- Run `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://<host>:51477 -LogCount 20`.
- If `-BaseUrl` is omitted, the script reads `$env:UE_MCP_DISCOVERY` or `%TEMP%/unity-explorer-mcp.json`.
- Sequence: `initialize` → `notifications/initialized` → `list_tools` → `call_tool` (`GetStatus`, `TailLogs`) → `read_resource` (`unity://status`, `unity://scenes`, `unity://logs/tail?count=...`).
- Emits a short summary and exits non-zero on failure; use it alongside the inspector for quick health checks.

## MCP Tests & CI

- Local/CI command: `pwsh ./tools/Run-McpContractTests.ps1` (runs Release configuration).
- Run after building the CoreCLR IL2CPP target so the MCP server is present; CI should call this helper as part of the standard build pipeline.

## Troubleshooting

- No discovery file:
  - Check `%TEMP%\unity-explorer-mcp.json` exists (or `UE_MCP_DISCOVERY` override).
  - PowerShell:
    - `Get-ChildItem $env:TEMP\unity-explorer-mcp.json`
    - `Get-Content  $env:TEMP\unity-explorer-mcp.json | Write-Host`
- Wrong base URL or port:
  - Verify `baseUrl` and `port` values in the discovery file match what you expect.
- No events on stream_events:
  - Confirm `modeHints` includes `"streamable-http"` in the discovery file.
  - Use the C# snippet above or `curl -N -H "Content-Type: application/json" -d '{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"stream_events\"}' http://<host>:<port>/mcp`.
  - Trigger activity in the Unity game (logs, selection changes, scene changes, or tool calls) and verify JSON lines appear.

## Notes

- Error envelopes follow JSON-RPC with `error.data.kind` (see `plans/mcp-interface-concept.md` for the exact shapes). Tool failures return `{ ok:false, error:{ kind, message, hint? } }`.
- All Unity API calls are marshalled to the main thread.
