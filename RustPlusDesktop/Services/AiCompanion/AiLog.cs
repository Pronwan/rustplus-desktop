using System;
using System.Diagnostics;
using System.Windows;
using RustPlusDesk.Views;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Logging for AI Companion operations to console output and the app's diagnostic log.
    /// </summary>
    public static class AiLog
    {
        public static void Info(string message)
        {
            var formatted = $"[AI Companion] {message}";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {formatted}");
            Debug.WriteLine(formatted);

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (Application.Current?.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.AppendLog(formatted);
                        }
                    });
                }
            }
            catch
            {
                // Logging failure should never interrupt application flow
            }
        }
    }
}
