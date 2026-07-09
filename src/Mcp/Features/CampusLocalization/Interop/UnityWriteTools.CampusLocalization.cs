#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Set live text on a Campus/TMP text object. Preserves the string exactly; optional horizontalInVertical layout patch is reversible.")]
        public static async Task<object> SetCampusTextObject(
            string textRef,
            string text,
            bool confirm = false,
            string layoutMode = "preserve",
            CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                return await MainThread.Run(() => CampusLocalizationSupport.SetTextObject(textRef, text, layoutMode, "auto"));
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Apply a reversible layout patch to a Campus/TMP text object, including horizontalInVertical for English in vertical slots.")]
        public static async Task<object> SetCampusTextLayout(
            string textRef,
            string mode,
            string rotation = "auto",
            bool confirm = false,
            CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                var result = await MainThread.Run(() => CampusLocalizationSupport.SetTextLayout(textRef, mode, rotation));
                if (!result.Supported)
                    return new { ok = false, error = new { kind = "Unsupported", message = result.Reason ?? "Layout mode is not supported" }, analysis = result };
                return new { ok = true, textRef, mode, rotation, analysis = result };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Restore the last captured RectTransform/TMP layout state for a Campus/TMP text object.")]
        public static async Task<object> ResetCampusTextLayout(string textRef, bool confirm = false, CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                return await MainThread.Run(() => CampusLocalizationSupport.ResetTextLayout(textRef));
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Set a live Campus i18n value through I18nHelper.SetValue. Runtime override only.")]
        public static async Task<object> SetCampusI18nValue(string key, string value, bool confirm = false, CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                return await MainThread.Run(() => CampusLocalizationSupport.SetI18nValue(key, value));
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Export a Campus localization snapshot JSON under exportRoot or ExplorerFolder/CampusLocalization.")]
        public static async Task<object> ExportCampusLocalizationSnapshot(
            string scope = "loadedText",
            string source = "all",
            bool confirm = false,
            CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                var snapshot = await MainThread.Run(() => CampusLocalizationSupport.ExportLocalizationSnapshot(scope, source));
                return new { ok = true, snapshot };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Export a Campus texture PNG by texture/visual ref, object id, or texture name under exportRoot or ExplorerFolder/CampusLocalization.")]
        public static async Task<object> ExportCampusTexture(string refOrNameOrId, bool confirm = false, CancellationToken ct = default)
        {
            var gate = CheckCampusWriteGate(confirm);
            if (gate != null) return gate;
            try
            {
                var export = await MainThread.Run(() => CampusLocalizationSupport.ExportTexture(refOrNameOrId));
                return new { ok = true, export };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        private static object? CheckCampusWriteGate(bool confirm)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            return null;
        }
    }
}
#endif
