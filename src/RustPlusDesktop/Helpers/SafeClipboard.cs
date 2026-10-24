using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace RustPlusDesk.Helpers
{
    /// <summary>
    /// Safe wrapper for Windows Clipboard operations to avoid COMException (0x800401D0 / CLIPBRD_E_CANT_OPEN)
    /// when the system clipboard is momentarily locked by other applications or background processes.
    /// </summary>
    public static class SafeClipboard
    {
        public static bool SetText(string? text, int maxRetries = 6, int delayMs = 60)
        {
            if (string.IsNullOrEmpty(text)) return false;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (COMException)
                {
                    if (i == maxRetries - 1) return false;
                    Thread.Sleep(delayMs);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
