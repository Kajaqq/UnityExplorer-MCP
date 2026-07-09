#if INTEROP
#nullable enable
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Campus localization status: Unity/runtime info, Campus assembly/type availability, interop path health, and feature readiness.")]
        public static async Task<CampusStatusDto> GetCampusStatus(CancellationToken ct = default)
        {
            return await MainThread.Run(CampusLocalizationSupport.GetStatus);
        }

        [McpServerTool, Description("List live Campus/TMP text objects for localization work (paged, bounded, reflection-only).")]
        public static async Task<Page<CampusTextObjectDto>> ListCampusTextObjects(
            string? query = null,
            string source = "scenes",
            string activeState = "active",
            string? kind = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ListTextObjects(query, source, activeState, kind, limit, offset));
        }

        [McpServerTool, Description("Read a Campus/TMP text object by textRef, preserving the string exactly and returning layout/style evidence.")]
        public static async Task<CampusTextObjectDto> ReadCampusTextObject(string textRef, int? maxChars = null, CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ReadTextObject(textRef, maxChars));
        }

        [McpServerTool, Description("Analyze fit/layout for a Campus/TMP text object without leaving mutations behind.")]
        public static async Task<CampusTextLayoutAnalysisDto> AnalyzeCampusTextObject(
            string textRef,
            string? candidateText = null,
            string layoutMode = "preserve",
            CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.AnalyzeTextObject(textRef, candidateText, layoutMode));
        }

        [McpServerTool, Description("List Campus i18n keys and values from I18nHelper (paged, reflection-only).")]
        public static async Task<Page<CampusI18nEntryDto>> ListCampusI18nKeys(
            string? query = null,
            string? valueQuery = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ListI18nKeys(query, valueQuery, limit, offset));
        }

        [McpServerTool, Description("Read one Campus i18n value by key from I18nHelper.")]
        public static async Task<CampusI18nEntryDto> ReadCampusI18nKey(string key, CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ReadI18nKey(key));
        }

        [McpServerTool, Description("List live visual objects useful as localization evidence: RawImage, Image, SpriteRenderer, Renderer, and Campus resource views.")]
        public static async Task<Page<CampusVisualObjectDto>> ListCampusVisualObjects(
            string? query = null,
            string source = "scenes",
            string activeState = "active",
            string? kind = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ListVisualObjects(query, source, activeState, kind, limit, offset));
        }

        [McpServerTool, Description("Read one Campus visual object or texture by visualRef.")]
        public static async Task<CampusVisualObjectDto> ReadCampusVisualObject(string visualRef, CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ReadVisualObject(visualRef));
        }

        [McpServerTool, Description("Search loaded Unity assets as best-effort Campus localization evidence (paged, not a complete catalog).")]
        public static async Task<Page<CampusAssetDto>> SearchCampusAssets(
            string? query = null,
            string? kind = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.SearchAssets(query, kind, limit, offset));
        }

        [McpServerTool, Description("Resolve a Campus asset by ref, obj id, exact name, or partial name.")]
        public static async Task<CampusAssetDto> ResolveCampusAsset(string refOrNameOrId, CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ResolveAsset(refOrNameOrId));
        }

        [McpServerTool, Description("List observed Campus asset-bundle load events after safe subscription to CampusOctoSetupper.onAssetBundleLoadInterceptor.")]
        public static async Task<Page<CampusAssetLoadEventDto>> ListCampusAssetLoadEvents(int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            return await MainThread.Run(() => CampusLocalizationSupport.ListAssetLoadEvents(limit, offset));
        }
    }
}
#endif
