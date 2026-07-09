# Campus Tools Debugging Guide

Quick reference for debugging Campus localization through the UnityExplorer MCP
server in a running IL2CPP game. The full contract remains
`docs/mcp-interface-specs.md`; this guide focuses on what each Campus tool
expects and what useful output looks like.

## Connection Checks

The current in-game MCP host is normally:

```text
http://127.0.0.1:51477/mcp
```

Start with read-only checks:

```bash
npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/list
npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/call --tool-name GetCampusStatus
```

Expected result:

- `tools/list` includes `GetCampusStatus`, text, i18n, visual, asset, and
  guarded Campus write tools.
- `GetCampusStatus` returns `Ready: true`, `Runtime: "IL2CPP"`, required
  Campus/TMP types as `true`, and `FeatureReadiness` similar to
  `ready: v1 text/i18n workflow; asset/resource discovery deferred`.

If an agent call hangs or the tool list looks stale, use the inspector CLI above
as the baseline. It talks directly to the running HTTP server.

## Safety Gates

Read tools do not require writes. Mutating tools are guarded by MCP config:

- `allowWrites=false` blocks writes.
- `requireConfirm=true` means guarded calls must include `confirm=true`.
- `enableConsoleEval=false` affects console/script execution, not normal Campus
  text and i18n reads.

Check the live state with `GetConfig`. Safe defaults look like:

```json
{
  "ok": true,
  "enabled": true,
  "port": 51477,
  "allowWrites": false,
  "requireConfirm": true,
  "enableConsoleEval": false
}
```

## Status

### `GetCampusStatus()`

Inputs: none.

Use it to confirm that the Campus interop assemblies and reflection targets are
loaded.

Expected output shape:

```json
{
  "UnityVersion": "6000.0.77f1",
  "Runtime": "IL2CPP",
  "Platform": "WindowsPlayer",
  "ExplorerVersion": "4.13.6",
  "LoadedCampusAssemblies": [],
  "InteropPathHealth": {
    "BepInExRootFound": true,
    "RuntimeDumpInteropLikelyReady": true
  },
  "Types": {
    "CampusText": true,
    "TmpText": true,
    "TextMeshProUGUIRuby": true,
    "MessageText": true,
    "I18nHelper": true
  },
  "Ready": true,
  "MissingRequiredTypes": [],
  "FeatureReadiness": "ready: v1 text/i18n workflow; asset/resource discovery deferred"
}
```

Debug meaning:

- `Ready=false`: do not trust text/i18n scans yet.
- `MissingRequiredTypes`: the failed reflection dependency.
- `InteropPathHealth.RuntimeDumpInteropLikelyReady=false`: likely BepInEx
  interop dump/path issue.

## Text Objects

### `ListCampusTextObjects(query?, source?, activeState?, kind?, limit?, offset?)`

Inputs:

- `query`: optional substring filter. Prefer filtering before raising `limit`.
- `source`: `scenes`, `resources`, or `all`; default is `scenes`.
- `activeState`: `active`, `inactive`, or `all`; default is `active`.
- `kind`: optional text kind filter, such as `campusText`, `tmpText`,
  `rubyText`, `messageText`, or `uiText`.
- `limit`/`offset`: paging.

Expected output:

```json
{
  "Total": 30,
  "Items": [
    {
      "TextRef": "ref:57039ceb:1",
      "ObjectId": "obj:-39400",
      "ObjectPath": "/WindowSystemRoot/.../Name",
      "ObjectName": "Name",
      "ComponentType": "Campus.Common.CampusText",
      "Kind": "campusText",
      "ActiveSelf": true,
      "ActiveInHierarchy": true,
      "LocalizeKey": "common.dearness",
      "Text": "\u89aa\u611b\u5ea6",
      "Truncated": false,
      "Preview": "\u89aa\u611b\u5ea6",
      "Flags": {
        "Utf16Length": 3,
        "HasCjk": true,
        "HasFullWidth": true,
        "HasMarkup": false
      },
      "Style": {
        "Font": "CampusNormalJp",
        "Alignment": "Center",
        "FontSize": 30,
        "IsTextOverflowing": false
      },
      "RectTransform": {
        "Width": 250,
        "Height": 36,
        "LocalEulerZ": 0
      },
      "Ruby": {
        "IsRubyComponent": false
      },
      "GlyphEvidence": {
        "Checked": false,
        "MissingCharacters": []
      }
    }
  ]
}
```

Debug meaning:

- `TextRef`: pass this to text read/analyze/write calls.
- `ObjectId`: pass this to normal UnityExplorer object tools.
- `LocalizeKey`: link back to i18n when available.
- `Truncated=true`: call `ReadCampusTextObject` with larger `maxChars`.
- `Style.IsTextOverflowing`, rendered dimensions, and `RectTransform` dimensions
  are the first layout-failure signals.

### `ReadCampusTextObject(textRef, maxChars?)`

Inputs:

- `textRef`: from `ListCampusTextObjects`.
- `maxChars`: optional cap, default `65536`.

Expected output: one `CampusTextObjectDto` with the same fields as list items,
but refreshed for a single live component.

Use this after a list result when text may be long or when layout changed after a
write.

### `AnalyzeCampusTextObject(textRef, candidateText?, layoutMode?)`

Inputs:

- `textRef`: from a text list/read call.
- `candidateText`: optional replacement text to simulate.
- `layoutMode`: `preserve` or `horizontalInVertical`; default is `preserve`.

Expected output shape:

```json
{
  "Mode": "preserve",
  "Supported": true,
  "Reason": null,
  "Before": {
    "PreferredWidth": 90.01,
    "PreferredHeight": 30.01,
    "RenderedWidth": 90,
    "RenderedHeight": 30,
    "RectWidth": 250,
    "RectHeight": 36,
    "Overflow": false,
    "OverflowReason": null
  },
  "Candidates": [],
  "RecommendedRotation": null,
  "OverflowRemains": false,
  "WouldMutate": false
}
```

The useful signal is whether the current or candidate text overflows, whether
the requested mode is supported, which rotation is recommended, and whether
`horizontalInVertical` can solve a tall Japanese slot.

Use this before `SetCampusTextObject` when replacing Japanese with longer
English.

## Text Writes

### `SetCampusTextObject(textRef, text, confirm?, layoutMode?)`

Inputs:

- `textRef`: from `ListCampusTextObjects`.
- `text`: exact string to assign. The tool does not trim, normalize, convert to
  ASCII, or strip TMP/ruby markup.
- `confirm`: required when `requireConfirm=true`.
- `layoutMode`: `preserve` or `horizontalInVertical`.

Expected success:

```json
{
  "ok": true,
  "textRef": "ref:57039ceb:1",
  "layoutMode": "preserve",
  "textPreview": "Dearness",
  "layout": {}
}
```

Expected blocked write:

```json
{
  "ok": false,
  "error": {
    "kind": "PermissionDenied",
    "message": "Writes are disabled"
  }
}
```

### `SetCampusTextLayout(textRef, mode, rotation?, confirm?)`

Inputs:

- `mode`: `preserve` or `horizontalInVertical`.
- `rotation`: `auto`, `+90`, or `-90`; default is `auto`.
- `confirm`: required when confirmation is enabled.

Expected success:

```json
{
  "ok": true,
  "textRef": "ref:57039ceb:1",
  "mode": "horizontalInVertical",
  "rotation": "+90",
  "analysis": {}
}
```

Unsupported layout returns `ok:false` with `error.kind:"Unsupported"` and
analysis evidence.

### `ResetCampusTextLayout(textRef, confirm?)`

Inputs:

- `textRef`: any fresh stable ref for the same live text component.
- `confirm`: required when confirmation is enabled.

Expected success:

```json
{ "ok": true, "textRef": "ref:57039ceb:1" }
```

Use it to restore the captured RectTransform/TMP layout state after
`horizontalInVertical` experiments.

## I18n

### `ListCampusI18nKeys(query?, valueQuery?, limit?, offset?)`

Inputs:

- `query`: key substring filter.
- `valueQuery`: value substring filter.
- `limit`/`offset`: paging. The live game can expose thousands of keys, so keep
  limits small and filter whenever possible.

Expected output:

```json
{
  "Total": 6812,
  "Items": [
    {
      "Key": "common.yes",
      "Value": "\u306f\u3044",
      "Utf16Length": 2,
      "HasCjk": true,
      "HasFullWidth": true
    }
  ]
}
```

### `ReadCampusI18nKey(key)`

Inputs:

- `key`: exact i18n key, often from `LocalizeKey` on a text object.

Expected output:

```json
{
  "Key": "common.yes",
  "Value": "\u306f\u3044",
  "Utf16Length": 2,
  "HasCjk": true,
  "HasFullWidth": true
}
```

### `SetCampusI18nValue(key, value, confirm?)`

Inputs:

- `key`: exact i18n key.
- `value`: exact runtime override string.
- `confirm`: required when confirmation is enabled.

Expected success:

```json
{ "ok": true, "key": "common.yes", "utf16Length": 3 }
```

This is a live runtime override through `I18nHelper.SetValue`; it is not a
source-file patch.

## Visuals And Assets

### `ListCampusVisualObjects(query?, source?, activeState?, kind?, limit?, offset?)`

Inputs:

- `query`: optional object/asset substring filter.
- `source`: `scenes`, `resources`, or `all`; default is `scenes`.
- `activeState`: `active`, `inactive`, or `all`; default is `active`.
- `kind`: optional kind such as `image`, `rawImage`, `spriteRenderer`,
  `renderer`, or resource-view related kinds.

Expected output:

```json
{
  "Total": 627,
  "Items": [
    {
      "VisualRef": "ref:57039ceb:7",
      "ObjectId": "obj:-39082",
      "ObjectPath": "/WindowSystemRoot/.../3dTargetImage",
      "ObjectName": "3dTargetImage",
      "ComponentType": "UnityEngine.UI.RawImage",
      "Kind": "rawImage",
      "ActiveSelf": true,
      "ActiveInHierarchy": true,
      "AssetName": "_VLTargetTexture_2160x3840",
      "TextureRef": "ref:57039ceb:6",
      "TextureName": "_VLTargetTexture_2160x3840",
      "Width": 2160,
      "Height": 3840,
      "IsReadable": true,
      "Evidence": "RawImage.texture"
    }
  ]
}
```

### `ReadCampusVisualObject(visualRef)`

Inputs:

- `visualRef`: from `ListCampusVisualObjects`.

Expected output: one `CampusVisualObjectDto`.

Debug meaning:

- `Evidence` tells which reflection path found the visual.
- `TextureRef` is the preferred handle for texture export.
- `AssetName`, width/height, and readability may be null when only partial
  sprite/component evidence is available.

### `SearchCampusAssets(query?, kind?, limit?, offset?)`

Inputs:

- `query`: optional loaded asset name/object filter.
- `kind`: optional asset kind filter.
- `limit`/`offset`: paging.

Expected output:

```json
{
  "Total": 64199,
  "Items": [
    {
      "AssetRef": "ref:57039ceb:1228",
      "Name": "OutGame3DManager",
      "RuntimeType": "Campus.Common.Octo.MotionAssetKeeper",
      "Kind": "component",
      "ObjectId": "obj:2353786",
      "Evidence": null
    }
  ]
}
```

Debug meaning:

- This is best-effort loaded-object evidence, not a complete asset catalog.
- Broad unfiltered searches can be slow on large live scenes. Prefer `query`,
  `kind`, and small `limit` values.

### `ResolveCampusAsset(refOrNameOrId)`

Inputs:

- `refOrNameOrId`: an `AssetRef`, Unity `obj:<id>`, exact name, or partial name.

Expected output: one `CampusAssetDto` when resolution is unambiguous; otherwise a
normal `NotFound` or `InvalidArgument` error.

### `ListCampusAssetLoadEvents(limit?, offset?)`

Inputs:

- `limit`/`offset`: paging.

Expected output:

```json
{ "Total": 0, "Items": [] }
```

Zero events is valid if no asset bundle loads have been observed since the safe
subscription was installed. Trigger game navigation that loads bundles, then
call again.

### `ExportCampusTexture(refOrNameOrId, confirm?)`

Inputs:

- `refOrNameOrId`: `TextureRef`, `VisualRef`, `AssetRef`, Unity object id, or
  texture name.
- `confirm`: required when confirmation is enabled.

Expected success:

```json
{
  "ok": true,
  "export": {
    "Path": "C:/.../CampusLocalization/texture.png",
    "Width": 2160,
    "Height": 3840
  }
}
```

Readable textures export directly; non-readable textures use a RenderTexture
readback path.

## Snapshot Export

### `ExportCampusLocalizationSnapshot(scope?, source?, confirm?)`

Inputs:

- `scope`: `loadedText`, `i18n`, or `both`.
- `source`: `scenes`, `resources`, or `all`.
- `confirm`: required when confirmation is enabled.

Expected success:

```json
{
  "ok": true,
  "snapshot": {}
}
```

Exports are written under `exportRoot` when configured, otherwise under
`ExplorerFolder/CampusLocalization`.

## Common Sequences

### Find the live text behind a visible UI label

1. `ListCampusTextObjects(query="<visible text>", source="scenes", activeState="active", limit=10)`.
2. Pick the result whose `ObjectPath` matches the visible screen.
3. `ReadCampusTextObject(textRef)` for exact style and rect evidence.
4. `AnalyzeCampusTextObject(textRef, candidateText="Dearness", layoutMode="preserve")`.
5. If it overflows, retry with `layoutMode="horizontalInVertical"`.

### Trace text to an i18n key

1. Read/list the text object.
2. If `LocalizeKey` is present, call `ReadCampusI18nKey(LocalizeKey)`.
3. If `LocalizeKey` is missing, search by value:
   `ListCampusI18nKeys(valueQuery="<current text>", limit=20)`.

### Debug a missing or wrong image

1. `ListCampusVisualObjects(query="<object or asset name>", source="scenes", activeState="all", limit=20)`.
2. `ReadCampusVisualObject(visualRef)` for the exact evidence.
3. If a texture is present, export it with `ExportCampusTexture(textureRef, confirm=true)` after write gates are intentionally enabled.
4. If no live object is found, try `SearchCampusAssets(query="<asset name>", limit=20)`.

### Check whether the Campus bridge is broken

1. `GetCampusStatus`.
2. Check `Ready`, `MissingRequiredTypes`, and `InteropPathHealth`.
3. `TailLogs(count=100)` and look for `[MCP] error ...` lines.
4. Validate outside the agent with inspector:

   ```bash
   npx @modelcontextprotocol/inspector --cli http://127.0.0.1:51477/mcp --method tools/call --tool-name GetCampusStatus
   ```

## Error Expectations

Normal failures use either JSON-RPC errors or tool-level errors with this shape:

```json
{
  "ok": false,
  "error": {
    "kind": "InvalidArgument",
    "message": "Bad textRef",
    "hint": "Use a TextRef returned by ListCampusTextObjects"
  }
}
```

Common `kind` values:

- `PermissionDenied`: write gate, confirmation, or allowlist blocked the call.
- `NotFound`: ref/key/object no longer exists in the live game.
- `InvalidArgument`: bad enum, bad ref format, ambiguous asset name, or wrong
  tool input.
- `NotReady`: MCP or Campus reflection surface is not initialized yet.
- `RateLimited`: more than 32 in-flight requests; slow down and reduce parallel
  scans.
