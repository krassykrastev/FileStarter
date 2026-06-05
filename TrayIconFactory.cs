using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public static class TrayIconFactory
    {
        public static Icon CreateAutoStartStateIcon(bool enabled, int renderSize)
        {
            renderSize = Math.Clamp(renderSize, 16, 128);

            using Icon baseIcon = LoadBaseIcon(renderSize);
            using var bmp = new Bitmap(renderSize, renderSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            g.DrawIcon(baseIcon, new Rectangle(0, 0, renderSize, renderSize));

            if (!enabled)
            {
                DrawBusyBadge(g, renderSize);
            }

            IntPtr hIcon = bmp.GetHicon();
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

        private static void DrawBusyBadge(Graphics g, int renderSize)
        {
            int diameter = Math.Max(20, ((renderSize * 2) / 3) -2);
            int margin = Math.Max(1, renderSize / 20);
            int x = renderSize - diameter - margin;
            int y = renderSize - diameter - margin;
            var badgeRect = new Rectangle(x, y, diameter, diameter);

            using var fillBrush = new SolidBrush(Color.FromArgb(196, 49, 75));
            using var borderPen = new Pen(Color.Black, 1f);

            g.FillEllipse(fillBrush, badgeRect);
            g.DrawEllipse(borderPen, badgeRect);
        }

        private static Icon LoadBaseIcon(int renderSize)
        {
            string[] candidatePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico"),
                Path.Combine(Application.StartupPath, "Assets", "app.ico"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico")
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                    return new Icon(path, new Size(renderSize, renderSize));
            }

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                Icon? exeIcon = Icon.ExtractAssociatedIcon(exePath);
                if (exeIcon != null)
                    return new Icon(exeIcon, new Size(renderSize, renderSize));
            }

            return new Icon(SystemIcons.Application, new Size(renderSize, renderSize));
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}