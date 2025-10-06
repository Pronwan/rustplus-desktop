using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

public sealed class GlobalHotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 👇 HIER die Konstanten hin:
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000; // verhindert Autorepeat von WM_HOTKEY

    private readonly IntPtr _hwnd;
    private int _nextId = 1;
    private readonly Dictionary<int, string> _idToGesture = new();
    private readonly Dictionary<string, int> _gestureToId = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? HotkeyPressed;

    public GlobalHotkeyManager(IntPtr hwnd) => _hwnd = hwnd;

    public bool Register(string gesture)
    {
        if (_gestureToId.ContainsKey(gesture)) return true;
        if (!TryParseGesture(gesture, out var mods, out var vk)) return false;

        int id = _nextId++;
        // 👇 NOREPEAT hier addieren
        if (!RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            return false;

        _gestureToId[gesture] = id;
        _idToGesture[id] = gesture;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToGesture.Keys)
            UnregisterHotKey(_hwnd, id);
        _idToGesture.Clear();
        _gestureToId.Clear();
    }

    public void OnWmHotkey(IntPtr wParam, IntPtr lParam)
    {
        int id = wParam.ToInt32();
        if (_idToGesture.TryGetValue(id, out var g))
            HotkeyPressed?.Invoke(g);
    }

    public void Dispose() => UnregisterAll();

    public static string Format(Key key, bool ctrl, bool alt, bool shift, bool win)
    {
        // Gleiche Schreibweise wie TryParseGesture erwartet
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key.ToString()); // z.B. A, D1, F5, OemPlus ...
        return string.Join("+", parts);
    }

    private static bool TryParseGesture(string gesture, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Key? key = null;
        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Meta", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
            else { if (!Enum.TryParse(p, true, out Key k)) return false; key = k; }
        }
        if (key is null) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return true;
    }
}