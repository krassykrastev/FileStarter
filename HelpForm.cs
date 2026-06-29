using System;
using System.Drawing;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "Help";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(880, 800);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            BackColor = Color.White;

            var bottomPanel = new Panel
            {
                Location = new Point(0, ClientSize.Height - 72),
                Size = new Size(ClientSize.Width, 72),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.White
            };

            var iconBox = new PictureBox
            {
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.White,
                Image = AppUiHelpers.TryLoadAppIconBitmap()
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
            int clickableStart = supportLinkLabel.Text.IndexOf(clickablePart, StringComparison.Ordinal);
            if (clickableStart >= 0)
            {
                supportLinkLabel.Links.Add(clickableStart, clickablePart.Length, "https://ko-fi.com/filestarter");
            }
            supportLinkLabel.LinkClicked += (_, e) =>
            {
                if (e.Link?.LinkData is string url && !string.IsNullOrWhiteSpace(url))
                {
                    AppUiHelpers.TryOpenUrl(this, url, "Could not open the Ko-fi link.", "Open link failed");
                }
            };

            var okButton = new Button
            {
                Text = "OK",
                Width = 90,
                Height = 32,
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                BackColor = Color.White
            };
            okButton.Click += (_, _) => Close();

            int iconY = (bottomPanel.Height - iconBox.Height) / 2;
            iconBox.Location = new Point(18, iconY);
            Size supportSize = TextRenderer.MeasureText(supportLinkLabel.Text, supportLinkLabel.Font);
            int supportY = (bottomPanel.Height - supportSize.Height) / 2;
            supportLinkLabel.Location = new Point(iconBox.Right + 16, supportY);
            int okY = (bottomPanel.Height - okButton.Height) / 2;
            okButton.Location = new Point(bottomPanel.Width - 110, okY);

            bottomPanel.Controls.Add(iconBox);
            bottomPanel.Controls.Add(supportLinkLabel);
            bottomPanel.Controls.Add(okButton);

            var helpTextBox = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Location = new Point(18, 18),
                Size = new Size(ClientSize.Width - 36, ClientSize.Height - bottomPanel.Height - 28),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                WordWrap = true,
                ShortcutsEnabled = false,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Default,
                BackColor = Color.White,
                Text =
@"FileStarter is a lightweight Windows tray application that automatically launches apps or files based on a configurable schedule. It is designed to be simple, reliable, and unobtrusive — perfect for automating your daily startup routine.    

• Tray icon behavior:    
  - Single click on the tray icon toggles Auto-start ON or OFF.    
  - Double click on the tray icon opens Settings.    
  - Right click on the tray icon opens the context menu.    

• Context menu options:    
  - Auto-start ON / OFF -> Enables or disables automatic launching.    
  - Run FileStarter on Windows startup -> Starts FileStarter automatically when you sign in to Windows.    
  - Start VPN first & reconnect on drops -> starts VPN on startup and auto-reconnect if the VPN drops.    
  - Settings -> Opens the Settings window where you can configure schedule and files.    
  - View log -> Opens the FileStarter log.  
  - Suggestions / bugs -> Leads to web form to submit a suggestion or report a bug.  
  - Check for new version -> Leads to FileStarter release web page.    
  - Help -> Opens this Help window.    
  - About -> Shows version and author information.    
  - Exit -> Closes FileStarter.      

• What you can control in Settings:    
  - Auto-start schedule -> Choose the days of the week and the launch time for each day.    
  - Select file(s) to launch -> Choose up to 4 files or applications to launch.    
  - File 1 / File 2 'Default' buttons -> Quickly restore File 1 to MS Teams and File 2 to MS Outlook.    
  - File 3 / File 4 'Clear' buttons -> Clear the selected file and uncheck that file row.    
  - Vacation mode -> Temporarily turn auto-start OFF from a future date. Optionally set an end date using “Turn auto-start OFF until this date”."
            };

            Controls.Add(helpTextBox);
            Controls.Add(bottomPanel);
            AcceptButton = okButton;
        }
    }
}