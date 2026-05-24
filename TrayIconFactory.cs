using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TeamsTrayStarter
{
    public static class TrayIconFactory
    {
        /// <summary>
        /// Creates a semaphore-style icon (green/red circle) at the requested render size.
        /// Windows may scale it depending on tray/DPI, but this controls source quality.
        /// </summary>
        public static Icon CreateSemaphoreIcon(bool enabled, int renderSize)
        {
            // Clamp to a reasonable range
            renderSize = Math.Clamp(renderSize, 16, 128);

            using var bmp = new Bitmap(renderSize, renderSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Colors
            var fill = enabled ? Color.FromArgb(0, 190, 0) : Color.FromArgb(210, 0, 0);
            var dark = enabled ? Color.FromArgb(0, 120, 0) : Color.FromArgb(120, 0, 0);

            // Padding scales with size
            int pad = Math.Max(2, renderSize / 8);
            var rect = new Rectangle(pad, pad, renderSize - pad * 2, renderSize - pad * 2);

            // Shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                var shadowRect = new Rectangle(rect.X + 1, rect.Y + 2, rect.Width, rect.Height);
                g.FillEllipse(shadowBrush, shadowRect);
            }

            // Main gradient fill
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);

                using var pgb = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(255, fill),
                    SurroundColors = new[] { dark }
                };
                g.FillEllipse(pgb, rect);
            }

            // Highlight
            var highlightRect = new Rectangle(
                rect.X + rect.Width / 5,
                rect.Y + rect.Height / 5,
                rect.Width / 2,
                rect.Height / 2);

            using (var highlightBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
            {
                g.FillEllipse(highlightBrush, highlightRect);
            }

            // Border thickness scales
            float border = Math.Max(1f, renderSize / 16f);
            using (var pen = new Pen(Color.FromArgb(180, 0, 0, 0), border))
            {
                g.DrawEllipse(pen, rect);
            }

            // Convert Bitmap -> Icon (avoid handle leaks)
            var hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}