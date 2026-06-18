using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class LogViewerForm : Form
    {
        private const int TopPanelHeight = 62;
        private const int PanelPadding = 10;
        private const int LabelWidth = 62;
        private const int ToggleHeight = 36;
        private const int ActionButtonWidth = 124;
        private const int ActionButtonHeight = 36;
        private const int ActionButtonMarginRight = 8;
        private const int FilterButtonMarginRight = 6;
        private const int RefreshIntervalMs = 300;
        private const int InitialLoadIntervalMs = 1;

        private static readonly Color ActionTextColor = Color.FromArgb(45, 45, 45);
        private static readonly Color LabelTextColor = Color.FromArgb(70, 70, 70);
        private static readonly Color ToggleCheckedBackColor = Color.FromArgb(232, 239, 255);
        private static readonly Color ToggleCheckedForeColor = Color.FromArgb(41, 70, 135);
        private static readonly Color ToggleCheckedBorderColor = Color.FromArgb(181, 198, 236);
        private static readonly Color ToggleUncheckedForeColor = Color.FromArgb(80, 80, 80);
        private static readonly Color BorderColor = Color.FromArgb(214, 219, 226);
        private static readonly Color MouseDownBackColor = Color.FromArgb(235, 238, 242);
        private static readonly Color MouseOverBackColor = Color.FromArgb(245, 247, 250);

        private readonly Panel _topPanel;
        private readonly Panel _contentPanel;
        private readonly FlowLayoutPanel _actionsPanel;
        private readonly FlowLayoutPanel _filtersPanel;
        private readonly Label _showLabel;
        private readonly Button _clearLogButton;
        private readonly Button _exportLogButton;
        private readonly CheckBox _otherFilterButton;
        private readonly CheckBox _changeFilterButton;
        private readonly RichTextBox _logRichTextBox;
        private readonly FileSystemWatcher _fileWatcher;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _initialLoadTimer;
        private bool _refreshPending;
        private bool _hasLoadedOnce;

        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmLineScroll = 0x00B6;
        private const int WmSetRedraw = 0x000B;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public LogViewerForm()
        {
            Text = "FileStarter Log";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 620);
            Size = new Size(1100, 760);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            BackColor = Color.White;
            ShowInTaskbar = false;
            ShowIcon = false;
            MinimizeBox = false;

            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = TopPanelHeight,
                BackColor = Color.White,
                Padding = new Padding(PanelPadding)
            };
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            _actionsPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Left,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            _filtersPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Right,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _clearLogButton = CreateActionButton("Clear log", (_, _) => ClearLog());
            _exportLogButton = CreateActionButton("Export log", (_, _) => ExportLog());

            _showLabel = new Label
            {
                AutoSize = false,
                Width = LabelWidth,
                Height = ToggleHeight,
                Text = "Show:",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = LabelTextColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 0, 4, 0),
                UseCompatibleTextRendering = false
            };

            _otherFilterButton = CreateFilterToggle("Other", 126);
            _changeFilterButton = CreateFilterToggle("Changes", 132);

            _actionsPanel.Controls.Add(_clearLogButton);
            _actionsPanel.Controls.Add(_exportLogButton);
            _filtersPanel.Controls.Add(_showLabel);
            _filtersPanel.Controls.Add(_otherFilterButton);
            _filtersPanel.Controls.Add(_changeFilterButton);
            _topPanel.Controls.Add(_actionsPanel);
            _topPanel.Controls.Add(_filtersPanel);

            _logRichTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                DetectUrls = false,
                HideSelection = false,
                WordWrap = false,
                Multiline = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = true
            };
            _contentPanel.Controls.Add(_logRichTextBox);

            Controls.Add(_contentPanel);
            Controls.Add(_topPanel);

            _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
            _refreshTimer.Tick += (_, _) =>
            {
                _refreshTimer.Stop();
                if (_refreshPending)
                {
                    _refreshPending = false;
                    LoadAndRenderLog();
                }
            };

            _initialLoadTimer = new System.Windows.Forms.Timer { Interval = InitialLoadIntervalMs };
            _initialLoadTimer.Tick += (_, _) =>
            {
                _initialLoadTimer.Stop();
                LoadAndRenderLog();
                _hasLoadedOnce = true;
            };

            string directory = Path.GetDirectoryName(Logger.LogFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string fileName = Path.GetFileName(Logger.LogFilePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "app.log";

            Directory.CreateDirectory(directory);
            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };
            _fileWatcher.Changed += (_, _) => ScheduleRefresh();
            _fileWatcher.Created += (_, _) => ScheduleRefresh();
            _fileWatcher.Deleted += (_, _) => ScheduleRefresh();
            _fileWatcher.Renamed += (_, _) => ScheduleRefresh();

            Resize += (_, _) => LayoutTopPanels();
            Shown += (_, _) => _initialLoadTimer.Start();
            FormClosed += (_, _) => DisposeResources();
            LayoutTopPanels();
        }

        private Button CreateActionButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Width = ActionButtonWidth,
                Height = ActionButtonHeight,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = ActionTextColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 0, ActionButtonMarginRight, 0),
                Cursor = Cursors.Hand,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = true
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = MouseDownBackColor;
            button.FlatAppearance.MouseOverBackColor = MouseOverBackColor;
            button.Click += onClick;
            return button;
        }

        private CheckBox CreateFilterToggle(string text, int width)
        {
            var check = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoSize = false,
                Width = width,
                Height = ToggleHeight,
                Text = string.Empty,
                FlatStyle = FlatStyle.Flat,
                Checked = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Margin = new Padding(0, 0, FilterButtonMarginRight, 0),
                Cursor = Cursors.Hand,
                TabStop = false,
                UseCompatibleTextRendering = true,
                Tag = text
            };
            check.FlatAppearance.BorderSize = 1;
            check.Paint += FilterToggle_Paint;
            check.CheckedChanged += (_, _) =>
            {
                ApplyFilterStyle(check);
                check.Invalidate();
                LoadAndRenderLog();
            };
            ApplyFilterStyle(check);
            return check;
        }

        private void FilterToggle_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not CheckBox check)
                return;

            string text = check.Tag?.ToString() ?? string.Empty;
            const int reservedLeftWidth = 28;
            const int reservedRightWidth = 28;
            const int checkGlyphWidth = 12;
            var checkRect = new Rectangle(reservedLeftWidth - checkGlyphWidth, 0, checkGlyphWidth, check.ClientSize.Height);
            var textRect = new Rectangle(reservedLeftWidth, 0, check.ClientSize.Width - reservedLeftWidth - reservedRightWidth, check.ClientSize.Height);

            if (check.Checked)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "✓",
                    check.Font,
                    checkRect,
                    check.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                check.Font,
                textRect,
                check.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        private static void ApplyFilterStyle(CheckBox check)
        {
            if (check.Checked)
            {
                check.BackColor = ToggleCheckedBackColor;
                check.ForeColor = ToggleCheckedForeColor;
                check.FlatAppearance.BorderColor = ToggleCheckedBorderColor;
            }
            else
            {
                check.BackColor = Color.White;
                check.ForeColor = ToggleUncheckedForeColor;
                check.FlatAppearance.BorderColor = BorderColor;
            }
        }

        private void LayoutTopPanels()
        {
            _actionsPanel.Location = new Point(PanelPadding, PanelPadding);
            _filtersPanel.Location = new Point(Math.Max(PanelPadding, ClientSize.Width - _filtersPanel.PreferredSize.Width - 16), PanelPadding);
            _showLabel.Top = Math.Max(0, (_otherFilterButton.Height - _showLabel.Height) / 2);
        }

        private void ScheduleRefresh()
        {
            if (IsDisposed)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _refreshPending = true;
                    _refreshTimer.Stop();
                    _refreshTimer.Start();
                }));
            }
            catch
            {
            }
        }

        private void LoadAndRenderLog()
        {
            int firstVisibleLine = 0;
            int selectionStart = 0;
            bool preserveView = _hasLoadedOnce && _logRichTextBox.IsHandleCreated;
            if (preserveView)
            {
                firstVisibleLine = GetFirstVisibleLine();
                selectionStart = _logRichTextBox.SelectionStart;
            }
            RenderEntries(Logger.ReadEntries(), preserveView, firstVisibleLine, selectionStart);
        }

        private int GetFirstVisibleLine()
            => (int)SendMessage(_logRichTextBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);

        private void RestoreFirstVisibleLine(int targetLine)
        {
            int delta = targetLine - GetFirstVisibleLine();
            if (delta != 0)
                SendMessage(_logRichTextBox.Handle, EmLineScroll, IntPtr.Zero, (IntPtr)delta);
        }

        private void RenderEntries(List<Logger.ParsedLogEntry> entries, bool preserveView, int firstVisibleLine, int selectionStart)
        {
            SendMessage(_logRichTextBox.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            _logRichTextBox.SuspendLayout();
            _logRichTextBox.Clear();

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var kind = GetKind(entry.Level);
                if (!ShouldShow(kind))
                    continue;

                Color kindColor = GetColor(kind);
                _logRichTextBox.SelectionColor = kindColor;
                _logRichTextBox.AppendText(GetEmoji(kind) + " ");
                _logRichTextBox.SelectionColor = Color.DimGray;
                _logRichTextBox.AppendText(entry.Timestamp + "  ");
                AppendMessageWithHighlights(entry.Message, kindColor);
                _logRichTextBox.AppendText(Environment.NewLine);
            }

            _logRichTextBox.SelectionStart = preserveView ? Math.Min(selectionStart, _logRichTextBox.TextLength) : 0;
            _logRichTextBox.SelectionLength = 0;

            if (preserveView)
                RestoreFirstVisibleLine(firstVisibleLine);

            _logRichTextBox.ResumeLayout();
            SendMessage(_logRichTextBox.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            _logRichTextBox.Invalidate();
            _logRichTextBox.Update();
        }

        private void AppendMessageWithHighlights(string message, Color defaultColor)
        {
            if (string.IsNullOrEmpty(message))
                return;

            int index = 0;
            while (index < message.Length)
            {
                int nextOn = message.IndexOf("ON", index, StringComparison.Ordinal);
                int nextOff = message.IndexOf("OFF", index, StringComparison.Ordinal);
                if (nextOn < 0 && nextOff < 0)
                {
                    _logRichTextBox.SelectionColor = defaultColor;
                    _logRichTextBox.AppendText(message[index..]);
                    break;
                }

                bool useOn = nextOn >= 0 && (nextOff < 0 || nextOn < nextOff);
                int tokenIndex = useOn ? nextOn : nextOff;
                string token = useOn ? "ON" : "OFF";
                Color tokenColor = useOn ? Color.FromArgb(22, 101, 52) : Color.Crimson;

                if (tokenIndex > index)
                {
                    _logRichTextBox.SelectionColor = defaultColor;
                    _logRichTextBox.AppendText(message.Substring(index, tokenIndex - index));
                }

                _logRichTextBox.SelectionColor = tokenColor;
                _logRichTextBox.AppendText(token);
                index = tokenIndex + token.Length;
            }
        }

        private bool ShouldShow(LogEntryKind kind)
            => kind switch
            {
                LogEntryKind.Other => _otherFilterButton.Checked,
                LogEntryKind.Change => _changeFilterButton.Checked,
                _ => false
            };

        private static LogEntryKind GetKind(string level)
            => string.Equals(level, "CHANGE", StringComparison.OrdinalIgnoreCase) ? LogEntryKind.Change : LogEntryKind.Other;

        private static string GetEmoji(LogEntryKind kind)
            => kind switch
            {
                LogEntryKind.Other => "❗",
                LogEntryKind.Change => "🔄",
                _ => "•"
            };

        private static Color GetColor(LogEntryKind kind)
            => kind switch
            {
                LogEntryKind.Other => Color.Crimson,
                LogEntryKind.Change => Color.RoyalBlue,
                _ => Color.Black
            };

        private void ClearLog()
        {
            Logger.Clear();
            _hasLoadedOnce = false;
            LoadAndRenderLog();
            _hasLoadedOnce = true;
        }

        private void ExportLog()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export log",
                FileName = "FileStarter log.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var sb = new StringBuilder();
            var entries = Logger.ReadEntries();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var kind = GetKind(entry.Level);
                if (!ShouldShow(kind))
                    continue;

                sb.Append(GetEmoji(kind)).Append(' ')
                  .Append(entry.Timestamp).Append("  ")
                  .AppendLine(entry.Message);
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        }

        private void DisposeResources()
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _initialLoadTimer.Stop();
            _initialLoadTimer.Dispose();
        }

        private enum LogEntryKind
        {
            Other,
            Change
        }
    }
}