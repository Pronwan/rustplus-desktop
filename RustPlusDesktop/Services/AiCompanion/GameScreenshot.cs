using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Takes a picture of the game for the companion to look at.
    ///
    /// It is deliberately slow to fire. Clicking anything in this app means leaving Rust — alt
    /// tab, or opening the chat or the crafting menu first — so a shot taken the instant the
    /// button is pressed is a picture of a menu, or of the desktop. The countdown is the time to
    /// get back in.
    /// </summary>
    public static class GameScreenshot
    {
        private const string GameProcessName = "RustClient";

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// Captures the screen the game is on, or the primary one when it is not running.
        ///
        /// The whole screen rather than the game's window: Rust in borderless fullscreen fills a
        /// display anyway, and a windowed client with an overlay over it is still better
        /// answered by what the player can actually see.
        /// </summary>
        public static Task<string?> CaptureAsync()
        {
            return Task.Run<string?>(() =>
            {
                try
                {
                    var bounds = GameScreenBounds();

                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

                    var folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk", "ai-companion");
                    Directory.CreateDirectory(folder);

                    var path = Path.Combine(folder, $"shot-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jpg");

                    // JPEG at 80: a screenshot goes up with the question, and a lossless 4K frame
                    // is eight megabytes of upload for detail no model needs to read a map.
                    bitmap.Save(path, JpegEncoder(), JpegQuality(80));
                    return path;
                }
                catch
                {
                    return null;
                }
            });
        }

        private static Rectangle GameScreenBounds()
        {
            try
            {
                var processes = Process.GetProcessesByName(GameProcessName);
                if (processes.Length > 0)
                {
                    var handle = processes[0].MainWindowHandle;
                    if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
                    {
                        var middle = new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                        return System.Windows.Forms.Screen.FromPoint(middle).Bounds;
                    }
                }
            }
            catch { }

            return System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        }

        private static ImageCodecInfo JpegEncoder() =>
            Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid)!;

        private static EncoderParameters JpegQuality(long quality)
        {
            var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            return parameters;
        }

        public static void Delete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
