# UnityExplorer Runtime Capabilities (Non-MCP)

This document summarizes the runtime functionality provided by UnityExplorer itself for interacting with a Unity game it is injected into. It focuses on the core in-game UI and scripting features and deliberately excludes the MCP integration and HTTP tooling - these are covered in separate MCP specific documentation.

The implementation in this repo is based on the `yukieiji/UnityExplorer` fork, which is a maintained fork of `sinai-dev/UnityExplorer`. Feature descriptions below reflect that fork where it extends the original behavior (for example, additional C# console script management).

---

## 1. High-Level Overview

At a high level, UnityExplorer provides:

- An in-game overlay UI that can be opened and closed via a configurable hotkey while the game is running.
- A top navbar with panel tabs (Object Explorer, Inspector, C# Console, Hooks, Freecam, Clipboard, Log, Options) and a time-scale widget.
- Panels for exploring scenes and objects, searching for Unity objects and singletons, and loading scenes.
- Inspectors for reading and modifying fields, properties, and components on GameObjects and other objects, including helpers like "View in dnSpy".
- A C# REPL console with line numbers, syntax highlighting, auto-complete options, and support for loading, editing, and compiling scripts from disk.
- A hook manager UI for creating and editing Harmony-style hooks on methods.
- Mouse-based object inspection for world and UI elements, including a UI results panel when multiple objects are hit.
- A free camera (Freecam) that can be controlled independently of the game’s own camera controls.
- A log panel that displays UnityExplorer and (optionally) UnityEngine logs, with optional disk logging.
- Clipboard utilities for copying/pasting values between inspectors and the C# console.
- Time-scale controls and keybinds for pausing, resuming, and speeding up/down the game.
- Configuration options and settings that control startup behavior, input handling, display, hotkeys, and other runtime behavior.
- A small public Inspector API for programmatically opening inspectors from other mods or scripts.

---

## 2. Object & Scene Exploration

UnityExplorer exposes the game’s runtime object graph via the Object Explorer, which is composed primarily of the **Scene Explorer** and **Object Search** tabs.

### 2.1 Scene Explorer

- Traverses all **active scenes** in the game.
- Also exposes special “pseudo-scenes”:
  - `DontDestroyOnLoad` objects.
  - `HideAndDontSave` objects, as well as Assets and Resources that are not part of any scene but behave in a similar way.
- Shows a hierarchical tree of GameObjects and (by implication) their components.
- Integrates with a **Scene Loader** sub-feature:
  - Lists scenes included in the build.
  - Allows loading scenes directly from UnityExplorer.
  - The upstream README notes that the Scene Loader may not work reliably for some older Unity 5.X games.

### 2.2 Object Search

The **Object Search** tab supports several search modes:

- **UnityObject search**
  - Searches for any object deriving from `UnityEngine.Object` (e.g., GameObjects, Components, ScriptableObjects, Textures, AudioClips).
  - Supports optional filters (e.g., by type or name) to narrow down search results.
- **Singleton search**
  - Scans for C# classes that look like singletons, typically with a static `Instance` field.
  - Checks each candidate’s `Instance` field for a current value (to find active singletons).
  - On IL2CPP games, some property accessors might be invoked when scanning for singletons because the system cannot always distinguish between “real” fields and field-like properties.
- **Static class search**
  - Allows locating static classes that may hold global state or utility methods relevant to debugging.

Search results can be used to open inspectors or otherwise inspect the selected objects.

---

## 3. Inspectors & Value Editing

UnityExplorer includes a flexible Inspector system for examining and modifying objects and types at runtime.

### 3.1 Inspector Types

There are three primary inspector modes, exposed as different tab types:

- **GameObject Inspector** (`[G]` tab prefix)
  - Targets a specific `GameObject` instance.
  - Shows and allows manipulation of:
    - The GameObject’s Transform (position, rotation, scale).
    - Attached Components and their fields/properties.
  - Supports editing any writable fields via input fields in the UI.
  - Pressing **Enter** applies changes; pressing **Escape** cancels edits.
  - Editing the GameObject’s **path** can be used to re-parent the GameObject (changing its parent in the hierarchy).
  - When inspecting GameObjects with a `Canvas`, transform controls may be influenced or overridden by RectTransform anchors.

- **Reflection Inspector (instance)** (`[R]` tab prefix)
  - Used for arbitrary non-GameObject objects.
  - Uses reflection to enumerate fields and properties.
  - Does not auto-apply changes; you must click **Apply** to persist modifications.
  - Provides a **filter bar** to quickly locate members by name.
  - Offers scope filters (All / Instance / Static) and member-type toggles (properties, fields, methods, constructors).
  - Supports expanding complex values via a `▼` button for types like strings, enums, lists, dictionaries, and some structs.

- **Reflection Inspector (static)** (`[S]` tab prefix)
  - Similar to the reflection inspector, but focused on **static** members of types.
  - Useful for inspecting and modifying global/static state (e.g., static configuration, singleton fields that aren’t discovered by search).

### 3.2 Special Handling for Media & Assets

Certain Unity types have additional inspector features:

- **Textures, Images, Sprites, Materials**
  - Provide a **View Texture** button.
  - Opens a viewer for the texture(s).
  - Allows saving textures as `.png` files to disk.

- **AudioClip**
  - Offers a **Show Player** button.
  - Opens an audio player widget for previewing the clip.
  - For clips loaded with `DecompressOnLoad`, includes an option to export them as `.wav` files.

### 3.3 dnSpy Integration

For types whose assemblies are physically present on disk, the reflection inspector exposes a "View in dnSpy" button:

- The dnSpy path is configured via the `DnSpy Path` option in UnityExplorer settings (see Config section).
- When the path points to a valid `dnspy.exe`, the button:
  - Launches dnSpy.
  - Opens the assembly containing the inspected type.
  - Selects the specific type in dnSpy’s tree (`--select T:<FullName>`).
- If the type comes from an in-memory assembly or no valid dnSpy path is configured, the button is hidden or shows a notification prompting you to set the path.

### 3.4 Editing Workflow

Across inspectors:

- Most members are editable when they are not readonly.
- Changes are applied either immediately (GameObject inspector) or via an explicit **Update/Apply** action (reflection inspectors).
- The inspector respects Unity’s type system; editing invalid values usually results in validation errors or is ignored.
- The inspector UI is deeply integrated with the rest of UnityExplorer (e.g., selection in Object Explorer opens corresponding inspector tabs).

---

## 4. C# Console & Scripting

UnityExplorer provides an in-game C# console for executing code and managing scripts.

### 4.1 Interactive REPL

- Uses `Mono.CSharp.Evaluator` (via a custom `ScriptEvaluator`) to:
  - Define temporary classes and methods.
  - Run immediate C# statements and expressions.
- Keeps REPL state across commands during a session.
- Provides a **Help/Welcome** dropdown in the console UI with more detailed usage information.

### 4.2 Editor Features (Line Numbers, Highlighting, Suggestions)

The C# console includes a fairly full-featured text editor surface:

- **Line numbers** rendered in a dedicated column next to the input area.
- **Syntax highlighting** implemented via a layered text element and a custom lexer (keywords, numbers, strings, comments, etc.).
- **Auto-complete / suggestions** support via an AutoComplete panel and a `CSAutoCompleter` backend.
- Toggles for:
  - "Compile on Ctrl+R" (maps compile to a Ctrl+R keybind).
  - "Suggestions" (enables or disables auto-complete behavior).
  - "Auto-indent" for nicer editing of multi-line code.

### 4.3 Startup Scripts

- On startup, UnityExplorer looks for a script named `startup.cs` in the `sinai-dev-UnityExplorer\Scripts\` folder.
- If present, it can be executed automatically to set up a custom environment, register hooks, or initialize game-specific debugging utilities.
- The `Scripts` folder is created next to the UnityExplorer DLL location if it does not already exist.

### 4.4 Script Load/Save/Compile (Fork Enhancement)

In the `yukieiji/UnityExplorer` fork used here, the C# console also supports file-based script management:

- Manage `.cs` files under `sinai-dev-UnityExplorer\Scripts\` via the console UI:
  - **Load**: Press the **Refresh** button and select a script from the dropdown to load it into the console.
  - **Save & Compile**: Press the **Compile** button; UnityExplorer compiles the script and automatically saves it back to the selected file.
- This workflow makes it easier to iterate on larger scripts than with pure REPL input.

### 4.5 Clipboard Helpers

- The console exposes helper methods:
  - `Copy(obj)` to push a value into UnityExplorer’s clipboard.
  - `Paste()` to retrieve the current clipboard value.
- This integrates with the Clipboard panel (see below), making it easy to move values between code, inspectors, and runtime state.

---

## 5. Hook Manager

The Hook Manager provides a UI for creating and managing Harmony-style method patches for debugging or instrumentation.

- The **Hooks** panel allows you to select a class and choose methods to hook.
- For each hook, UnityExplorer generates a patch class whose source can be edited via the **Edit Hook Source** button.
- Recognized patch method names (per Harmony conventions):
  - `Prefix` — may return `bool` or `void`.
  - `Postfix` — run after the original method.
  - `Finalizer` — may return `Exception` or `void`.
  - `Transpiler` — must return `IEnumerable<HarmonyLib.CodeInstruction>`.
- Multiple patch methods (e.g., multiple prefixes) can be defined per hook if desired.
- Patches are applied at runtime and can be used to log calls, modify arguments/return values, or temporarily change game behavior for debugging.

---

## 6. Mouse Inspect

Mouse Inspect allows you to quickly inspect objects under the cursor without manually navigating the hierarchy.

- Exposed as a **Mouse Inspect** dropdown within the Inspector panel’s title bar.
- Provides two main modes:
  - **World**
    - Uses `Physics.Raycast` from the camera through the mouse position.
    - Finds `Collider` components under the cursor.
    - Typically used to pick world-space objects (enemies, pickups, environment props, etc.).
  - **UI**
    - Uses `GraphicRaycaster` components on canvases.
    - Finds UI elements (buttons, panels, labels) under the cursor.
- When multiple UI objects are hit, UnityExplorer opens the **UI Inspector Results** panel:
  - Lists all hit UI elements using a scrollable list.
  - Clicking an entry opens an inspector for that object.

Objects found via Mouse Inspect can be opened directly in an inspector tab or used as the current selection.

---

## 7. Freecam

UnityExplorer includes a basic free camera that can be used to inspect the world independently of the game’s normal camera behavior.

- **Free Camera Controls**
  - Gives you keyboard and mouse-driven control over a camera in the scene (WASD/arrow keys, Space/Ctrl/PageUp/PageDown plus right mouse for look).
  - Allows flying around the scene, zooming, and viewing objects from arbitrary angles.
- **Decoupled from the Menu**
  - Freecam continues to function even while the UnityExplorer menu is hidden.
  - This makes it useful for taking clean screenshots or inspecting the world without the overlay visible.
- **Camera Source Options**
  - Can reuse the game’s main camera and later restore its original position/rotation.
  - Or create and use a separate dedicated camera tagged `UE_Freecam`.
- The Freecam panel lets you:
  - Start/stop Freecam.
  - Choose whether to reuse the game camera.
  - View and edit the current camera position as a `Vector3` string.
  - Adjust movement speed.

---

## 8. Clipboard Panel

The Clipboard panel centralizes copy/paste operations across inspectors and the C# console.

- Shows the current clipboard value and allows clearing it (reset to `null`).
- Supports copying values from multiple sources:
  - Any member in a Reflection Inspector.
  - Elements in enumerables and dictionaries.
  - The target object of any inspector tab.
- Supports pasting values into:
  - Members in Reflection Inspectors (with type compatibility checks).
  - Arguments in Method/Property evaluators (even for some non-parsable arguments, when pasting values instead of typing them).
- Integrates with the console’s `Copy(obj)` and `Paste()` helpers to move values between code and UI.
- Includes an **Inspect** button to open the current clipboard value in an inspector (with null/destroyed safety checks).

---

## 9. Log Panel & Logging

UnityExplorer includes a Log panel for viewing log messages inside the game.

- Shows UnityExplorer’s internal logs with color-coding by severity (info, warning, error, exception).
- Can optionally show Unity’s own `Debug.Log` output when `Log Unity Debug` is enabled in the config.
- Supports:
  - **Clear**: clears the in-memory log list.
  - **Open Log File**: opens the current log file on disk.
  - A toggle for enabling/disabling Unity debug log forwarding.
- Persists logs to disk when `Log To Disk` is enabled:
  - Log files are written under `{ExplorerFolder}/Logs`.
  - Filenames are timestamped; older log files are rotated to avoid unbounded growth.

---

## 10. Time Scale Controls & Keybinds

UnityExplorer provides direct controls for Unity’s `Time.timeScale`, both via the navbar and via keybinds.

- The top navbar includes a **Time** widget:
  - A numeric input bound to `Time.timeScale`.
  - A **Lock/Unlock** button that locks the time scale at the desired value.
  - When locked, UnityExplorer uses a Harmony patch on `Time.timeScale` to keep the value fixed, ignoring external changes.
- Configurable keybinds in settings:
  - `TimeScale Toggle`: toggles the lock state at the current value.
  - `Pause Keybind`: locks `Time.timeScale` to `0.0`.
  - `Playback Keybind`: locks `Time.timeScale` to `1.0`.
  - `Speed-Down Keybind`: locks to half of the current desired time scale.
  - `Speed-Up Keybind`: locks to double the current desired time scale.

These features make it easy to pause, slow down, or speed up gameplay for debugging or capture purposes.

---

## 11. Settings, Hotkeys & Configuration

UnityExplorer exposes many runtime settings via an **Options** tab and corresponding config files, allowing it to adapt to different games and loaders.

### 11.1 Configuration Files

In this fork UnityExplorer is injected through BepInEx 6 IL2CPP, so the relevant config path is:

- **BepInEx**: `BepInEx\config\com.sinai.unityexplorer.cfg`

These configs control global behavior such as startup delays, input handling, overlay options, hotkeys, and various feature toggles.

### 11.2 Notable Settings & Hotkeys

Some of the more important user-facing options:

- **Master Toggle (`UnityExplorer Toggle`)**
  - Key that opens/closes the UnityExplorer UI and features.
  - Default is `F7` (can be changed in settings); the navbar close button shows the current key.

- **Startup & Display**
  - `Hide On Startup`: whether UnityExplorer starts hidden.
  - `Startup Delay Time`: delay before UI creation, useful when early startup causes issues.
  - `Target Display`: which monitor UnityExplorer should use in multi-monitor setups.
  - `Main Navbar Anchor`: whether the navbar appears at the top or bottom of the screen.

- **Mouse & Input**
  - `Force Unlock Mouse`: whether to force the cursor unlocked while the menu is open.
  - `Force Unlock Toggle Key`: keybind that toggles the above at runtime.
  - `World Mouse-Inspect Keybind` / `UI Mouse-Inspect Keybind`: optional hotkeys to start Mouse Inspect in the corresponding mode.

- **Logging**
  - `Log Unity Debug`: whether to forward `UnityEngine.Debug.Log` messages into UnityExplorer’s log panel.
  - `Log To Disk`: whether to persist log entries to log files under the Explorer folder.

- **Export & External Tools**
  - `Default Output Path`: base folder used when exporting textures, audio, etc.
  - `DnSpy Path`: full path to `dnspy.exe` (used by the reflection inspector’s "View in dnSpy" button).

- **Console & Reflection Safety**
  - `CSharp Console Assembly Blacklist`: a semicolon-separated list of assemblies the C# console should not reference.
  - `Member Signature Blacklist`: signatures of problematic members that should be hidden from reflection inspectors.

The Options panel surfaces these settings as editable entries; changes are saved via a "Save Options" button, which writes the current configuration back to disk.

---

## 12. Programmatic Inspector API

UnityExplorer exposes a small API for other mods or scripts to open inspectors without going through the UI manually.

- The main entry point is the `UnityExplorer.InspectorManager` class.
- From external code, such as another BepInEx plugin, you can:

```csharp
// Inspect a specific object instance
UnityExplorer.InspectorManager.Inspect(theObject);

// Inspect the static members of a type
UnityExplorer.InspectorManager.Inspect(typeof(SomeClass));
```

- These calls create new inspector tabs targeting the given object or type, leveraging the same inspector functionality described above.
- This API makes it easy to integrate UnityExplorer with other tooling, e.g., debugging helpers that open inspectors for particular game systems.

---

## 13. Summary

In summary, UnityExplorer (without MCP) acts as a comprehensive in-game debugger and editor for Unity titles:

- It exposes scenes, objects, components, and assets via an interactive explorer.
- It lets you inspect and modify values through GameObject and reflection-based inspectors, including dnSpy integration for deeper static analysis.
- It provides a powerful C# console with a code editor UI and a hook manager for dynamic debugging and instrumentation.
- It augments inspection with Freecam, Mouse Inspect (plus UI results), Clipboard utilities, logging, and time-scale controls.
- It can be configured and extended both via config files and a lightweight Inspector API.

These capabilities form the foundation that the MCP integration builds upon, but they are all available to users even when MCP is not enabled or present.
