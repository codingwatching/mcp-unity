#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using UnityEditor;

namespace McpUnity.Utils
{
    /// <summary>
    /// Keeps the Unity Editor loop ticking while the editor window is unfocused.
    ///
    /// Unity 6.4 on Windows parks the editor tick loop entirely when the editor
    /// is not the foreground application (regression of
    /// https://github.com/CoderGamester/mcp-unity/issues/147), so callbacks
    /// scheduled via EditorApplication.delayCall — which the WebSocket handler
    /// relies on — never run and every MCP request times out.
    ///
    /// The main thread still pumps OS messages in the background (the window is
    /// never reported "Not Responding"), so a native Win32 timer created on the
    /// main thread keeps firing its callback there. From that callback we ask
    /// Unity to run a player-loop update, which flushes delayCall, coroutines
    /// and the synchronization context.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpBackgroundTick
    {
        private delegate void TimerProc(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime);

        [DllImport("user32.dll")]
        private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

        [DllImport("user32.dll")]
        private static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        private const uint TickIntervalMs = 100;

        // Rooted in a static field so the native timer never invokes a collected delegate.
        private static readonly TimerProc Callback = OnTimer;
        private static UIntPtr _timerId;

        static McpBackgroundTick()
        {
            _timerId = SetTimer(IntPtr.Zero, UIntPtr.Zero, TickIntervalMs, Callback);

            // The timer must not outlive the scripting domain: a native callback
            // into an unloaded domain crashes the editor.
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (_timerId != UIntPtr.Zero)
                {
                    KillTimer(IntPtr.Zero, _timerId);
                    _timerId = UIntPtr.Zero;
                }
            };
        }

        private static void OnTimer(IntPtr hWnd, uint uMsg, UIntPtr nIDEvent, uint dwTime)
        {
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception)
            {
                // Never let an exception escape into the native timer dispatch.
            }
        }
    }
}
#endif
