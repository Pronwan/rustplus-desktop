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
        /// <param name="zoom">
        /// How much of the screen to keep, measured from the middle: 1 for all of it, 0.5 for
        /// the middle half. Anything under 1 is there to make small things legible — see
        /// <see cref="AiCompanionSettings.ScreenshotZoom"/>.
        /// </param>
        public static Task<string?> CaptureAsync(IReadOnlyList<IntPtr>? exclude = null, double zoom = 1.0)
        {
            return Task.Run<string?>(() =>
            {
                var hidden = Exclude(exclude);

                try
                {
                    var bounds = Crop(GameScreenBounds(), zoom);

                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

                    var folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk", "ai-companion");
                    Directory.CreateDirectory(folder);

                    var path = Path.Combine(folder, $"shot-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jpg");

                    // JPEG at 92 rather than 80.
                    //
                    // The things being asked about are small: a smart switch on a wall is a few
                    // dozen pixels before the provider scales the frame down again, and JPEG
                    // spends its error budget exactly there, on small high-contrast detail. The
                    // extra megabyte buys back the difference between naming the deployable and
                    // guessing at it, and the file never leaves this machine except with the
                    // question it belongs to.
                    bitmap.Save(path, JpegEncoder(), JpegQuality(92));
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
        /// <summary>
        /// <summary>
        /// The whole game screen, as a PNG, for somebody to draw a rectangle on.
        ///
        /// Kept apart from the JPEG one the companion sends: this is looked at closely and
        /// measured against, and JPEG's artefacts around small text would be measured too.
        /// </summary>
        public static Task<string?> CaptureWholeScreenAsync(IReadOnlyList<IntPtr>? exclude = null)
            => CaptureRegionAsync(0, 0, 1, 1, exclude, whole: true);

        /// Captures one part of the game screen, given as fractions of its WIDTH — all four.
        ///
        /// Fractions rather than pixels because the thing being looked for sits in the same
        /// place whatever the resolution. Fractions of the width rather than one axis each,
        /// because that is what the measurements say: Rust scales this part of its interface
        /// with the screen width, so the same band is 0.027 of the width from the top on a
        /// 2560x1440 screen and on a 1680x1050 one — while as a fraction of the height those
        /// two are 0.049 and 0.044, and a default tuned on one is wrong on the other.
        ///
        /// Returns a PNG rather than a JPEG: this one is read by a character recogniser, and
        /// JPEG spends its error budget on exactly the small high-contrast edges that letters
        /// are made of.
        /// </summary>
        public static Task<string?> CaptureRegionAsync(
            double left, double top, double width, double height,
            IReadOnlyList<IntPtr>? exclude = null, bool whole = false)
        {
            return Task.Run<string?>(() =>
            {
                var hidden = Exclude(exclude);

                try
                {
                    var screen = GameScreenBounds();

                    // Every one of them against the width — see the note above. The whole
                    // screen is the one case that cannot be expressed that way, since its
                    // height is not a fraction of its width.
                    int x = whole ? screen.Left : screen.Left + (int)Math.Round(screen.Width * Math.Clamp(left, 0, 1));
                    int y = whole ? screen.Top : screen.Top + (int)Math.Round(screen.Width * Math.Clamp(top, 0, 1));
                    int w = whole ? screen.Width : (int)Math.Round(screen.Width * Math.Clamp(width, 0.01, 1));
                    int h = whole ? screen.Height : (int)Math.Round(screen.Width * Math.Clamp(height, 0.005, 1));

                    // A region that runs off the edge is clipped rather than refused: it is a
                    // setting somebody dragged, and a slightly short crop still reads.
                    w = Math.Min(w, screen.Right - x);
                    h = Math.Min(h, screen.Bottom - y);
                    if (w < 8 || h < 8) return null;

                    using var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);

                    var folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk", "deaths");
                    Directory.CreateDirectory(folder);

                    // One name, overwritten each time: this is scratch for the recogniser and
                    // for the preview in the settings, not something to accumulate on disk.
                    var path = Path.Combine(folder, whole ? "death-full.png" : "death-screen.png");

                    try { if (File.Exists(path)) File.Delete(path); } catch { }

                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
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

        /// <summary>
        /// Keeps the middle of a screen, in the proportion asked for.
        ///
        /// The middle rather than anywhere else because this is a first-person game: what the
        /// player is asking about is what the crosshair is on.
        /// </summary>
        private static Rectangle Crop(Rectangle bounds, double zoom)
        {
            zoom = Math.Clamp(zoom, 0.2, 1.0);
            if (zoom >= 1.0) return bounds;

            int width = Math.Max(64, (int)Math.Round(bounds.Width * zoom));
            int height = Math.Max(64, (int)Math.Round(bounds.Height * zoom));

            return new Rectangle(
                bounds.Left + (bounds.Width - width) / 2,
                bounds.Top + (bounds.Height - height) / 2,
                width,
                height);
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
