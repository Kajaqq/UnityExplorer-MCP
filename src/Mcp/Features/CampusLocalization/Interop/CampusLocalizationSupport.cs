#if INTEROP
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime;
#endif

namespace UnityExplorer.Mcp
{
    internal static class CampusLocalizationSupport
    {
        private const int MaxPageLimit = 500;
        private const int MaxListTextChars = 512;
        private const int MaxGlyphCheckChars = 2048;
        private const int MaxMissingGlyphs = 32;
        private const int MaxAssetEvents = 2048;

        private static readonly object AssetEventsGate = new();
        private static readonly List<CampusAssetLoadEventDto> AssetLoadEvents = new();
        private static bool _assetLoadSubscribed;
        private static Action<string>? _assetLoadHandler;

        private sealed class CampusTypes
        {
            public Type? CampusText { get; init; }
            public Type? TmpText { get; init; }
            public Type? RubyText { get; init; }
            public Type? MessageText { get; init; }
            public Type? I18nHelper { get; init; }
            public Type? TextureResourceView { get; init; }
            public Type? ImageResourceViewBase { get; init; }
            public Type? CampusOctoSetupper { get; init; }
        }

        private sealed class CampusTextLayoutState
        {
            public RectTransform? Rect { get; init; }
            public Vector2 SizeDelta { get; init; }
            public Vector2 AnchoredPosition { get; init; }
            public Vector2 Pivot { get; init; }
            public Vector2 AnchorMin { get; init; }
            public Vector2 AnchorMax { get; init; }
            public Vector3 LocalEulerAngles { get; init; }
            public Vector3 LocalScale { get; init; }
            public Dictionary<string, object?> TextValues { get; } = new(StringComparer.Ordinal);
        }

        private sealed class CampusTextTarget
        {
            public Component Source { get; init; } = null!;
            public object TextOwner { get; init; } = null!;
            public object RenderOwner { get; init; } = null!;
            public Component? RenderComponent { get; init; }
            public Component? RubyComponent { get; init; }
            public RectTransform? LayoutRect { get; init; }
            public string Kind { get; init; } = "text";
            public bool IsRubyWrapper { get; init; }
        }

        private static readonly Dictionary<string, CampusTextLayoutState> LayoutStates = new(StringComparer.Ordinal);

        public static CampusStatusDto GetStatus()
        {
            var types = GetCampusTypes();
            var availability = new CampusTypeAvailabilityDto(
                CampusText: types.CampusText != null,
                TmpText: types.TmpText != null,
                TextMeshProUGUIRuby: types.RubyText != null,
                MessageText: types.MessageText != null,
                I18nHelper: types.I18nHelper != null,
                TextureResourceView: types.TextureResourceView != null,
                ImageResourceViewBase: types.ImageResourceViewBase != null,
                CampusOctoSetupper: types.CampusOctoSetupper != null);

            var missing = new List<string>();
            if (types.CampusText == null) missing.Add("Campus.Common.CampusText");
            if (types.TmpText == null) missing.Add("TMPro.TMP_Text");
            if (types.RubyText == null) missing.Add("Qua.UI.TextMeshProUGUIRuby");
            if (types.MessageText == null) missing.Add("Campus.ADV.MessageText");
            if (types.I18nHelper == null) missing.Add("I18nHelper");

            var interop = GetInteropPathHealth();
            var ready = missing.Count == 0 && interop.RuntimeDumpInteropLikelyReady;
            var readiness = ready
                ? "ready: v1 text/i18n workflow; asset/resource discovery deferred"
                : missing.Count == 0
                    ? "partial: interop path evidence incomplete; asset/resource discovery deferred"
                    : "partial: required v1 Campus/TMP text types missing; asset/resource discovery deferred";

            return new CampusStatusDto(
                UnityVersion: Application.unityVersion,
                Runtime: Universe.Context.ToString(),
                Platform: Application.platform.ToString(),
                ExplorerVersion: ExplorerCore.VERSION,
                LoadedCampusAssemblies: GetLoadedCampusAssemblies(),
                InteropPathHealth: interop,
                Types: availability,
                Ready: ready,
                MissingRequiredTypes: missing,
                FeatureReadiness: readiness);
        }

        public static Page<CampusTextObjectDto> ListTextObjects(string? query, string? source, string? activeState, string? kind, int? limit, int? offset)
        {
            var lim = ClampLimit(limit);
            var off = Math.Max(0, offset ?? 0);
            var items = new List<CampusTextObjectDto>(lim);
            var matched = 0;

            foreach (var (component, path) in EnumerateTextComponents(source, activeState))
            {
                if (component == null) continue;
                var target = ResolveTextTarget(component);
                if (!MatchesKind(target.Kind, kind)) continue;

                var text = GetText(target) ?? string.Empty;
                var localizeKey = GetLocalizeKey(target);
                if (!MatchesTextQuery(query, component, path, text, localizeKey)) continue;

                if (matched >= off && items.Count < lim)
                    items.Add(BuildTextDto(target, path, MaxListTextChars, checkGlyphs: false));
                matched++;
            }

            return new Page<CampusTextObjectDto>(matched, items);
        }

        public static CampusTextObjectDto ReadTextObject(string textRef, int? maxChars)
        {
            var component = ResolveComponentRef(textRef);
            var target = ResolveTextTarget(component);
            var path = BuildObjectPath(component);
            var max = Math.Max(1, maxChars ?? 65536);
            return BuildTextDto(target, path, max, checkGlyphs: true);
        }

        public static CampusTextLayoutAnalysisDto AnalyzeTextObject(string textRef, string? candidateText, string? layoutMode)
        {
            var component = ResolveComponentRef(textRef);
            var target = ResolveTextTarget(component);
            return AnalyzeLayout(target, candidateText, NormalizeLayoutMode(layoutMode), "auto", wouldMutate: false);
        }

        public static object SetTextObject(string textRef, string text, string? layoutMode, string? rotation)
        {
            var component = ResolveComponentRef(textRef);
            var target = ResolveTextTarget(component);
            var mode = NormalizeLayoutMode(layoutMode);
            CampusTextLayoutAnalysisDto? layout = null;
            if (string.Equals(mode, "horizontalInVertical", StringComparison.OrdinalIgnoreCase))
            {
                var analysis = AnalyzeLayout(target, text, mode, rotation ?? "auto", wouldMutate: true);
                if (!analysis.Supported)
                    return Unsupported(analysis.Reason ?? "Layout mode is not supported for this text object.");

                var stateKey = BuildTextStableKey(target);
                if (!LayoutStates.ContainsKey(stateKey))
                    LayoutStates[stateKey] = CaptureLayoutState(target);

                SetText(target, text);
                var selected = analysis.RecommendedRotation ?? "+90";
                ApplyHorizontalCandidate(target, selected);
                layout = AnalyzeLayout(target, null, mode, selected, wouldMutate: false);
            }
            else
            {
                SetText(target, text);
            }

            return new
            {
                ok = true,
                textRef,
                layoutMode = mode,
                textPreview = MakePreview(text, 160),
                layout
            };
        }

        public static CampusTextLayoutAnalysisDto SetTextLayout(string textRef, string mode, string? rotation)
        {
            var component = ResolveComponentRef(textRef);
            var target = ResolveTextTarget(component);
            return ApplyLayout(target, NormalizeLayoutMode(mode), rotation);
        }

        public static object ResetTextLayout(string textRef)
        {
            var component = ResolveComponentRef(textRef);
            var target = ResolveTextTarget(component);
            var stateKey = BuildTextStableKey(target);
            if (!LayoutStates.TryGetValue(stateKey, out var state))
                return new { ok = false, error = new { kind = "NotFound", message = "No captured layout state for this textRef" } };

            RestoreLayoutState(target, state);
            LayoutStates.Remove(stateKey);
            return new { ok = true, textRef };
        }

        public static Page<CampusI18nEntryDto> ListI18nKeys(string? query, string? valueQuery, int? limit, int? offset)
        {
            var lim = ClampLimit(limit);
            var off = Math.Max(0, offset ?? 0);
            var items = new List<CampusI18nEntryDto>(lim == int.MaxValue ? 1024 : lim);
            var matched = 0;

            foreach (var key in GetI18nKeys())
            {
                if (!string.IsNullOrEmpty(query) && key.IndexOf(query!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var value = GetI18nValue(key);
                if (!string.IsNullOrEmpty(valueQuery) && (value == null || value.IndexOf(valueQuery!, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                if (matched >= off && items.Count < lim)
                    items.Add(BuildI18nEntry(key, value));
                matched++;
            }

            return new Page<CampusI18nEntryDto>(matched, items);
        }

        public static CampusI18nEntryDto ReadI18nKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));
            if (!I18nContainsKey(key))
                throw new InvalidOperationException("NotFound");

            var value = GetI18nValue(key);
            return BuildI18nEntry(key, value);
        }

        public static object SetI18nValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));
            var helper = GetCampusTypes().I18nHelper ?? throw new InvalidOperationException("I18nHelper not found");
            var setValue = FindMethod(helper, "SetValue", isStatic: true, parameterCount: 2);
            if (setValue == null) throw new InvalidOperationException("I18nHelper.SetValue not found");
            setValue.Invoke(null, new object?[] { key, value });
            return new { ok = true, key, utf16Length = value.Length };
        }

        public static CampusI18nSnapshotDto ExportLocalizationSnapshot(string? scope, string? source)
        {
            var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "loadedText" : scope!;
            var normalizedSource = string.IsNullOrWhiteSpace(source) ? "all" : source!;
            var includeText = string.Equals(normalizedScope, "loadedText", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase);
            var includeI18n = string.Equals(normalizedScope, "i18n", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase);
            if (!includeText && !includeI18n)
                throw new ArgumentException("scope must be loadedText, i18n, or both", nameof(scope));

            IReadOnlyList<CampusTextObjectDto> loadedText = includeText
                ? ListTextObjects(null, normalizedSource, "all", null, MaxPageLimit, 0).Items
                : Array.Empty<CampusTextObjectDto>();
            IReadOnlyList<CampusI18nEntryDto> i18n = includeI18n
                ? ListI18nKeys(null, null, int.MaxValue, 0).Items
                : Array.Empty<CampusI18nEntryDto>();

            var exportRoot = GetExportRoot();
            Directory.CreateDirectory(exportRoot);
            var path = Path.Combine(exportRoot, "campus-localization-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            var payload = new
            {
                scope = normalizedScope,
                source = normalizedSource,
                createdUtc = DateTime.UtcNow.ToString("O"),
                loadedText,
                i18n
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return new CampusI18nSnapshotDto(normalizedScope, normalizedSource, path, loadedText.Count, i18n.Count, DateTime.UtcNow.ToString("O"));
        }

        public static Page<CampusVisualObjectDto> ListVisualObjects(string? query, string? source, string? activeState, string? kind, int? limit, int? offset)
        {
            var lim = ClampLimit(limit);
            var off = Math.Max(0, offset ?? 0);
            var items = new List<CampusVisualObjectDto>(lim);
            var matched = 0;

            foreach (var (component, path) in EnumerateVisualComponents(source, activeState))
            {
                if (component == null) continue;
                var visualKind = GetVisualKind(component);
                if (!MatchesKind(visualKind, kind)) continue;
                var dto = BuildVisualDto(component, path);
                if (!MatchesVisualQuery(query, dto)) continue;

                if (matched >= off && items.Count < lim)
                    items.Add(dto);
                matched++;
            }

            return new Page<CampusVisualObjectDto>(matched, items);
        }

        public static CampusVisualObjectDto ReadVisualObject(string visualRef)
        {
            if (!McpObjectRefs.TryGet(visualRef, out var value) || value == null)
                throw new InvalidOperationException("NotFound");
            if (value is Texture texture)
                return BuildVisualDto(texture);
            if (value is Component component)
                return BuildVisualDto(component, BuildObjectPath(component));
            throw new ArgumentException("visualRef does not refer to a supported visual object");
        }

        public static CampusTextureExportDto ExportTexture(string refOrNameOrId)
        {
            var (texture, source) = ResolveTexture(refOrNameOrId);
            if (texture == null) throw new InvalidOperationException("NotFound");
            var exportRoot = GetExportRoot();
            Directory.CreateDirectory(exportRoot);

            var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(texture.name) ? "texture" : texture.name);
            var path = Path.Combine(exportRoot, safeName + "-" + texture.GetInstanceID() + ".png");
            var (bytes, usedReadablePath) = EncodeTexturePng(texture);
            File.WriteAllBytes(path, bytes);
            return new CampusTextureExportDto(true, path, texture.name, texture.width, texture.height, usedReadablePath, source);
        }

        public static Page<CampusAssetDto> SearchAssets(string? query, string? kind, int? limit, int? offset)
        {
            var lim = ClampLimit(limit);
            var off = Math.Max(0, offset ?? 0);
            var items = new List<CampusAssetDto>(lim);
            var matched = 0;

            foreach (var asset in EnumerateAssets())
            {
                if (asset == null) continue;
                var assetKind = GetAssetKind(asset);
                if (!MatchesKind(assetKind, kind)) continue;
                var typeName = SafeTypeName(ActualType(asset));
                if (!string.IsNullOrEmpty(query) &&
                    (asset.name ?? string.Empty).IndexOf(query!, StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf(query!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (matched >= off && items.Count < lim)
                    items.Add(BuildAssetDto(asset, assetKind, null));
                matched++;
            }

            return new Page<CampusAssetDto>(matched, items);
        }

        public static CampusAssetDto ResolveAsset(string refOrNameOrId)
        {
            if (string.IsNullOrWhiteSpace(refOrNameOrId)) throw new ArgumentException("refOrNameOrId is required");
            if (refOrNameOrId.StartsWith("ref:", StringComparison.Ordinal))
            {
                if (!McpObjectRefs.TryGet(refOrNameOrId, out var value) || value == null)
                    throw new InvalidOperationException("NotFound");
                if (value is UnityEngine.Object unityObject)
                    return BuildAssetDto(unityObject, GetAssetKind(unityObject), "resolved from ref");
                return new CampusAssetDto(refOrNameOrId, value.ToString() ?? "<object>", SafeTypeName(ActualType(value)), "object", null, "non-Unity object ref");
            }

            if (refOrNameOrId.StartsWith("obj:", StringComparison.Ordinal) &&
                int.TryParse(refOrNameOrId.Substring(4), out var iid))
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                return BuildAssetDto(go, "gameObject", "resolved from object id");
            }

            foreach (var asset in EnumerateAssets())
            {
                if (asset != null && string.Equals(asset.name, refOrNameOrId, StringComparison.Ordinal))
                    return BuildAssetDto(asset, GetAssetKind(asset), "resolved by exact name");
            }

            foreach (var asset in EnumerateAssets())
            {
                if (asset != null && (asset.name ?? string.Empty).IndexOf(refOrNameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return BuildAssetDto(asset, GetAssetKind(asset), "resolved by partial name");
            }

            throw new InvalidOperationException("NotFound");
        }

        public static Page<CampusAssetLoadEventDto> ListAssetLoadEvents(int? limit, int? offset)
        {
            TrySubscribeAssetLoadEvents();
            var lim = ClampLimit(limit);
            var off = Math.Max(0, offset ?? 0);
            lock (AssetEventsGate)
            {
                var total = AssetLoadEvents.Count;
                var items = AssetLoadEvents.Skip(off).Take(lim).ToArray();
                return new Page<CampusAssetLoadEventDto>(total, items);
            }
        }

        private static CampusTextLayoutAnalysisDto ApplyLayout(CampusTextTarget target, string mode, string? rotation)
        {
            if (!string.Equals(mode, "horizontalInVertical", StringComparison.OrdinalIgnoreCase))
                return AnalyzeLayout(target, null, mode, rotation ?? "auto", wouldMutate: false);

            var analysis = AnalyzeLayout(target, null, mode, rotation ?? "auto", wouldMutate: true);
            if (!analysis.Supported)
                return analysis;

            var stateKey = BuildTextStableKey(target);
            if (!LayoutStates.ContainsKey(stateKey))
                LayoutStates[stateKey] = CaptureLayoutState(target);

            var selected = analysis.RecommendedRotation ?? "+90";
            var rect = target.LayoutRect;
            if (rect == null)
                return analysis with { Supported = false, Reason = "RectTransform missing" };

            ApplyHorizontalCandidate(target, selected);
            ForceTextRefresh(target);
            return AnalyzeLayout(target, null, mode, selected, wouldMutate: false);
        }

        private static CampusTextLayoutAnalysisDto AnalyzeLayout(CampusTextTarget target, string? candidateText, string mode, string rotation, bool wouldMutate)
        {
            var rect = target.LayoutRect;
            var before = MeasureLayout(target, rect);
            if (!string.Equals(mode, "horizontalInVertical", StringComparison.OrdinalIgnoreCase))
            {
                return new CampusTextLayoutAnalysisDto(mode, true, null, before, Array.Empty<CampusLayoutCandidateDto>(), null, before.Overflow, wouldMutate);
            }

            if (rect == null)
                return new CampusTextLayoutAnalysisDto(mode, false, "RectTransform missing", before, Array.Empty<CampusLayoutCandidateDto>(), null, before.Overflow, wouldMutate);

            var state = CaptureLayoutState(target);
            var originalText = GetText(target);
            var candidates = new List<CampusLayoutCandidateDto>(2);
            var rotations = string.Equals(rotation, "auto", StringComparison.OrdinalIgnoreCase)
                ? new[] { "+90", "-90" }
                : new[] { NormalizeRotation(rotation) };

            try
            {
                if (candidateText != null)
                    SetText(target, candidateText);

                foreach (var candidateRotation in rotations)
                {
                    RestoreLayoutState(target, state);
                    if (candidateText != null)
                        SetText(target, candidateText);

                    ApplyHorizontalCandidate(target, candidateRotation);
                    ForceTextRefresh(target);
                    var metrics = MeasureLayout(target, rect);
                    candidates.Add(new CampusLayoutCandidateDto(candidateRotation, metrics, ScoreLayout(metrics)));
                }
            }
            finally
            {
                RestoreLayoutState(target, state);
                if (candidateText != null && originalText != null)
                    SetText(target, originalText);
                ForceTextRefresh(target);
            }

            var best = candidates
                .OrderBy(c => c.Score)
                .ThenBy(c => c.Rotation == "+90" ? 0 : 1)
                .FirstOrDefault();

            return new CampusTextLayoutAnalysisDto(
                mode,
                true,
                null,
                before,
                candidates,
                best?.Rotation,
                best?.Metrics.Overflow ?? before.Overflow,
                wouldMutate);
        }

        private static void ApplyHorizontalCandidate(CampusTextTarget target, string rotation)
        {
            var rect = target.LayoutRect;
            if (rect == null) return;
            var slotWidth = Math.Max(1f, rect.rect.width);
            var slotHeight = Math.Max(1f, rect.rect.height);

            SetTextMember(target.RenderOwner, "enableWordWrapping", false);
            SetTextMember(target.RenderOwner, "enableAutoSizing", false);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, slotHeight);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, slotWidth);
            var euler = rect.localEulerAngles;
            euler.z = string.Equals(NormalizeRotation(rotation), "-90", StringComparison.Ordinal) ? -90f : 90f;
            rect.localEulerAngles = euler;

            ForceTextRefresh(target);
            var metrics = MeasureLayout(target, rect);
            if ((metrics.PreferredWidth.HasValue && metrics.PreferredWidth.Value > metrics.RectWidth + 0.5f) || metrics.Overflow)
            {
                SetTextMember(target.RenderOwner, "enableWordWrapping", true);
                ForceTextRefresh(target);
            }

            metrics = MeasureLayout(target, rect);
            if ((metrics.PreferredHeight.HasValue && metrics.PreferredHeight.Value > metrics.RectHeight + 0.5f) || metrics.Overflow)
            {
                var fontSize = GetFloatMember(target.RenderOwner, "fontSize");
                if (fontSize.HasValue)
                {
                    SetTextMember(target.RenderOwner, "fontSizeMax", fontSize.Value);
                    SetTextMember(target.RenderOwner, "fontSizeMin", Math.Max(1f, fontSize.Value * 0.55f));
                }
                SetTextMember(target.RenderOwner, "enableAutoSizing", true);
                ForceTextRefresh(target);
            }
        }

        private static CampusTextLayoutState CaptureLayoutState(CampusTextTarget target)
        {
            var rect = target.LayoutRect;
            var state = new CampusTextLayoutState
            {
                Rect = rect,
                SizeDelta = rect != null ? rect.sizeDelta : Vector2.zero,
                AnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero,
                Pivot = rect != null ? rect.pivot : Vector2.zero,
                AnchorMin = rect != null ? rect.anchorMin : Vector2.zero,
                AnchorMax = rect != null ? rect.anchorMax : Vector2.zero,
                LocalEulerAngles = rect != null ? rect.localEulerAngles : Vector3.zero,
                LocalScale = rect != null ? rect.localScale : Vector3.one
            };

            foreach (var name in new[] { "enableWordWrapping", "overflowMode", "enableAutoSizing", "fontSize", "fontSizeMin", "fontSizeMax", "alignment", "margin" })
                state.TextValues[name] = GetMemberValue(target.RenderOwner, name);
            return state;
        }

        private static void RestoreLayoutState(CampusTextTarget target, CampusTextLayoutState state)
        {
            if (state.Rect != null)
            {
                state.Rect.sizeDelta = state.SizeDelta;
                state.Rect.anchoredPosition = state.AnchoredPosition;
                state.Rect.pivot = state.Pivot;
                state.Rect.anchorMin = state.AnchorMin;
                state.Rect.anchorMax = state.AnchorMax;
                state.Rect.localEulerAngles = state.LocalEulerAngles;
                state.Rect.localScale = state.LocalScale;
            }

            foreach (var kv in state.TextValues)
                SetTextMember(target.RenderOwner, kv.Key, kv.Value);
            ForceTextRefresh(target);
        }

        private static CampusLayoutMetricsDto MeasureLayout(CampusTextTarget target, RectTransform? rect)
        {
            ForceTextRefresh(target);
            var style = BuildTextStyle(target);
            var rectWidth = rect != null ? Math.Max(0f, rect.rect.width) : 0f;
            var rectHeight = rect != null ? Math.Max(0f, rect.rect.height) : 0f;
            var overflow = style.IsTextOverflowing == true;
            var reasons = new List<string>();
            if (style.PreferredWidth.HasValue && rectWidth > 0f && style.PreferredWidth.Value > rectWidth + 0.5f)
            {
                overflow = true;
                reasons.Add("preferredWidth>rectWidth");
            }
            if (style.PreferredHeight.HasValue && rectHeight > 0f && style.PreferredHeight.Value > rectHeight + 0.5f)
            {
                overflow = true;
                reasons.Add("preferredHeight>rectHeight");
            }
            if (style.IsTextOverflowing == true)
                reasons.Add("tmpOverflow");

            return new CampusLayoutMetricsDto(
                style.PreferredWidth,
                style.PreferredHeight,
                style.RenderedWidth,
                style.RenderedHeight,
                rectWidth,
                rectHeight,
                overflow,
                reasons.Count == 0 ? null : string.Join(",", reasons));
        }

        private static float ScoreLayout(CampusLayoutMetricsDto metrics)
        {
            var score = metrics.Overflow ? 100000f : 0f;
            if (metrics.PreferredWidth.HasValue)
                score += Math.Max(0f, metrics.PreferredWidth.Value - metrics.RectWidth);
            if (metrics.PreferredHeight.HasValue)
                score += Math.Max(0f, metrics.PreferredHeight.Value - metrics.RectHeight);
            return score;
        }

        private static object Unsupported(string message)
            => new { ok = false, error = new { kind = "Unsupported", message } };

        private static IEnumerable<(Component component, string path)> EnumerateTextComponents(string? source, string? activeState)
        {
            var types = GetCampusTypes();
            var componentTypes = DistinctComponentTypes(new[]
            {
                types.CampusText,
                types.TmpText,
                types.RubyText,
                types.MessageText,
                typeof(Text)
            });
            var seenComponents = new HashSet<int>();

            foreach (var (go, path) in EnumerateGameObjects(source))
            {
                if (!MatchesActiveState(go, activeState)) continue;
                foreach (var component in EnumerateComponentsByType(go, componentTypes))
                {
                    if (component != null && seenComponents.Add(component.GetInstanceID()) && IsTextComponent(component))
                        yield return (component, path);
                }
            }
        }

        private static IEnumerable<(Component component, string path)> EnumerateVisualComponents(string? source, string? activeState)
        {
            var types = GetCampusTypes();
            var componentTypes = DistinctComponentTypes(new[]
            {
                typeof(RawImage),
                typeof(Image),
                typeof(SpriteRenderer),
                typeof(Renderer),
                types.TextureResourceView,
                types.ImageResourceViewBase
            });
            var seenComponents = new HashSet<int>();

            foreach (var (go, path) in EnumerateGameObjects(source))
            {
                if (!MatchesActiveState(go, activeState)) continue;
                foreach (var component in EnumerateComponentsByType(go, componentTypes))
                {
                    if (component != null && seenComponents.Add(component.GetInstanceID()) && IsVisualComponent(component))
                        yield return (component, path);
                }
            }
        }

        private static IReadOnlyList<Type> DistinctComponentTypes(IEnumerable<Type?> types)
        {
            var result = new List<Type>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                if (type == null || type.ContainsGenericParameters) continue;
                try
                {
                    if (!typeof(Component).IsAssignableFrom(type)) continue;
                }
                catch
                {
                    continue;
                }

                var key = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
                if (seen.Add(key))
                    result.Add(type);
            }
            return result;
        }

        private static IEnumerable<Component> EnumerateComponentsByType(GameObject go, IReadOnlyList<Type> componentTypes)
        {
            foreach (var type in componentTypes)
            {
#if CPP
                Il2CppSystem.Type? il2cppType = null;
                try { il2cppType = Il2CppType.From(type, false); }
                catch { }
                if (il2cppType == null) continue;
                Component[] components;
                try { components = go.GetComponents(il2cppType); }
                catch { continue; }
#else
                Component[] components;
                try { components = go.GetComponents(type); }
                catch { continue; }
#endif
                foreach (var component in components)
                {
                    if (component != null)
                        yield return component;
                }
            }
        }

        private static IEnumerable<(GameObject go, string path)> EnumerateGameObjects(string? source)
        {
            var mode = string.IsNullOrWhiteSpace(source) ? "scenes" : source!;
            var seen = new HashSet<int>();

            if (string.Equals(mode, "scenes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var item in UnityQuery.Traverse(root))
                        {
                            if (item.go != null && seen.Add(item.go.GetInstanceID()))
                                yield return item;
                        }
                    }
                }
            }

            if (string.Equals(mode, "resources", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                GameObject[] all;
                try { all = Resources.FindObjectsOfTypeAll<GameObject>(); }
                catch { yield break; }
                foreach (var go in all)
                {
                    if (go == null || !seen.Add(go.GetInstanceID())) continue;
                    yield return (go, BuildObjectPath(go));
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> EnumerateAssets()
        {
            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>(); }
            catch { yield break; }
            foreach (var asset in all)
            {
                if (asset != null)
                    yield return asset;
            }
        }

        private static CampusTextObjectDto BuildTextDto(CampusTextTarget target, string path, int maxChars, bool checkGlyphs)
        {
            var component = target.Source;
            var text = GetText(target) ?? string.Empty;
            var truncated = text.Length > maxChars;
            var returnedText = truncated ? text.Substring(0, maxChars) : text;
            var go = component.gameObject;
            var componentType = SafeTypeName(ActualType(component));
            var kind = target.Kind;

            return new CampusTextObjectDto(
                TextRef: CaptureStableRef(component, BuildTextStableKey(target), "campusText", componentType, go.name, path, new[] { kind }),
                ObjectId: ObjectId(go),
                ObjectPath: path,
                ObjectName: go.name,
                ComponentType: componentType,
                Kind: kind,
                ActiveSelf: go.activeSelf,
                ActiveInHierarchy: go.activeInHierarchy,
                LocalizeKey: GetLocalizeKey(target),
                Text: returnedText,
                Truncated: truncated,
                Preview: MakePreview(text, 160),
                Flags: BuildTextFlags(text),
                Style: BuildTextStyle(target),
                RectTransform: BuildRectDto(target.LayoutRect),
                Ruby: BuildRubyDto(component),
                GlyphEvidence: checkGlyphs ? CheckMissingGlyphs(target, text) : new CampusGlyphEvidenceDto(false, null, Array.Empty<string>(), Array.Empty<int>(), null));
        }

        private static CampusTextFlagsDto BuildTextFlags(string text)
        {
            var hasCjk = false;
            var hasFullWidth = false;
            foreach (var ch in text)
            {
                if (IsCjk(ch)) hasCjk = true;
                if (IsFullWidth(ch)) hasFullWidth = true;
                if (hasCjk && hasFullWidth) break;
            }

            return new CampusTextFlagsDto(
                Utf16Length: text.Length,
                HasCjk: hasCjk,
                HasFullWidth: hasFullWidth,
                HasMarkup: text.IndexOf('<') >= 0 && text.IndexOf('>') >= 0);
        }

        private static CampusI18nEntryDto BuildI18nEntry(string key, string? value)
        {
            var flags = BuildTextFlags(value ?? string.Empty);
            return new CampusI18nEntryDto(key, value, flags.Utf16Length, flags.HasCjk, flags.HasFullWidth);
        }

        private static CampusTextStyleDto BuildTextStyle(CampusTextTarget target)
        {
            var owner = target.RenderOwner;
            return new CampusTextStyleDto(
                Font: ObjectName(GetMemberValue(owner, "font")),
                FontId: GetStringMember(owner, "_fontId") ?? GetStringMember(target.Source, "_fontId"),
                Material: ObjectName(GetMemberValue(owner, "fontSharedMaterial") ?? GetMemberValue(owner, "fontMaterial") ?? GetMemberValue(owner, "materialForRendering") ?? GetMemberValue(owner, "material")),
                Alignment: MemberToString(GetMemberValue(owner, "alignment")),
                EnableWordWrapping: GetBoolMember(owner, "enableWordWrapping"),
                OverflowMode: MemberToString(GetMemberValue(owner, "overflowMode") ?? GetMemberValue(owner, "horizontalOverflow") ?? GetMemberValue(owner, "verticalOverflow")),
                FontSize: GetFloatMember(owner, "fontSize"),
                EnableAutoSizing: GetBoolMember(owner, "enableAutoSizing") ?? GetBoolMember(owner, "resizeTextForBestFit"),
                FontSizeMin: GetFloatMember(owner, "fontSizeMin") ?? GetFloatMember(owner, "resizeTextMinSize"),
                FontSizeMax: GetFloatMember(owner, "fontSizeMax") ?? GetFloatMember(owner, "resizeTextMaxSize"),
                Margin: Vector4Dto(GetMemberValue(owner, "margin")),
                PreferredWidth: GetFloatMember(owner, "preferredWidth"),
                PreferredHeight: GetFloatMember(owner, "preferredHeight"),
                RenderedWidth: GetFloatMember(owner, "renderedWidth"),
                RenderedHeight: GetFloatMember(owner, "renderedHeight"),
                IsTextOverflowing: GetBoolMember(owner, "isTextOverflowing"),
                StyleType: MemberToString(GetMemberValue(target.Source, "StyleType") ?? GetMemberValue(target.Source, "_styleType")));
        }

        private static CampusRectTransformDto? BuildRectDto(RectTransform? rect)
        {
            if (rect == null) return null;
            return new CampusRectTransformDto(
                Width: rect.rect.width,
                Height: rect.rect.height,
                SizeDelta: new CampusVector2Dto(rect.sizeDelta.x, rect.sizeDelta.y),
                AnchoredPosition: new CampusVector2Dto(rect.anchoredPosition.x, rect.anchoredPosition.y),
                Pivot: new CampusVector2Dto(rect.pivot.x, rect.pivot.y),
                AnchorMin: new CampusVector2Dto(rect.anchorMin.x, rect.anchorMin.y),
                AnchorMax: new CampusVector2Dto(rect.anchorMax.x, rect.anchorMax.y),
                LocalEulerZ: rect.localEulerAngles.z);
        }

        private static CampusRubyDto BuildRubyDto(Component component)
        {
            var isRuby = IsTypeOrBase(ActualType(component), "Qua.UI.TextMeshProUGUIRuby");
            return new CampusRubyDto(
                IsRubyComponent: isRuby,
                HasRuby: GetBoolMember(component, "HasRuby"),
                RubyLineHeight: GetStringMember(component, "RubyLineHeight") ?? GetStringMember(component, "_rubyLineHeight"),
                RubyMargin: GetFloatMember(component, "RubyMargin") ?? GetFloatMember(component, "_rubyMargin"),
                AdjustXPosition: GetBoolMember(component, "AdjustXPosition") ?? GetBoolMember(component, "_adjustXPosition"));
        }

        private static CampusGlyphEvidenceDto CheckMissingGlyphs(CampusTextTarget target, string text)
        {
            var font = GetMemberValue(target.RenderOwner, "font");
            if (font == null)
                return new CampusGlyphEvidenceDto(false, null, Array.Empty<string>(), Array.Empty<int>(), "font unavailable");

            var missingChars = new List<string>();
            var missingCodePoints = new List<int>();
            try
            {
                var checkedChars = 0;
                foreach (var ch in text)
                {
                    if (char.IsControl(ch)) continue;
                    if (checkedChars++ >= MaxGlyphCheckChars) break;
                    if (!FontHasCharacter(font, ch))
                    {
                        missingChars.Add(ch.ToString());
                        missingCodePoints.Add(ch);
                        if (missingChars.Count >= MaxMissingGlyphs) break;
                    }
                }
                return new CampusGlyphEvidenceDto(true, ObjectName(font), missingChars, missingCodePoints, null);
            }
            catch (Exception ex)
            {
                return new CampusGlyphEvidenceDto(false, ObjectName(font), missingChars, missingCodePoints, ex.Message);
            }
        }

        private static bool FontHasCharacter(object font, char ch)
        {
            var methods = ActualType(font)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => string.Equals(m.Name, "HasCharacter", StringComparison.Ordinal))
                .ToArray();

            foreach (var method in methods)
            {
                var ps = method.GetParameters();
                if (ps.Length < 1) continue;
                try
                {
                    object?[] args;
                    var p0 = ps[0].ParameterType;
                    if (p0 == typeof(char)) args = new object?[] { ch };
                    else if (p0 == typeof(int)) args = new object?[] { (int)ch };
                    else if (p0 == typeof(uint)) args = new object?[] { (uint)ch };
                    else continue;

                    if (ps.Length == 2 && ps[1].ParameterType == typeof(bool))
                        args = new[] { args[0], (object?)false };
                    else if (ps.Length != args.Length)
                        continue;

                    var result = method.Invoke(font, args);
                    if (result is bool b) return b;
                }
                catch { }
            }

            return true;
        }

        private static bool MatchesTextQuery(string? query, Component component, string path, string text, string? localizeKey)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return text.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   component.name.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   SafeTypeName(ActualType(component)).IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (localizeKey != null && localizeKey.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool MatchesVisualQuery(string? query, CampusVisualObjectDto dto)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return dto.ObjectPath.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dto.ObjectName.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dto.ComponentType.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (dto.AssetName != null && dto.AssetName.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (dto.TextureName != null && dto.TextureName.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool MatchesActiveState(GameObject go, string? activeState)
        {
            if (string.IsNullOrWhiteSpace(activeState) || string.Equals(activeState, "active", StringComparison.OrdinalIgnoreCase))
                return go.activeInHierarchy;
            if (string.Equals(activeState, "inactive", StringComparison.OrdinalIgnoreCase))
                return !go.activeInHierarchy;
            return string.Equals(activeState, "all", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesKind(string actual, string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return true;
            return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTextComponent(Component component)
        {
            var t = ActualType(component);
            return IsTypeOrBase(t, "Campus.Common.CampusText") ||
                   IsTypeOrBase(t, "TMPro.TMP_Text") ||
                   IsTypeOrBase(t, "Qua.UI.TextMeshProUGUIRuby") ||
                   IsTypeOrBase(t, "Campus.ADV.MessageText") ||
                   typeof(Text).IsAssignableFrom(t);
        }

        private static string GetTextKind(Component component)
        {
            var t = ActualType(component);
            if (IsTypeOrBase(t, "Campus.ADV.MessageText")) return "messageText";
            if (IsTypeOrBase(t, "Qua.UI.TextMeshProUGUIRuby")) return "ruby";
            if (IsTypeOrBase(t, "Campus.Common.CampusText")) return "campusText";
            if (IsTypeOrBase(t, "TMPro.TMP_Text")) return "tmpText";
            if (typeof(Text).IsAssignableFrom(t)) return "uiText";
            return "text";
        }

        private static CampusTextTarget ResolveTextTarget(Component component)
        {
            var kind = GetTextKind(component);
            object textOwner = component;
            object renderOwner = component;
            Component? renderComponent = component;
            Component? rubyComponent = null;
            var isRubyWrapper = string.Equals(kind, "ruby", StringComparison.Ordinal) ||
                                string.Equals(kind, "messageText", StringComparison.Ordinal);

            if (isRubyWrapper)
            {
                var targetTmp = GetMemberValue(component, "TargetTMP") ??
                                GetMemberValue(component, "_targetTMP") ??
                                FindComponentByTypeName(component.gameObject, "TMPro.TextMeshProUGUI", "TMPro.TMP_Text");
                var rubyTmp = GetMemberValue(component, "RubyTMP") ??
                              GetMemberValue(component, "_rubyTMP");

                if (targetTmp != null)
                    renderOwner = targetTmp;
                renderComponent = renderOwner as Component;
                if (renderComponent == null)
                    renderComponent = component;
                rubyComponent = rubyTmp as Component;
            }

            RectTransform? layoutRect = null;
            if (renderComponent != null)
                layoutRect = renderComponent.GetComponent<RectTransform>();
            if (layoutRect == null)
                layoutRect = component.GetComponent<RectTransform>();

            return new CampusTextTarget
            {
                Source = component,
                TextOwner = textOwner,
                RenderOwner = renderOwner,
                RenderComponent = renderComponent,
                RubyComponent = rubyComponent,
                LayoutRect = layoutRect,
                Kind = kind,
                IsRubyWrapper = isRubyWrapper
            };
        }

        private static string? GetText(CampusTextTarget target)
        {
            if (target.IsRubyWrapper)
                return GetStringMember(target.TextOwner, "Text") ?? GetStringMember(target.TextOwner, "_text");
            return GetStringMember(target.TextOwner, "text") ?? GetStringMember(target.TextOwner, "Text") ?? GetStringMember(target.TextOwner, "m_text");
        }

        private static void SetText(CampusTextTarget target, string text)
        {
            if (target.IsRubyWrapper && SetTextMember(target.TextOwner, "Text", text))
            {
                ForceTextRefresh(target);
                return;
            }

            if (!SetTextMember(target.TextOwner, "text", text) &&
                !SetTextMember(target.TextOwner, "Text", text) &&
                !SetTextMember(target.TextOwner, "m_text", text) &&
                !InvokeStringMethod(target.TextOwner, "SetText", text) &&
                (!ReferenceEquals(target.RenderOwner, target.TextOwner) && !InvokeStringMethod(target.RenderOwner, "SetText", text)))
                throw new InvalidOperationException("Text setter not found");

            ForceTextRefresh(target);
        }

        private static void ForceTextRefresh(CampusTextTarget target)
        {
            ForceTextRefreshObject(target.TextOwner);
            if (!ReferenceEquals(target.RenderOwner, target.TextOwner))
                ForceTextRefreshObject(target.RenderOwner);
            if (target.RubyComponent != null)
                ForceTextRefreshObject(target.RubyComponent);

            Canvas.ForceUpdateCanvases();
            ForceRebuildRect(target.LayoutRect);
            if (target.RenderComponent != null)
                ForceRebuildRect(target.RenderComponent.GetComponent<RectTransform>());
            if (target.RubyComponent != null)
                ForceRebuildRect(target.RubyComponent.GetComponent<RectTransform>());
        }

        private static void ForceTextRefreshObject(object owner)
        {
            InvokeNoThrow(owner, "ForceMeshUpdate", true, true);
            InvokeNoThrow(owner, "ForceMeshUpdate");
            InvokeNoThrow(owner, "SetVerticesDirty");
            InvokeNoThrow(owner, "SetLayoutDirty");
        }

        private static void ForceRebuildRect(RectTransform? rect)
        {
            if (rect == null) return;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(rect); } catch { }
        }

        private static Component ResolveComponentRef(string refId)
        {
            if (string.IsNullOrWhiteSpace(refId) || !refId.StartsWith("ref:", StringComparison.Ordinal))
                throw new ArgumentException("Invalid textRef; expected ref:...");
            if (!McpObjectRefs.TryGet(refId, out var value) || value == null)
                throw new InvalidOperationException("NotFound");
            if (value is Component component)
                return component;
            throw new ArgumentException("Reference does not point to a Unity component");
        }

        private static CampusVisualObjectDto BuildVisualDto(Component component, string path)
        {
            var texture = TryGetTexture(component, out var evidence, out var assetName);
            var textureRef = texture != null
                ? CaptureRef(texture, "campusTexture", SafeTypeName(ActualType(texture)), texture.name, null, new[] { "texture" })
                : null;
            var go = component.gameObject;
            return new CampusVisualObjectDto(
                VisualRef: CaptureRef(component, "campusVisual", SafeTypeName(ActualType(component)), go.name, path, new[] { GetVisualKind(component) }),
                ObjectId: ObjectId(go),
                ObjectPath: path,
                ObjectName: go.name,
                ComponentType: SafeTypeName(ActualType(component)),
                Kind: GetVisualKind(component),
                ActiveSelf: go.activeSelf,
                ActiveInHierarchy: go.activeInHierarchy,
                AssetName: assetName,
                TextureRef: textureRef,
                TextureName: texture != null ? texture.name : null,
                Width: texture != null ? texture.width : null,
                Height: texture != null ? texture.height : null,
                IsReadable: texture != null ? TextureReadable(texture) : null,
                Evidence: evidence);
        }

        private static CampusVisualObjectDto BuildVisualDto(Texture texture)
        {
            return new CampusVisualObjectDto(
                VisualRef: CaptureRef(texture, "campusTexture", SafeTypeName(ActualType(texture)), texture.name, null, new[] { "texture" }),
                ObjectId: string.Empty,
                ObjectPath: string.Empty,
                ObjectName: texture.name,
                ComponentType: SafeTypeName(ActualType(texture)),
                Kind: "texture",
                ActiveSelf: false,
                ActiveInHierarchy: false,
                AssetName: texture.name,
                TextureRef: CaptureRef(texture, "campusTexture", SafeTypeName(ActualType(texture)), texture.name, null, new[] { "texture" }),
                TextureName: texture.name,
                Width: texture.width,
                Height: texture.height,
                IsReadable: TextureReadable(texture),
                Evidence: "direct texture ref");
        }

        private static bool IsVisualComponent(Component component)
        {
            var t = ActualType(component);
            return typeof(RawImage).IsAssignableFrom(t) ||
                   typeof(Image).IsAssignableFrom(t) ||
                   typeof(SpriteRenderer).IsAssignableFrom(t) ||
                   typeof(Renderer).IsAssignableFrom(t) ||
                   IsTypeOrBase(t, "Campus.Common.Octo.TextureResourceView") ||
                   ContainsBaseName(t, "ImageResourceViewBase");
        }

        private static string GetVisualKind(Component component)
        {
            var t = ActualType(component);
            if (typeof(RawImage).IsAssignableFrom(t)) return "rawImage";
            if (typeof(Image).IsAssignableFrom(t)) return "image";
            if (typeof(SpriteRenderer).IsAssignableFrom(t)) return "spriteRenderer";
            if (typeof(Renderer).IsAssignableFrom(t)) return "rendererMaterial";
            if (IsTypeOrBase(t, "Campus.Common.Octo.TextureResourceView")) return "textureResourceView";
            if (ContainsBaseName(t, "ImageResourceViewBase")) return "imageResourceView";
            return "visual";
        }

        private static Texture? TryGetTexture(object? value, out string? evidence, out string? assetName)
        {
            evidence = null;
            assetName = null;
            if (value == null) return null;

            if (TryCastUnity<Texture>(value) is { } direct)
            {
                evidence = "Texture";
                assetName = direct.name;
                return direct;
            }

            if (TryCastUnity<Sprite>(value) is { } sprite)
            {
                evidence = "Sprite.texture";
                assetName = sprite.name;
                return sprite.texture;
            }

            if (TryCastUnity<RawImage>(value) is { } raw)
            {
                evidence = "RawImage.texture";
                assetName = raw.texture != null ? raw.texture.name : null;
                return raw.texture;
            }

            if (TryCastUnity<Image>(value) is { } image)
            {
                evidence = "Image.sprite";
                assetName = image.sprite != null ? image.sprite.name : null;
                return image.sprite != null ? image.sprite.texture : null;
            }

            if (TryCastUnity<SpriteRenderer>(value) is { } sr)
            {
                evidence = "SpriteRenderer.sprite";
                assetName = sr.sprite != null ? sr.sprite.name : null;
                return sr.sprite != null ? sr.sprite.texture : null;
            }

            if (TryCastUnity<Renderer>(value) is { } renderer)
            {
                var material = renderer.sharedMaterial;
                evidence = "Renderer.sharedMaterial";
                assetName = material != null ? material.name : null;
                return material != null ? material.mainTexture : null;
            }

            if (value is Component component)
            {
                assetName = GetStringMember(component, "_debugAssetName") ??
                            GetStringMember(component, "RequestedAssetBundleName") ??
                            ObjectName(GetMemberValue(component, "Asset"));

                var asset = GetMemberValue(component, "Asset");
                if (TryCastUnity<Texture>(asset) is { } assetTexture)
                {
                    evidence = "CampusAssetKeeperBase.Asset";
                    return assetTexture;
                }

                var imageComponent = GetMemberValue(component, "ImageComponent") ?? GetMemberValue(component, "_image");
                var imageTexture = TryGetTexture(imageComponent, out var innerEvidence, out var innerAssetName);
                if (imageTexture != null)
                {
                    evidence = "ImageResourceViewBase." + innerEvidence;
                    assetName ??= innerAssetName;
                    return imageTexture;
                }
            }

            return null;
        }

        private static (Texture? texture, string source) ResolveTexture(string refOrNameOrId)
        {
            if (string.IsNullOrWhiteSpace(refOrNameOrId)) throw new ArgumentException("refOrNameOrId is required");
            if (refOrNameOrId.StartsWith("ref:", StringComparison.Ordinal))
            {
                if (!McpObjectRefs.TryGet(refOrNameOrId, out var value) || value == null)
                    throw new InvalidOperationException("NotFound");
                var texture = TryGetTexture(value, out var evidence, out _);
                return (texture, evidence ?? "ref");
            }

            if (refOrNameOrId.StartsWith("obj:", StringComparison.Ordinal) &&
                int.TryParse(refOrNameOrId.Substring(4), out var iid))
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var texture = TryGetTexture(component, out var evidence, out _);
                    if (texture != null) return (texture, evidence ?? "object component");
                }
            }

            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (texture != null && string.Equals(texture.name, refOrNameOrId, StringComparison.Ordinal))
                    return (texture, "texture exact name");
            }

            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (texture != null && (texture.name ?? string.Empty).IndexOf(refOrNameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return (texture, "texture partial name");
            }

            return (null, "not found");
        }

        private static (byte[] bytes, bool usedReadablePath) EncodeTexturePng(Texture texture)
        {
            if (texture is Texture2D tex2D && TextureReadable(texture) == true)
            {
                var bytes = EncodeTexture2DToPng(tex2D);
                if (bytes != null && bytes.Length > 0)
                    return (bytes, true);
            }

            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Texture2D? copy = null;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply(false, false);
                return (EncodeTexture2DToPng(copy), false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (copy != null) UnityEngine.Object.Destroy(copy);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static bool? TextureReadable(Texture texture)
        {
            var value = GetMemberValue(texture, "isReadable");
            return value is bool b ? b : null;
        }

        private static byte[] EncodeTexture2DToPng(Texture2D texture)
        {
            var imageConversion = FindType("UnityEngine.ImageConversion");
            var encode = imageConversion?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "EncodeToPNG", StringComparison.Ordinal))
                        return false;
                    var ps = m.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(typeof(Texture2D));
                });
            if (encode != null)
                return ToByteArray(encode.Invoke(null, new object?[] { texture }));

            encode = ActualType(texture).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "EncodeToPNG", StringComparison.Ordinal) && m.GetParameters().Length == 0);
            if (encode != null)
                return ToByteArray(encode.Invoke(texture, Array.Empty<object>()));

            throw new InvalidOperationException("Unity PNG encoder not found");
        }

        private static byte[] ToByteArray(object? value)
        {
            if (value is byte[] bytes) return bytes;
            if (value is IEnumerable enumerable)
            {
                var list = new List<byte>();
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    list.Add(Convert.ToByte(item));
                }
                return list.ToArray();
            }
            return Array.Empty<byte>();
        }

        private static CampusAssetDto BuildAssetDto(UnityEngine.Object asset, string kind, string? evidence)
        {
            var go = TryCastUnity<GameObject>(asset);
            var component = TryCastUnity<Component>(asset);
            var path = go != null ? BuildObjectPath(go) : null;
            var refId = CaptureRef(asset, "campusAsset", SafeTypeName(ActualType(asset)), asset.name, path, new[] { kind });
            var objectId = go != null ? ObjectId(go) :
                component != null ? ObjectId(component.gameObject) : null;
            return new CampusAssetDto(refId, asset.name, SafeTypeName(ActualType(asset)), kind, objectId, evidence);
        }

        private static string GetAssetKind(UnityEngine.Object asset)
        {
            if (TryCastUnity<Texture>(asset) != null) return "texture";
            if (TryCastUnity<Sprite>(asset) != null) return "sprite";
            if (TryCastUnity<Material>(asset) != null) return "material";
            if (TryCastUnity<GameObject>(asset) != null) return "gameObject";
            if (TryCastUnity<Component>(asset) != null) return "component";
            return SafeTypeName(ActualType(asset));
        }

        private static IReadOnlyList<string> GetI18nKeys()
        {
            var i18n = GetI18nInstance();
            if (i18n == null) return Array.Empty<string>();

            object? keys = null;
            foreach (var (type, instance) in GetCandidateTypesWithInstance(i18n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var getKeys = FindMethod(type, "GetKeys", isStatic: false, parameterCount: 0);
                    if (getKeys == null) continue;
                    keys = getKeys.Invoke(instance, Array.Empty<object>());
                    break;
                }
                catch (TargetException) { }
                catch (ArgumentException) { }
            }

            var list = EnumerateStrings(keys).ToList();
            if (list.Count > 0) return list;

            var dict = GetMemberValue(i18n, "_i18nKeyValues");
            return EnumerateDictionaryKeys(dict).ToList();
        }

        private static bool I18nContainsKey(string key)
        {
            var i18n = GetI18nInstance();
            if (i18n == null) return false;

            foreach (var (type, instance) in GetCandidateTypesWithInstance(i18n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var contains = FindMethod(type, "ContainsKey", isStatic: false, parameterCount: 1);
                if (contains == null) continue;
                try
                {
                    var result = contains.Invoke(instance, new object?[] { key });
                    if (result is bool b) return b;
                    if (result != null) return Convert.ToBoolean(result);
                }
                catch (TargetException) { }
                catch (ArgumentException) { }
                catch { }
            }

            return GetI18nKeys().Contains(key, StringComparer.Ordinal);
        }

        private static string? GetI18nValue(string key)
        {
            var helper = GetCampusTypes().I18nHelper;
            if (helper != null)
            {
                var get = FindMethod(helper, "Get", isStatic: true, parameterCount: 1);
                if (get != null)
                {
                    try { return get.Invoke(null, new object?[] { key })?.ToString(); }
                    catch { }
                }
            }

            var i18n = GetI18nInstance();
            if (i18n == null) return null;

            foreach (var (type, instance) in GetCandidateTypesWithInstance(i18n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var method = FindMethod(type, "Get", isStatic: false, parameterCount: 1);
                if (method == null) continue;
                try { return method.Invoke(instance, new object?[] { key })?.ToString(); }
                catch (TargetException) { }
                catch (ArgumentException) { }
                catch { return null; }
            }

            return null;
        }

        private static object? GetI18nInstance()
        {
            var helper = GetCampusTypes().I18nHelper;
            if (helper == null) return null;
            var instance = GetStaticMemberValue(helper, "Instance");
            if (instance != null)
            {
                var concrete = GetMemberValue(instance, "_i18n");
                if (concrete != null) return concrete;
            }

            return GetStaticMemberValue(helper, "I18n");
        }

        private static void TrySubscribeAssetLoadEvents()
        {
            if (_assetLoadSubscribed) return;
            _assetLoadSubscribed = true;
            try
            {
                var type = GetCampusTypes().CampusOctoSetupper;
                if (type == null) return;
                var ev = type.GetEvent("onAssetBundleLoadInterceptor", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) return;

                _assetLoadHandler = assetBundleName =>
                {
                    lock (AssetEventsGate)
                    {
                        AssetLoadEvents.Add(new CampusAssetLoadEventDto(DateTime.UtcNow.ToString("O"), assetBundleName));
                        while (AssetLoadEvents.Count > MaxAssetEvents)
                            AssetLoadEvents.RemoveAt(0);
                    }
                };
                ev.AddEventHandler(null, _assetLoadHandler);
            }
            catch (Exception ex)
            {
                try { LogBuffer.Add("warn", "Campus asset load event subscription failed: " + ex.Message, "mcp", "campus"); } catch { }
            }
        }

        private static CampusTypes GetCampusTypes()
        {
            return new CampusTypes
            {
                CampusText = FindType("Campus.Common.CampusText"),
                TmpText = FindType("TMPro.TMP_Text"),
                RubyText = FindType("Qua.UI.TextMeshProUGUIRuby"),
                MessageText = FindType("Campus.ADV.MessageText"),
                I18nHelper = FindType("Qua.UI.I18nHelper") ?? FindType("I18nHelper"),
                TextureResourceView = FindType("Campus.Common.Octo.TextureResourceView"),
                ImageResourceViewBase = FindOpenGenericType("Campus.Common.Octo.ImageResourceViewBase`2"),
                CampusOctoSetupper = FindType("Campus.Common.Octo.CampusOctoSetupper")
            };
        }

        private static Type? FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = null;
                try { type = asm.GetType(fullName, throwOnError: false, ignoreCase: false); }
                catch { }
                if (type != null) return type;
            }

            foreach (var type in EnumerateLoadedTypes())
            {
                if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
                    return type;
            }
            return null;
        }

        private static Type? FindOpenGenericType(string fullName)
        {
            foreach (var type in EnumerateLoadedTypes())
            {
                if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
                    return type;
            }
            return null;
        }

        private static IEnumerable<Type> EnumerateLoadedTypes()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
                catch { continue; }
                foreach (var type in types)
                    yield return type;
            }
        }

        private static IReadOnlyList<CampusAssemblyDto> GetLoadedCampusAssemblies()
        {
            var list = new List<CampusAssemblyDto>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name ?? string.Empty;
                if (name.IndexOf("Campus", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("campus", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Qua", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string? location = null;
                try { location = string.IsNullOrEmpty(asm.Location) ? null : asm.Location; } catch { }
                list.Add(new CampusAssemblyDto(name, asm.GetName().Version?.ToString(), location));
            }
            return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static CampusInteropPathHealthDto GetInteropPathHealth()
        {
            var root = GetBepInExRoot();
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(Path.Combine(root!, "interop"));
                candidates.Add(Path.Combine(root!, "Il2CppInterop"));
                candidates.Add(Path.Combine(root!, "unity-libs"));
                candidates.Add(Path.Combine(root!, "DumpedAssemblies"));
            }

            var existing = candidates.Where(Directory.Exists).ToArray();
            var likelyReady = existing.Any(path =>
            {
                try { return Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).Take(2).Any(); }
                catch { return false; }
            });

            return new CampusInteropPathHealthDto(
                BepInExRootFound: !string.IsNullOrWhiteSpace(root),
                BepInExRoot: root,
                CandidatePaths: candidates,
                ExistingPaths: existing,
                RuntimeDumpInteropLikelyReady: likelyReady);
        }

        private static string? GetBepInExRoot()
        {
            var pathsType = FindType("BepInEx.Paths");
            if (pathsType == null) return null;
            return GetStaticMemberValue(pathsType, "BepInExRootPath")?.ToString() ??
                   GetStaticMemberValue(pathsType, "GameRootPath")?.ToString();
        }

        private static string GetExportRoot()
        {
            var cfg = McpConfig.Load();
            var root = string.IsNullOrWhiteSpace(cfg.ExportRoot)
                ? Path.Combine(ExplorerCore.ExplorerFolder, "CampusLocalization")
                : cfg.ExportRoot!;
            return Path.GetFullPath(root);
        }

        private static object? GetStaticMemberValue(Type type, string name)
        {
            return GetMemberValue(type, null, name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static object? GetMemberValue(object instance, string name)
        {
            foreach (var (type, declaringInstance) in GetCandidateTypesWithInstance(instance, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = GetMemberValue(type, declaringInstance, name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (value != null)
                    return value;
            }

            return null;
        }

        private static object? GetMemberValue(Type type, object? instance, string name, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var declaringInstance = GetDeclaringInstance(instance, current, flags);
                try
                {
                    var prop = current.GetProperty(name, flags);
                    if (prop != null && prop.GetIndexParameters().Length == 0 && prop.GetGetMethod(true) != null)
                        return prop.GetValue(declaringInstance);
                }
                catch { }

                try
                {
                    var field = current.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(declaringInstance);
                }
                catch { }
            }
            return null;
        }

        private static bool SetTextMember(object instance, string name, object? value)
        {
            foreach (var type in GetCandidateTypes(instance))
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    var declaringInstance = GetDeclaringInstance(instance, current, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    try
                    {
                        var prop = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null)
                        {
                            prop.SetValue(declaringInstance, CoerceValue(value, prop.PropertyType));
                            return true;
                        }
                    }
                    catch { }

                    try
                    {
                        var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            field.SetValue(declaringInstance, CoerceValue(value, field.FieldType));
                            return true;
                        }
                    }
                    catch { }
                }
            }
            return false;
        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value == null) return null;
            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType)) return value;
            if (targetType.IsEnum)
            {
                if (value is string s) return Enum.Parse(targetType, s);
                return Enum.ToObject(targetType, value);
            }
            try { return Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType); }
            catch { return value; }
        }

        private static MethodInfo? FindMethod(Type type, string name, bool isStatic, int parameterCount)
        {
            var flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetMethods(flags)
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal) && m.GetParameters().Length == parameterCount);
        }

        private static bool InvokeStringMethod(object instance, string name, string value)
        {
            foreach (var type in GetCandidateTypes(instance))
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    var declaringInstance = GetDeclaringInstance(instance, current, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    try
                    {
                        var method = current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m =>
                                string.Equals(m.Name, name, StringComparison.Ordinal) &&
                                m.GetParameters().Length == 1 &&
                                m.GetParameters()[0].ParameterType == typeof(string));
                        if (method == null) continue;
                        method.Invoke(declaringInstance, new object?[] { value });
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        private static void InvokeNoThrow(object instance, string name, params object?[] args)
        {
            foreach (var type in GetCandidateTypes(instance))
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    var declaringInstance = GetDeclaringInstance(instance, current, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    try
                    {
                        var method = current
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal) && m.GetParameters().Length == args.Length);
                        if (method == null) continue;
                        method.Invoke(declaringInstance, args);
                        return;
                    }
                    catch { }
                }
            }
        }

        private static object? GetDeclaringInstance(object? instance, Type declaringType, BindingFlags flags)
        {
            if (instance == null || flags.HasFlag(BindingFlags.Static))
                return null;

            try { return instance.TryCast(declaringType); }
            catch { return instance; }
        }

        private static string? GetStringMember(object instance, string name)
            => GetMemberValue(instance, name)?.ToString();

        private static bool? GetBoolMember(object instance, string name)
        {
            var value = GetMemberValue(instance, name);
            return value is bool b ? b : null;
        }

        private static float? GetFloatMember(object instance, string name)
        {
            var value = GetMemberValue(instance, name);
            if (value == null) return null;
            try { return Convert.ToSingle(value); }
            catch { return null; }
        }

        private static CampusVector4Dto? Vector4Dto(object? value)
        {
            if (value is Vector4 v) return new CampusVector4Dto(v.x, v.y, v.z, v.w);
            return null;
        }

        private static string? MemberToString(object? value)
            => value == null ? null : value.ToString();

        private static string? ObjectName(object? value)
        {
            if (value is UnityEngine.Object unityObject)
                return unityObject.name;
            return value?.ToString();
        }

        private static string? GetLocalizeKey(CampusTextTarget target)
        {
            var direct = GetStringMember(target.Source, "LocalizeKey") ??
                         GetStringMember(target.Source, "_localizeKey") ??
                         GetStringMember(target.RenderOwner, "LocalizeKey") ??
                         GetStringMember(target.RenderOwner, "_localizeKey");
            if (!string.IsNullOrEmpty(direct))
                return direct;

            try
            {
                foreach (var component in target.Source.gameObject.GetComponents<Component>())
                {
                    if (component == null || ReferenceEquals(component, target.Source)) continue;
                    direct = GetStringMember(component, "LocalizeKey") ?? GetStringMember(component, "_localizeKey");
                    if (!string.IsNullOrEmpty(direct))
                        return direct;
                }
            }
            catch { }

            return null;
        }

        private static Component? FindComponentByTypeName(GameObject go, params string[] typeNames)
        {
            if (go == null) return null;
            Component[] components;
            try { components = go.GetComponents<Component>(); }
            catch { return null; }

            foreach (var component in components)
            {
                if (component == null) continue;
                var type = ActualType(component);
                foreach (var typeName in typeNames)
                {
                    if (IsTypeOrBase(type, typeName))
                        return component;
                }
            }

            return null;
        }

        private static bool IsTypeOrBase(Type type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool ContainsBaseName(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var fullName = current.FullName ?? current.Name;
                if (fullName.IndexOf(name, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        private static Type ActualType(object value)
        {
            try { return value.GetActualType() ?? value.GetType(); }
            catch { return value.GetType(); }
        }

        private static IEnumerable<Type> GetCandidateTypes(object value)
        {
            var runtimeType = value.GetType();
            yield return runtimeType;

            Type? actualType = null;
            try { actualType = value.GetActualType(); }
            catch { }

            if (actualType != null && !ReferenceEquals(actualType, runtimeType))
                yield return actualType;
        }

        private static IEnumerable<(Type type, object? instance)> GetCandidateTypesWithInstance(object instance, BindingFlags flags)
        {
            foreach (var type in GetCandidateTypes(instance))
                yield return (type, GetDeclaringInstance(instance, type, flags));
        }

        private static T? TryCastUnity<T>(object? value) where T : UnityEngine.Object
        {
            if (value is T typed) return typed;
            if (value is not UnityEngine.Object unityObject) return null;
            try { return unityObject.TryCast<T>(); }
            catch { return null; }
        }

        private static string SafeTypeName(Type? type)
            => type == null ? "<unknown>" : string.IsNullOrEmpty(type.FullName) ? type.Name : type.FullName!;

        private static string ObjectId(GameObject go) => "obj:" + go.GetInstanceID();

        private static string BuildObjectPath(Component component)
            => component != null && component.transform != null ? UnityQuery.BuildPath(component.transform) : string.Empty;

        private static string BuildObjectPath(GameObject go)
            => go != null && go.transform != null ? UnityQuery.BuildPath(go.transform) : string.Empty;

        private static string BuildTextStableKey(CampusTextTarget target)
            => "campusText:" + target.Kind + ":" + target.Source.GetInstanceID();

        private static string CaptureRef(object value, string kind, string runtimeType, string? name, string? path, IReadOnlyList<string>? tags)
        {
            return McpObjectRefs.Capture(value, new McpObjectRefMetadata(kind, runtimeType, name, path, DateTime.UtcNow, tags));
        }

        private static string CaptureStableRef(object value, string stableKey, string kind, string runtimeType, string? name, string? path, IReadOnlyList<string>? tags)
        {
            return McpObjectRefs.CaptureStable(stableKey, value, new McpObjectRefMetadata(kind, runtimeType, name, path, DateTime.UtcNow, tags));
        }

        private static string MakePreview(string text, int maxChars)
        {
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars);
        }

        private static int ClampLimit(int? limit)
        {
            if (limit == int.MaxValue) return int.MaxValue;
            return Math.Clamp(limit ?? 100, 1, MaxPageLimit);
        }

        private static string NormalizeLayoutMode(string? layoutMode)
            => string.IsNullOrWhiteSpace(layoutMode) ? "preserve" : layoutMode!;

        private static string NormalizeRotation(string? rotation)
        {
            if (string.Equals(rotation, "-90", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rotation, "minus90", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rotation, "ccw", StringComparison.OrdinalIgnoreCase))
                return "-90";
            return "+90";
        }

        private static string MakeSafeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            var safe = new string(chars);
            return string.IsNullOrWhiteSpace(safe) ? "asset" : safe;
        }

        private static bool IsCjk(char ch)
        {
            return (ch >= '\u3040' && ch <= '\u30ff') ||
                   (ch >= '\u3400' && ch <= '\u9fff') ||
                   (ch >= '\uf900' && ch <= '\ufaff');
        }

        private static bool IsFullWidth(char ch)
        {
            return (ch >= '\u1100' && ch <= '\u115f') ||
                   (ch >= '\u2e80' && ch <= '\ua4cf') ||
                   (ch >= '\uac00' && ch <= '\ud7a3') ||
                   (ch >= '\uf900' && ch <= '\ufaff') ||
                   (ch >= '\ufe10' && ch <= '\ufe19') ||
                   (ch >= '\ufe30' && ch <= '\ufe6f') ||
                   (ch >= '\uff01' && ch <= '\uff60') ||
                   (ch >= '\uffe0' && ch <= '\uffe6');
        }

        private static IEnumerable<string> EnumerateStrings(object? value)
        {
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                        yield return item.ToString() ?? string.Empty;
                }
                yield break;
            }

            foreach (var item in EnumerateReflected(value))
            {
                if (item != null)
                    yield return item.ToString() ?? string.Empty;
            }
        }

        private static IEnumerable<string> EnumerateDictionaryKeys(object? value)
        {
            if (value is IDictionary dictionary)
            {
                foreach (var key in dictionary.Keys)
                {
                    if (key != null)
                        yield return key.ToString() ?? string.Empty;
                }
                yield break;
            }

            if (value == null) yield break;

            var keys = GetMemberValue(value, "Keys");
            foreach (var key in EnumerateStrings(keys))
                yield return key;

            foreach (var entry in EnumerateReflected(value))
            {
                if (entry == null) continue;
                var key = GetMemberValue(entry, "Key");
                if (key != null)
                    yield return key.ToString() ?? string.Empty;
            }
        }

        private static IEnumerable<object?> EnumerateReflected(object? value)
        {
            if (value == null || value is string) yield break;

            object? enumerator = null;
            foreach (var (type, instance) in GetCandidateTypesWithInstance(value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var getEnumerator = FindMethod(type, "GetEnumerator", isStatic: false, parameterCount: 0);
                    if (getEnumerator == null) continue;
                    enumerator = getEnumerator.Invoke(instance, Array.Empty<object>());
                    break;
                }
                catch (TargetException) { }
                catch (ArgumentException) { }
                catch { }
            }

            if (enumerator == null) yield break;

            for (var i = 0; i < 200000; i++)
            {
                bool hasNext;
                try
                {
                    var moveNext = FindMethod(ActualType(enumerator), "MoveNext", isStatic: false, parameterCount: 0) ??
                                   FindMethod(enumerator.GetType(), "MoveNext", isStatic: false, parameterCount: 0);
                    if (moveNext == null) yield break;
                    var result = moveNext.Invoke(GetDeclaringInstance(enumerator, moveNext.DeclaringType ?? ActualType(enumerator), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Array.Empty<object>());
                    hasNext = result is bool b ? b : result != null && Convert.ToBoolean(result);
                }
                catch
                {
                    yield break;
                }

                if (!hasNext) yield break;
                yield return GetMemberValue(enumerator, "Current");
            }
        }
    }
}
#endif
