using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record CampusAssemblyDto(string Name, string? Version, string? Location);

    public record CampusInteropPathHealthDto(
        bool BepInExRootFound,
        string? BepInExRoot,
        IReadOnlyList<string> CandidatePaths,
        IReadOnlyList<string> ExistingPaths,
        bool RuntimeDumpInteropLikelyReady);

    public record CampusTypeAvailabilityDto(
        bool CampusText,
        bool TmpText,
        bool TextMeshProUGUIRuby,
        bool MessageText,
        bool I18nHelper,
        bool TextureResourceView,
        bool ImageResourceViewBase,
        bool CampusOctoSetupper);

    public record CampusStatusDto(
        string UnityVersion,
        string Runtime,
        string Platform,
        string ExplorerVersion,
        IReadOnlyList<CampusAssemblyDto> LoadedCampusAssemblies,
        CampusInteropPathHealthDto InteropPathHealth,
        CampusTypeAvailabilityDto Types,
        bool Ready,
        IReadOnlyList<string> MissingRequiredTypes,
        string FeatureReadiness);

    public record CampusTextFlagsDto(
        int Utf16Length,
        bool HasCjk,
        bool HasFullWidth,
        bool HasMarkup);

    public record CampusVector2Dto(float X, float Y);

    public record CampusVector4Dto(float X, float Y, float Z, float W);

    public record CampusRectTransformDto(
        float Width,
        float Height,
        CampusVector2Dto SizeDelta,
        CampusVector2Dto AnchoredPosition,
        CampusVector2Dto Pivot,
        CampusVector2Dto AnchorMin,
        CampusVector2Dto AnchorMax,
        float LocalEulerZ);

    public record CampusTextStyleDto(
        string? Font,
        string? FontId,
        string? Material,
        string? Alignment,
        bool? EnableWordWrapping,
        string? OverflowMode,
        float? FontSize,
        bool? EnableAutoSizing,
        float? FontSizeMin,
        float? FontSizeMax,
        CampusVector4Dto? Margin,
        float? PreferredWidth,
        float? PreferredHeight,
        float? RenderedWidth,
        float? RenderedHeight,
        bool? IsTextOverflowing,
        string? StyleType);

    public record CampusRubyDto(
        bool IsRubyComponent,
        bool? HasRuby,
        string? RubyLineHeight,
        float? RubyMargin,
        bool? AdjustXPosition);

    public record CampusGlyphEvidenceDto(
        bool Checked,
        string? Font,
        IReadOnlyList<string> MissingCharacters,
        IReadOnlyList<int> MissingCodePoints,
        string? Error);

    public record CampusTextObjectDto(
        string TextRef,
        string ObjectId,
        string ObjectPath,
        string ObjectName,
        string ComponentType,
        string Kind,
        bool ActiveSelf,
        bool ActiveInHierarchy,
        string? LocalizeKey,
        string Text,
        bool Truncated,
        string Preview,
        CampusTextFlagsDto Flags,
        CampusTextStyleDto Style,
        CampusRectTransformDto? RectTransform,
        CampusRubyDto Ruby,
        CampusGlyphEvidenceDto GlyphEvidence);

    public record CampusLayoutMetricsDto(
        float? PreferredWidth,
        float? PreferredHeight,
        float? RenderedWidth,
        float? RenderedHeight,
        float RectWidth,
        float RectHeight,
        bool Overflow,
        string? OverflowReason);

    public record CampusLayoutCandidateDto(
        string Rotation,
        CampusLayoutMetricsDto Metrics,
        float Score);

    public record CampusTextLayoutAnalysisDto(
        string Mode,
        bool Supported,
        string? Reason,
        CampusLayoutMetricsDto Before,
        IReadOnlyList<CampusLayoutCandidateDto> Candidates,
        string? RecommendedRotation,
        bool OverflowRemains,
        bool WouldMutate);

    public record CampusI18nEntryDto(
        string Key,
        string? Value,
        int Utf16Length,
        bool HasCjk,
        bool HasFullWidth);

    public record CampusI18nSnapshotDto(
        string Scope,
        string Source,
        string Path,
        int LoadedTextCount,
        int I18nCount,
        string CreatedUtc);

    public record CampusVisualObjectDto(
        string VisualRef,
        string ObjectId,
        string ObjectPath,
        string ObjectName,
        string ComponentType,
        string Kind,
        bool ActiveSelf,
        bool ActiveInHierarchy,
        string? AssetName,
        string? TextureRef,
        string? TextureName,
        int? Width,
        int? Height,
        bool? IsReadable,
        string? Evidence);

    public record CampusTextureExportDto(
        bool Ok,
        string Path,
        string TextureName,
        int Width,
        int Height,
        bool UsedReadablePath,
        string Source);

    public record CampusAssetDto(
        string AssetRef,
        string Name,
        string RuntimeType,
        string Kind,
        string? ObjectId,
        string? Evidence);

    public record CampusAssetLoadEventDto(
        string T,
        string AssetBundleName);
#endif
}
