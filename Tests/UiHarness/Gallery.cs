using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sentinel.UI;

namespace Sentinel.UiHarness
{
    /// <summary>
    /// Screenshot matrix for visual review: every captured state in dark + light theme and at the supported window
    /// sizes (default 1280×780, large 1440×900, minimum 980×560). Written to out/gallery.
    /// </summary>
    internal static class Gallery
    {
        public static string Dir;

        private static readonly Size[] AllSizes = { new Size(1280, 780), new Size(1440, 900), new Size(980, 560) };
        private static readonly Size[] DefaultOnly = { new Size(1280, 780) };

        public static void Capture(SentinelWindow window, string name, bool allSizes = false)
        {
            if (string.IsNullOrEmpty(Dir)) return;
            Directory.CreateDirectory(Dir);
            // Let panel reveals (≤180 ms, no loops) finish so captures show the settled state.
            Pump();
            System.Threading.Thread.Sleep(220);
            Pump();
            var startDark = window.Theme.IsDarkMode;
            foreach (var dark in new[] { true, false })
            {
                if (window.Theme.IsDarkMode != dark) window.Theme.ToggleTheme();
                foreach (var size in allSizes ? AllSizes : DefaultOnly)
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    Pump();
                    Render(window, 1.0, Path.Combine(Dir, name + "_" + (dark ? "dark" : "light") + "_" + size.Width + "x" + size.Height + ".png"));
                }
            }
            if (window.Theme.IsDarkMode != startDark) window.Theme.ToggleTheme();
            window.Width = 1280;
            window.Height = 780;
            Pump();
        }

        /// <summary>Renders at a display scale (1.0 = 96 dpi, 1.5 = 144 dpi) to check crispness at Windows scaling.</summary>
        public static void CaptureScaled(SentinelWindow window, string name, double scale)
        {
            if (string.IsNullOrEmpty(Dir)) return;
            Pump();
            Render(window, scale, Path.Combine(Dir, name + "_scale" + (int)(scale * 100) + ".png"));
        }

        public static void Render(Window w, double scale, string path)
        {
            var el = (FrameworkElement)w.Content;
            el.UpdateLayout();
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth * scale), (int)Math.Ceiling(el.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(el);
            PlanRenderer.Save(rtb, path);
        }

        private static void Pump(int rounds = 10)
        {
            for (int i = 0; i < rounds; i++)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
