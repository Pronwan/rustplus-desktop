using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace RustPlusDesk.Services;

/// <summary>Suppresses the exit-pointer-lock reminder belonging to the local 3D viewer.</summary>
internal sealed class Map3DPointerLockNotice : IDisposable
{
    private readonly uint _browserProcessId;
    private readonly WinEventCallback _callback;
    private readonly ConcurrentDictionary<IntPtr, byte> _pending = new();
    private IntPtr _hook;

    public Map3DPointerLockNotice(uint browserProcessId)
    {
        _browserProcessId = browserProcessId;
        _callback = OnWindowShown;
        _hook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, _callback, browserProcessId, 0, 0);
    }

    private void OnWindowShown(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint threadId, uint time)
    {
        if (_hook == IntPtr.Zero || objectId != 0 || childId != 0 || window == IntPtr.Zero) return;
        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        if (!className.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal) ||
            GetAncestor(window, 2) != window || !_pending.TryAdd(window, 0)) return;
        _ = Task.Run(() =>
        {
            try
            {
                GetWindowThreadProcessId(window, out uint processId);
                if (_hook == IntPtr.Zero || processId != _browserProcessId || !IsWindowVisible(window)) return;
                var root = AutomationElement.FromHandle(window);
                var elements = root.FindAll(TreeScope.Subtree, Condition.TrueCondition);
                var names = elements.Cast<AutomationElement>().Select(element => element.Current.Name).ToArray();
                if (_hook != IntPtr.Zero && IsPointerLockNotice(names)) ShowWindow(window, 0);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
            {
                // Popups can disappear while their accessibility tree is being read.
            }
            finally { _pending.TryRemove(window, out _); }
        });
    }

    internal static bool IsPointerLockNotice(IEnumerable<string> names)
    {
        var labels = names.ToArray();
        bool viewer = labels.Any(name => string.Equals(name, "rustplus3d.local", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("rustplus3d.local ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "https://rustplus3d.local", StringComparison.OrdinalIgnoreCase));
        // shortcut: matches the English runtime reminder; replace with a supported WebView2 setting when available.
        string text = string.Join(" ", labels);
        return viewer && text.Contains("To show your cursor, press", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("Esc", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        IntPtr hook = System.Threading.Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
