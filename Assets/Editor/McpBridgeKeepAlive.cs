using System;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// Keeps the MCP for Unity bridge connected so an assistant can drive the editor: it turns on the package's
    /// "Auto-Start on Editor Load" (so a restarted editor starts the server and connects again), reconnects the
    /// session whenever it has dropped (an editor restart, a failed resume after a domain reload), and lets play
    /// mode keep running while the editor window is in the background.
    /// </summary>
    [InitializeOnLoad]
    static class McpBridgeKeepAlive
    {
        const string AutoStartOnLoadKey = "MCPForUnity.AutoStartOnLoad";
        const double CheckSeconds = 8;
        static double nextCheck, nextServerStart;
        static bool connecting;

        static McpBridgeKeepAlive()
        {
            if (Application.isBatchMode) return;
            if (!EditorPrefs.GetBool(AutoStartOnLoadKey, false)) EditorPrefs.SetBool(AutoStartOnLoadKey, true);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                // Play mode keeps stepping when the editor is not the focused window.
                if (state == PlayModeStateChange.EnteredPlayMode) Application.runInBackground = true;
            };
            nextCheck = EditorApplication.timeSinceStartup + 3;
        }

        static async void Tick()
        {
            if (connecting || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + CheckSeconds;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                var bridge = MCPServiceLocator.Bridge;
                if (bridge.IsRunning) return;
                var server = MCPServiceLocator.Server;
                if (!server.IsLocalHttpServerReachable())
                {
                    // No server at all: launch the local one (at most once a minute), then connect on a later check.
                    if (EditorApplication.timeSinceStartup >= nextServerStart && server.IsLocalUrl() && server.CanStartLocalServer() && !server.IsManagedServerLaunchProcessAlive())
                    {
                        nextServerStart = EditorApplication.timeSinceStartup + 60;
                        server.StartLocalHttpServer(quiet: true);
                    }
                    return;
                }
                connecting = true;
                if (await bridge.StartAsync()) Debug.Log("[Bad Apple] MCP bridge reconnected.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bad Apple] MCP bridge reconnect failed: " + e.Message);
            }
            finally { connecting = false; }
        }
    }
}
