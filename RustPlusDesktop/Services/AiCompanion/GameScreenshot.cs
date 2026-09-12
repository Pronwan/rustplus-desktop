using System;
using System.Collections.Generic;
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

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        private const uint WDA_NONE = 0x00000000;

        /// <summary>
        /// Windows 10 2004 and later: the window stays on screen but is left out of anything
        /// that captures the display. The same mechanism a banking app uses to keep itself out
        /// of screenshots, and the reason this works without touching the game at all.
        /// </summary>
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// Captures the screen the game is on, or the primary one when it is not running.
        ///
        /// The whole screen rather than the game's window: Rust in borderless fullscreen fills a
        /// display anyway, and a windowed client with an overlay over it is still better
        /// answered by what the player can actually see.
        /// </summary>
        /// <param name="exclude">
        /// Our own overlay windows. They sit on top of the game, so without this the picture
        /// sent to the model is mostly a picture of this app — and the one thing the model
        /// does not need help with is reading its own tile.
        ///
        /// Nothing here reads from or writes to the game's process: this is a copy of what
        /// the display controller is already showing, the same thing a screen recorder takes,
        /// so there is nothing for an anti-cheat to object to.
        /// </param>
        public static Task<string?> CaptureAsync(IReadOnlyList<IntPtr>? exclude = null)
        {
            return Task.Run<string?>(() =>
            {
                var hidden = Exclude(exclude);

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
                finally
                {
                    foreach (var hwnd in hidden)
                    {
                        try { SetWindowDisplayAffinity(hwnd, WDA_NONE); } catch { }
                    }
                }
            });
        }

        /// <summary>
        /// Takes our windows out of the capture and returns the ones that accepted it.
        ///
        /// Only those, because the flag has to come back off afterwards and a window that
        /// never took it must not be reset — on a build too old for EXCLUDEFROMCAPTURE the
        /// call simply fails and the overlay is in the picture, which is a worse screenshot
        /// and not a broken one.
        /// </summary>
        private static List<IntPtr> Exclude(IReadOnlyList<IntPtr>? windows)
        {
            var done = new List<IntPtr>();
            if (windows == null) return done;

            foreach (var hwnd in windows)
            {
                if (hwnd == IntPtr.Zero) continue;

                try
                {
                    if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)) done.Add(hwnd);
                }
                catch { }
            }

            // The compositor needs a frame to drop them. Without it the capture can still
            // catch the frame they were last drawn in.
            if (done.Count > 0) System.Threading.Thread.Sleep(60);

            return done;
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
