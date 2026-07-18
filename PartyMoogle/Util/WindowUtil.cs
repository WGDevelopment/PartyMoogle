using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PartyMoogle.Util;

/// <summary>
/// Window-focus detection for the "away = window unfocused/minimized" half of the
/// presence gate. Windows-only (the plugin targets net10.0-windows).
/// </summary>
public static class WindowUtil
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static IntPtr gameWindow = IntPtr.Zero;

    private static IntPtr GameWindow
    {
        get
        {
            // MainWindowHandle is stable once the game window exists; cache it.
            if (gameWindow == IntPtr.Zero)
                gameWindow = Process.GetCurrentProcess().MainWindowHandle;
            return gameWindow;
        }
    }

    /// <summary>True when the FFXIV window is not the foreground window (alt-tabbed / minimized).</summary>
    public static bool IsGameUnfocused()
    {
        var handle = GameWindow;
        if (handle == IntPtr.Zero)
            return false; // can't tell — treat as focused to avoid false positives

        return GetForegroundWindow() != handle;
    }
}
