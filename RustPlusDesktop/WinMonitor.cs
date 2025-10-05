using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RustPlusDesk
{
    public sealed class MonitorInfo
    {
        public string Name { get; init; } = "";
        public bool Primary { get; init; }
        // Gerätepixel (nicht DPI-korrigiert)
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public override string ToString() =>
            $"{(Primary ? "Hauptmonitor" : "Monitor")} {Width}×{Height} @ {Left},{Top} ({Name})";
    }

    public static class WinMonitors
    {
        public static IReadOnlyList<MonitorInfo> All()
        {
            var list = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (hMon, _, __, ___) =>
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        var r = mi.rcMonitor;
                        list.Add(new MonitorInfo
                        {
                            Name = mi.szDevice,
                            Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                            Left = r.left,
                            Top = r.top,
                            Width = r.right - r.left,
                            Height = r.bottom - r.top
                        });
                    }
                    return true;
                }, IntPtr.Zero);
            // Primären nach oben sortieren, optional
            list.Sort((a, b) => b.Primary.CompareTo(a.Primary));
            return list;
        }

        // P/Invoke
        private const int CCHDEVICENAME = 32;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    }
}