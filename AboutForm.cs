using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 210);
            BackColor = Color.White;

            // App name at the top
            var appNameLabel = new Label
            {
                Text = "FileStarter",
                AutoSize = true,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(20, 18),
                BackColor = Color.White
            };

            // App icon in the top-right
            var iconBox = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point(ClientSize.Width - 78, 12),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.White
            };

            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    using Icon? appIcon = Icon.ExtractAssociatedIcon(exePath);
                    if (appIcon != null)
                    {
                        iconBox.Image = appIcon.ToBitmap();
                    }
                }
            }
            catch
            {
                // If icon cannot be loaded, just leave the PictureBox empty
            }

            var versionLabel = new Label
            {
                Text = "Version 1.6",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Location = new Point(22, 70),
                BackColor = Color.White
            };

            var authorLabel = new Label
            {
                Text = "Created by Krassy Krastev",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Location = new Point(22, 95),
                BackColor = Color.White
            };

            var copyrightLabel = new Label
            {
                Text = "© 2026 All rights reserved.",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Location = new Point(22, 120),
                BackColor = Color.White
            };

            // Bottom panel (support link + OK button)
            var bottomPanel = new Panel
            {
                Location = new Point(0, ClientSize.Height - 72),
                Size = new Size(ClientSize.Width, 72),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.White
            };

            var supportLinkLabel = new LinkLabel
            {
                Text = "Enjoying FileStarter? Buy me a beer ;-)",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.White
            };

            const string clickablePart = "Buy me a beer ;-)";
            int supportClickableStart = supportLinkLabel.Text.IndexOf(clickablePart, StringComparison.Ordinal);

            if (supportClickableStart >= 0)
            {
                supportLinkLabel.Links.Add(
                    supportClickableStart,
                    clickablePart.Length,
                    "https://www.paypal.com/paypalme/krasikrastev");
            }

            supportLinkLabel.LinkClicked += (_, e) =>
            {
                try
                {
                    if (e.Link != null &&
                        e.Link.LinkData is string url &&
                        !string.IsNullOrWhiteSpace(url))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                }
                catch
                {
                    MessageBox.Show(
                        this,
                        "Could not open the PayPal link.",
                        "Open link failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };

            var okButton = new Button
            {
                Text = "OK",
                Width = 90,
                Height = 32,
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            okButton.Click += (_, __) => Close();

            // Vertically center controls inside bottom panel
            Size supportSize = TextRenderer.MeasureText(
                supportLinkLabel.Text,
                supportLinkLabel.Font);

            int supportY = (bottomPanel.Height - supportSize.Height) / 2;
            supportLinkLabel.Location = new Point(20, supportY);

            int okY = (bottomPanel.Height - okButton.Height) / 2;
            okButton.Location = new Point(bottomPanel.Width - 110, okY);

            bottomPanel.Controls.Add(supportLinkLabel);
            bottomPanel.Controls.Add(okButton);

            Controls.Add(appNameLabel);
            Controls.Add(iconBox);
            Controls.Add(versionLabel);
            Controls.Add(authorLabel);
            Controls.Add(copyrightLabel);
            Controls.Add(bottomPanel);

            AcceptButton = okButton;
            CancelButton = okButton;
        }
    }
}