global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Reflection;
global using UnityEngine;
global using UnityEngine.UI;
global using UniverseLib;
global using UniverseLib.Utility;

using UnityExplorer.Config;
using UnityExplorer.ObjectExplorer;
using UnityExplorer.Runtime;
using UnityExplorer.UI;
using UnityExplorer.UI.Panels;

using HarmonyPatch = HarmonyLib.Harmony;

namespace UnityExplorer;

public static class ExplorerCore
{
    public const string NAME = "UnityExplorer";
    public const string VERSION = "4.13.6";
    public const string AUTHOR = "Sinai, yukieiji";
    public const string GUID = "com.sinai.unityexplorer";

    public static IExplorerLoader Loader { get; private set; }
    public static string ExplorerFolder => Path.Combine(Loader.ExplorerFolderDestination, Loader.ExplorerFolderName);
    public const string DEFAULT_EXPLORER_FOLDER_NAME = "sinai-dev-UnityExplorer";

    public static HarmonyPatch Harmony { get; } = new HarmonyPatch(GUID);

    /// <summary>
    /// Initialize UnityExplorer with the provided Loader implementation.
    /// </summary>
    public static void Init(IExplorerLoader loader)
    {
        if (Loader != null)
            throw new Exception("UnityExplorer is already loaded.");

        Loader = loader;

        Log($"{NAME} {VERSION} initializing...");

        CheckLegacyExplorerFolder();
        Directory.CreateDirectory(ExplorerFolder);
        ConfigManager.Init(Loader.ConfigHandler);

        Universe.Init(ConfigManager.Startup_Delay_Time.Value, LateInit, Log, new()
        {
            Disable_EventSystem_Override = ConfigManager.Disable_EventSystem_Override.Value,
            Force_Unlock_Mouse = ConfigManager.Force_Unlock_Mouse.Value,
            Disable_Setup_Force_ReLoad_ManagedAssemblies = ConfigManager.Disable_Setup_Force_ReLoad_ManagedAssemblies.Value,
            Bypass_UniverseLib_ICall = ConfigManager.Bypass_UniverseLib_ICall.Value,
            Unhollowed_Modules_Folder = loader.UnhollowedModulesFolder
        });

        UERuntimeHelper.Init();
        ExplorerBehaviour.Setup();
        UnityCrashPrevention.Init();
    }

    // Do a delayed setup so that objects aren't destroyed instantly.
    // This can happen for a multitude of reasons.
    // Default delay is 1 second which is usually enough.
    static void LateInit()
    {
        SceneHandler.Init();

        Log($"Creating UI...");

        UIManager.InitUI();

        Log($"{NAME} {VERSION} ({Universe.Context}) initialized.");

#if INTEROP
        // Capture Unity main thread context and start MCP server (CoreCLR targets only)
        try
        {
            Mcp.MainThread.Capture();
            Mcp.McpHost.StartIfEnabled();

            // Stream selection changes to connected MCP streaming clients
            InspectorManager.OnInspectedTabsChanged += () =>
            {
                try
                {
                    var task = Mcp.UnityReadTools.GetSelection(default);
                    task.ContinueWith(t =>
                    {
                        if (!t.IsCompletedSuccessfully || t.Result == null) return;
                        var http = Mcp.McpSimpleHttp.Current;
                        if (http != null)
                        {
                            _ = http.BroadcastNotificationAsync("selection", t.Result);
                        }
                    });
                }
                catch { }
            };

            // Stream scene events
            ObjectExplorer.SceneHandler.OnLoadedScenesUpdated += (scenes) =>
            {
                try
                {
                    var payload = new
                    {
                        loaded = scenes.Select(s => new { name = s.name, handle = s.handle, isLoaded = s.isLoaded }),
                        count = scenes.Count
                    };
                    var http = Mcp.McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("scenes", payload);
                    Mcp.McpSceneDiffState.UpdateScenes(scenes);
                }
                catch { }
            };

            ObjectExplorer.SceneHandler.OnInspectedSceneChanged += (scene) =>
            {
                try
                {
                    var http = Mcp.McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("inspected_scene", new { name = scene.name, handle = scene.handle, isLoaded = scene.isLoaded });
                }
                catch { }
            };
        }
        catch (Exception ex)
        {
            LogWarning($"MCP bootstrap failed: {ex.Message}");
        }
#elif MONO
        try
        {
            Mcp.MainThread.Capture();
            Mcp.McpHost.StartIfEnabled();

            InspectorManager.OnInspectedTabsChanged += () =>
            {
                try
                {
                    var http = Mcp.McpSimpleHttp.Current;
                    if (http != null)
                    {
                        var sel = http.GetSelectionSnapshot();
                        http.BroadcastNotificationAsync("selection", sel);
                    }
                }
                catch { }
            };

            ObjectExplorer.SceneHandler.OnLoadedScenesUpdated += (scenes) =>
            {
                try
                {
                    var payload = new
                    {
                        loaded = scenes.Select(s => new { name = s.name, handle = s.handle, isLoaded = s.isLoaded }),
                        count = scenes.Count
                    };
                    var http = Mcp.McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("scenes", payload);
                    Mcp.McpSceneDiffState.UpdateScenes(scenes);
                }
                catch { }
            };

            ObjectExplorer.SceneHandler.OnInspectedSceneChanged += (scene) =>
            {
                try
                {
                    var http = Mcp.McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("inspected_scene", new { name = scene.name, handle = scene.handle, isLoaded = scene.isLoaded });
                }
                catch { }
            };
        }
        catch (Exception ex)
        {
            LogWarning($"MCP (Mono) bootstrap failed: {ex.Message}");
        }
#else
        // MCP disabled on this build (INTEROP not defined).
#endif

        // InspectorManager.Inspect(typeof(Tests.TestClass));
    }

    internal static void Update()
    {
#if INTEROP
        Mcp.MainThread.Pump();
#endif
        ExplorerKeybind.Update();
    }


    #region LOGGING

    public static void Log(object message)
        => Log(message, LogType.Log);

    public static void LogWarning(object message)
        => Log(message, LogType.Warning);

    public static void LogError(object message)
        => Log(message, LogType.Error);

    public static void LogUnity(object message, LogType logType)
    {
        if (!ConfigManager.Log_Unity_Debug.Value)
            return;

        Log($"[Unity] {message}", logType);
    }

    private static void Log(object message, LogType logType)
    {
        string log = message?.ToString() ?? "";

        LogPanel.Log(log, logType);

        switch (logType)
        {
            case LogType.Assert:
            case LogType.Log:
                Loader.OnLogMessage(log);
#if INTEROP || MONO
                Mcp.LogBuffer.Add("info", log);
#endif
                break;

            case LogType.Warning:
                Loader.OnLogWarning(log);
#if INTEROP || MONO
                Mcp.LogBuffer.Add("warn", log);
#endif
                break;

            case LogType.Error:
            case LogType.Exception:
                Loader.OnLogError(log);
#if INTEROP || MONO
                Mcp.LogBuffer.Add("error", log);
#endif
                break;
        }
    }

    #endregion


    #region LEGACY FOLDER MIGRATION

    // Can be removed eventually. For migration from <4.7.0
    static void CheckLegacyExplorerFolder()
    {
        string legacyPath = Path.Combine(Loader.ExplorerFolderDestination, "UnityExplorer");
        if (Directory.Exists(legacyPath))
        {
            LogWarning($"Attempting to migrate old 'UnityExplorer/' folder to 'sinai-dev-UnityExplorer/'...");

            // If new folder doesn't exist yet, let's just use Move().
            if (!Directory.Exists(ExplorerFolder))
            {
                try
                {
                    Directory.Move(legacyPath, ExplorerFolder);
                    Log("Migrated successfully.");
                }
                catch (Exception ex)
                {
                    LogWarning($"Exception migrating folder: {ex}");
                }
            }
            else // We have to merge
            {
                try
                {
                    CopyAll(new(legacyPath), new(ExplorerFolder));
                    Directory.Delete(legacyPath, true);
                    Log("Migrated successfully.");
                }
                catch (Exception ex)
                {
                    LogWarning($"Exception migrating folder: {ex}");
                }
            }
        }
    }

    public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy each file into it's new directory.
        foreach (FileInfo fi in source.GetFiles())
        {
            fi.MoveTo(Path.Combine(target.ToString(), fi.Name));
        }

        // Copy each subdirectory using recursion.
        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir = target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }

    #endregion
}
