using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp.ContractTests;

public class CampusLocalizationContractTests
{
    [Fact]
    public void Campus_Localization_Source_Registers_Expected_Tools()
    {
        var repoRoot = FindRepoRoot();
        var readTools = File.ReadAllText(Path.Combine(repoRoot, "src", "Mcp", "Features", "CampusLocalization", "Interop", "UnityReadTools.CampusLocalization.cs"));
        var writeTools = File.ReadAllText(Path.Combine(repoRoot, "src", "Mcp", "Features", "CampusLocalization", "Interop", "UnityWriteTools.CampusLocalization.cs"));
        var support = File.ReadAllText(Path.Combine(repoRoot, "src", "Mcp", "Features", "CampusLocalization", "Interop", "CampusLocalizationSupport.cs"));

        foreach (var tool in new[]
        {
            "GetCampusStatus",
            "ListCampusTextObjects",
            "ReadCampusTextObject",
            "AnalyzeCampusTextObject",
            "ListCampusI18nKeys",
            "ReadCampusI18nKey",
            "ListCampusVisualObjects",
            "ReadCampusVisualObject",
            "SearchCampusAssets",
            "ResolveCampusAsset",
            "ListCampusAssetLoadEvents"
        })
        {
            readTools.Should().Contain(tool);
        }

        foreach (var tool in new[]
        {
            "SetCampusTextObject",
            "SetCampusTextLayout",
            "ResetCampusTextLayout",
            "SetCampusI18nValue",
            "ExportCampusLocalizationSnapshot",
            "ExportCampusTexture"
        })
        {
            writeTools.Should().Contain(tool);
        }

        support.Should().Contain("Campus.Common.CampusText");
        support.Should().Contain("TMPro.TMP_Text");
        support.Should().Contain("Qua.UI.TextMeshProUGUIRuby");
        support.Should().Contain("Campus.ADV.MessageText");
        support.Should().Contain("I18nHelper");
        support.Should().Contain("Campus.Common.Octo.TextureResourceView");
        support.Should().Contain("onAssetBundleLoadInterceptor");
    }

    [Fact]
    public void Campus_Localization_Dtos_Expose_Text_I18n_Visual_And_Asset_Shapes()
    {
        var repoRoot = FindRepoRoot();
        var dto = File.ReadAllText(Path.Combine(repoRoot, "src", "Mcp", "Dto", "CampusLocalizationDto.cs"));

        dto.Should().Contain("CampusStatusDto");
        dto.Should().Contain("CampusTextObjectDto");
        dto.Should().Contain("CampusTextLayoutAnalysisDto");
        dto.Should().Contain("CampusI18nEntryDto");
        dto.Should().Contain("CampusVisualObjectDto");
        dto.Should().Contain("CampusTextureExportDto");
        dto.Should().Contain("CampusAssetDto");
        dto.Should().Contain("LocalizeKey");
        dto.Should().Contain("Utf16Length");
        dto.Should().Contain("HasFullWidth");
        dto.Should().Contain("GlyphEvidence");
        dto.Should().Contain("RectTransform");
    }

    [Fact]
    public async Task ListTools_Includes_Campus_Localization_Tools_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "campus-list-tools-test",
            method = "list_tools",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray();

        names.Should().Contain(new[]
        {
            "GetCampusStatus",
            "ListCampusTextObjects",
            "SetCampusTextObject",
            "SetCampusTextLayout",
            "ExportCampusLocalizationSnapshot"
        });

        var analyze = tools.EnumerateArray().First(t => t.GetProperty("name").GetString() == "AnalyzeCampusTextObject");
        var layoutMode = analyze.GetProperty("inputSchema").GetProperty("properties").GetProperty("layoutMode");
        layoutMode.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().Contain("horizontalInVertical");
    }

    [Fact]
    public async Task Campus_Write_Tools_Return_PermissionDenied_When_Writes_Disabled()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = await CallToolAsync(client, "SetConfig", new { allowWrites = (bool?)false }, cts.Token);

        var result = await CallToolAsync(client, "SetCampusTextObject", new { textRef = "ref:missing", text = "日本語 English", confirm = true }, cts.Token);
        if (result is null)
            return;

        result.Value.TryGetProperty("ok", out var ok).Should().BeTrue();
        ok.ValueKind.Should().Be(JsonValueKind.False);
        result.Value.GetProperty("error").GetProperty("kind").GetString().Should().Be("PermissionDenied");
    }

    private static async Task<JsonElement?> CallToolAsync(HttpClient http, string name, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = $"campus-call-tool-{name}",
            method = "call_tool",
            @params = new
            {
                name,
                arguments
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("/message", content, ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("content", out var contentArr) ||
            contentArr.ValueKind != JsonValueKind.Array ||
            contentArr.GetArrayLength() == 0)
            return null;

        var first = contentArr[0];
        return first.TryGetProperty("json", out var jsonEl) ? jsonEl.Clone() : null;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;

            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;

            dir = parent;
        }

        throw new InvalidOperationException("Repo root could not be located from test directory.");
    }
}
