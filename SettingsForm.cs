using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class SettingsForm : Form
    {
        private readonly Label _daysHeaderLabel;
        private readonly Label _vacationModeLabel;
        private readonly CheckBox _autoStartOffFromCheckBox;
        private readonly DateTimePicker _autoStartOffFromDatePicker;
        private readonly CheckBox _autoStartOffUntilCheckBox;
        private readonly DateTimePicker _autoStartOffUntilDatePicker;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly DayRow[] _dayRows;
        private readonly FileRow[] _fileRows;

        public bool Accepted { get; private set; }
        public bool MonEnabled { get; private set; }
        public bool TueEnabled { get; private set; }
        public bool WedEnabled { get; private set; }
        public bool ThuEnabled { get; private set; }
        public bool FriEnabled { get; private set; }
        public bool SatEnabled { get; private set; }
        public bool SunEnabled { get; private set; }
        public string MonTimeHHmm { get; private set; } = "09:00";
        public string TueTimeHHmm { get; private set; } = "09:00";
        public string WedTimeHHmm { get; private set; } = "09:00";
        public string ThuTimeHHmm { get; private set; } = "09:00";
        public string FriTimeHHmm { get; private set; } = "09:00";
        public string SatTimeHHmm { get; private set; } = "09:00";
        public string SunTimeHHmm { get; private set; } = "09:00";
        public bool AutoStartOffFromEnabled { get; private set; }
        public DateTime? AutoStartOffFromDate { get; private set; }
        public bool AutoStartOffUntilEnabled { get; private set; }
        public DateTime? AutoStartOffUntilDate { get; private set; }
        public bool File1Enabled { get; private set; }
        public bool File2Enabled { get; private set; }
        public bool File3Enabled { get; private set; }
        public bool File4Enabled { get; private set; }
        public string? File1Path { get; private set; }
        public string? File2Path { get; private set; }
        public string? File3Path { get; private set; }
        public string? File4Path { get; private set; }

        public SettingsForm(AppSettings current)
        {
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            BackColor = Color.White;
            ClientSize = new Size(700, 400);
            SuspendLayout();

            const int leftMargin = 16;
            const int topHeaderY = 16;
            const int dayCheckX = 20;
            const int dayLabelX = 48;
            const int dayTimeX = 100;
            const int dayRowStartY = 52;
            const int dayRowGap = 36;
            const int rightColumnX = 240;
            const int rightHeaderY = topHeaderY;
            const int fileSectionLabelGap = 32;
            const int fileRowGap = 36;
            const int fileCheckX = rightColumnX;
            const int fileTextX = rightColumnX + 28;
            const int fileTextWidth = 180;
            const int fileBrowseX = 460;
            const int fileBrowseWidth = 100;
            const int fileActionX = 580;
            const int fileActionWidth = 100;
            const int futureOffDateWidth = 130;
            const int futureOffDateX = 550;
            const int vacationLabelY = 202;
            const int futureOffFromY = dayRowStartY + dayRowGap * 5;
            const int futureOffUntilY = dayRowStartY + dayRowGap * 6;
            const int buttonWidth = 100;
            const int buttonHeight = 36;
            const int bottomMargin = 16;

            _daysHeaderLabel = new Label
            {
                Text = "Auto-start schedule",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(leftMargin, topHeaderY)
            };
            Controls.Add(_daysHeaderLabel);

            _dayRows = new[]
            {
                CreateDayRow("Mon", current.Mon, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 0),
                CreateDayRow("Tue", current.Tue, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 1),
                CreateDayRow("Wed", current.Wed, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 2),
                CreateDayRow("Thu", current.Thu, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 3),
                CreateDayRow("Fri", current.Fri, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 4),
                CreateDayRow("Sat", current.Sat, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 5),
                CreateDayRow("Sun", current.Sun, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 6)
            };

            var selectFilesLabel = new Label
            {
                Text = "Select file(s) to launch",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(rightColumnX, rightHeaderY)
            };
            Controls.Add(selectFilesLabel);

            int fileRowY = rightHeaderY + fileSectionLabelGap;
            _fileRows = new[]
            {
                CreateFileRow(1, current.File1Path, current.File1Enabled, fileRowY + fileRowGap * 0, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth, "Default"),
                CreateFileRow(2, current.File2Path, current.File2Enabled, fileRowY + fileRowGap * 1, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth, "Default"),
                CreateFileRow(3, current.File3Path, current.File3Enabled, fileRowY + fileRowGap * 2, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth, "Clear"),
                CreateFileRow(4, current.File4Path, current.File4Enabled, fileRowY + fileRowGap * 3, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth, "Clear")
            };

            foreach (var row in _fileRows)
            {
                row.CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;
                row.CheckBox.CheckedChanged += (_, __) => row.TextBox.Enabled = row.CheckBox.Checked;
                row.BrowseButton.Click += (_, __) => BrowseForFile(row);
                row.ActionButton.Click += (_, __) => ApplyRowAction(row);
            }
            UpdateFileActionButtonStates();

            _vacationModeLabel = new Label
            {
                Text = "Vacation mode",
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(rightColumnX, vacationLabelY)
            };
            Controls.Add(_vacationModeLabel);

            _autoStartOffFromCheckBox = new CheckBox
            {
                Text = "Turn auto-start OFF from this date",
                AutoSize = true,
                Location = new Point(rightColumnX, futureOffFromY + 4),
                Checked = current.AutoStartOffFromEnabled
            };
            Controls.Add(_autoStartOffFromCheckBox);

            DateTime initialFromDate = current.AutoStartOffFromDate.HasValue && current.AutoStartOffFromDate.Value.Date >= DateTime.Today
                ? current.AutoStartOffFromDate.Value.Date
                : DateTime.Today;
            _autoStartOffFromDatePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Width = futureOffDateWidth,
                Location = new Point(futureOffDateX, futureOffFromY),
                MinDate = DateTime.Today,
                Value = initialFromDate,
                Enabled = current.AutoStartOffFromEnabled
            };
            Controls.Add(_autoStartOffFromDatePicker);

            _autoStartOffUntilCheckBox = new CheckBox
            {
                Text = "Turn auto-start OFF until this date",
                AutoSize = true,
                Location = new Point(rightColumnX, futureOffUntilY + 4),
                Checked = current.AutoStartOffUntilEnabled
            };
            Controls.Add(_autoStartOffUntilCheckBox);

            DateTime initialUntilMinDate = initialFromDate;
            _autoStartOffUntilDatePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Width = futureOffDateWidth,
                Location = new Point(futureOffDateX, futureOffUntilY),
                MinDate = initialUntilMinDate,
                Value = current.AutoStartOffUntilDate.HasValue && current.AutoStartOffUntilDate.Value.Date >= initialUntilMinDate
                    ? current.AutoStartOffUntilDate.Value.Date
                    : initialUntilMinDate,
                Enabled = current.AutoStartOffFromEnabled && current.AutoStartOffUntilEnabled
            };
            Controls.Add(_autoStartOffUntilDatePicker);
            WireFutureOffControls();

            int buttonY = ClientSize.Height - buttonHeight - bottomMargin;
            _okButton = new Button
            {
                Text = "OK",
                Width = buttonWidth,
                Height = buttonHeight,
                Location = new Point(leftMargin, buttonY),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.System
            };
            _okButton.Click += (_, __) => OnOk();

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = buttonWidth,
                Height = buttonHeight,
                Location = new Point(ClientSize.Width - leftMargin - buttonWidth, buttonY),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.System
            };
            _cancelButton.Click += (_, __) => Close();

            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            ResumeLayout(false);
            PerformLayout();
        }

        private DayRow CreateDayRow(string dayAbbrev, DayLaunchSetting currentDaySetting, int checkX, int dayX, int timeX, int y)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(checkX, y + 4),
                Checked = currentDaySetting.Enabled
            };
            var dayLabel = new Label
            {
                Text = dayAbbrev,
                AutoSize = true,
                Location = new Point(dayX, y + 6)
            };
            var timePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Width = 80,
                Location = new Point(timeX, y),
                Enabled = currentDaySetting.Enabled,
                CalendarMonthBackground = Color.White
            };
            timePicker.Value = TimeSpan.TryParse(currentDaySetting.Time, out var time)
                ? DateTime.Today.Add(time)
                : DateTime.Today.AddHours(9);
            checkBox.CheckedChanged += (_, __) => timePicker.Enabled = checkBox.Checked;

            Controls.Add(checkBox);
            Controls.Add(dayLabel);
            Controls.Add(timePicker);
            return new DayRow(dayAbbrev, checkBox, timePicker);
        }

        private FileRow CreateFileRow(int slotIndex, string? path, bool isChecked, int y, int checkX, int textX, int textWidth, int browseX, int browseWidth, int actionX, int actionWidth, string actionText)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(checkX, y + 4),
                Checked = isChecked
            };
            var textBox = new TextBox
            {
                ReadOnly = true,
                TabStop = false,
                Width = textWidth,
                Location = new Point(textX, y + 4),
                BackColor = BackColor,
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.Default,
                Text = SettingsManager.GetFileLineText(slotIndex, path),
                Enabled = isChecked
            };
            var browseButton = new Button
            {
                Text = "Browse...",
                Width = browseWidth,
                Height = 32,
                Location = new Point(browseX, y),
                FlatStyle = FlatStyle.System
            };
            var actionButton = new Button
            {
                Text = actionText,
                Width = actionWidth,
                Height = 32,
                Location = new Point(actionX, y),
                FlatStyle = FlatStyle.System
            };

            Controls.Add(checkBox);
            Controls.Add(textBox);
            Controls.Add(browseButton);
            Controls.Add(actionButton);

            return new FileRow(slotIndex, checkBox, textBox, browseButton, actionButton, actionText, path);
        }

        private void WireFutureOffControls()
        {
            _autoStartOffCheckBox_CheckedChanged(null, EventArgs.Empty);
            _autoStartOffFromCheckBox.CheckedChanged += _autoStartOffCheckBox_CheckedChanged;
            _autoStartOffUntilCheckBox.CheckedChanged += (_, __) =>
                _autoStartOffUntilDatePicker.Enabled = _autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked;
            _autoStartOffFromDatePicker.ValueChanged += (_, __) =>
            {
                _autoStartOffUntilDatePicker.MinDate = _autoStartOffFromDatePicker.Value.Date;
                if (_autoStartOffUntilDatePicker.Value.Date < _autoStartOffFromDatePicker.Value.Date)
                {
                    _autoStartOffUntilDatePicker.Value = _autoStartOffFromDatePicker.Value.Date;
                }
            };
        }

        private void _autoStartOffCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            bool fromChecked = _autoStartOffFromCheckBox.Checked;
            _autoStartOffFromDatePicker.Enabled = fromChecked;
            if (!fromChecked)
            {
                _autoStartOffUntilCheckBox.Checked = false;
            }
            _autoStartOffUntilCheckBox.Enabled = fromChecked;
            _autoStartOffUntilDatePicker.Enabled = fromChecked && _autoStartOffUntilCheckBox.Checked;
        }

        private void BrowseForFile(FileRow row)
        {
            using var dialog = new OpenFileDialog
            {
                Title = $"Select file for {SettingsManager.GetSlotLabel(row.SlotIndex)}",
                Filter = "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            row.Path = dialog.FileName;
            row.TextBox.Text = SettingsManager.GetFileLineText(row.SlotIndex, row.Path);
            UpdateFileActionButtonStates();
        }

        private void ApplyRowAction(FileRow row)
        {
            row.Path = null;
            row.TextBox.Text = SettingsManager.GetFileLineText(row.SlotIndex, row.Path);
            if (!row.IsDefaultAction)
            {
                row.CheckBox.Checked = false;
            }
            UpdateFileActionButtonStates();
        }

        private void UpdateFileActionButtonStates()
        {
            foreach (var row in _fileRows)
            {
                row.ActionButton.Enabled = !string.IsNullOrWhiteSpace(row.Path);
            }
        }

        private void FileCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is not CheckBox changedCheckBox)
                return;

            int checkedCount = 0;
            foreach (var row in _fileRows)
            {
                if (row.CheckBox.Checked)
                    checkedCount++;
            }

            if (checkedCount == 0)
            {
                changedCheckBox.Checked = true;
            }
        }

        private bool ValidateCustomPathExists(FileRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Path) || File.Exists(row.Path))
                return true;

            string slotLabel = SettingsManager.GetSlotLabel(row.SlotIndex);
            MessageBox.Show(
                this,
                $"{slotLabel} has a custom path selected, but the file no longer exists. Please browse for a valid file or restore the default path.",
                $"Missing {slotLabel} file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private void OnOk()
        {
            foreach (var row in _fileRows)
            {
                if (row.SlotIndex >= 3 && row.CheckBox.Checked && string.IsNullOrWhiteSpace(row.Path))
                {
                    string slotLabel = SettingsManager.GetSlotLabel(row.SlotIndex);
                    MessageBox.Show(
                        this,
                        $"{slotLabel} is selected, but no file has been chosen. Please browse for a file or uncheck {slotLabel}.",
                        $"Missing {slotLabel}",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            foreach (var row in _fileRows)
            {
                if (!ValidateCustomPathExists(row))
                    return;
            }

            if (_autoStartOffFromCheckBox.Checked)
            {
                if (_autoStartOffFromDatePicker.Value.Date < DateTime.Today)
                {
                    MessageBox.Show(this, "The start date cannot be in the past.", "Invalid start date", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_autoStartOffUntilCheckBox.Checked && _autoStartOffUntilDatePicker.Value.Date < _autoStartOffFromDatePicker.Value.Date)
                {
                    MessageBox.Show(this, "The 'Turn auto-start OFF until' date cannot be earlier than the 'from' date.", "Invalid end date", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            MonEnabled = _dayRows[0].CheckBox.Checked;
            TueEnabled = _dayRows[1].CheckBox.Checked;
            WedEnabled = _dayRows[2].CheckBox.Checked;
            ThuEnabled = _dayRows[3].CheckBox.Checked;
            FriEnabled = _dayRows[4].CheckBox.Checked;
            SatEnabled = _dayRows[5].CheckBox.Checked;
            SunEnabled = _dayRows[6].CheckBox.Checked;
            MonTimeHHmm = _dayRows[0].TimePicker.Value.ToString("HH:mm");
            TueTimeHHmm = _dayRows[1].TimePicker.Value.ToString("HH:mm");
            WedTimeHHmm = _dayRows[2].TimePicker.Value.ToString("HH:mm");
            ThuTimeHHmm = _dayRows[3].TimePicker.Value.ToString("HH:mm");
            FriTimeHHmm = _dayRows[4].TimePicker.Value.ToString("HH:mm");
            SatTimeHHmm = _dayRows[5].TimePicker.Value.ToString("HH:mm");
            SunTimeHHmm = _dayRows[6].TimePicker.Value.ToString("HH:mm");

            AutoStartOffFromEnabled = _autoStartOffFromCheckBox.Checked;
            AutoStartOffFromDate = _autoStartOffFromCheckBox.Checked ? _autoStartOffFromDatePicker.Value.Date : null;
            AutoStartOffUntilEnabled = _autoStartOffUntilCheckBox.Checked;
            AutoStartOffUntilDate = (_autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked)
                ? _autoStartOffUntilDatePicker.Value.Date
                : null;

            File1Enabled = _fileRows[0].CheckBox.Checked;
            File2Enabled = _fileRows[1].CheckBox.Checked;
            File3Enabled = _fileRows[2].CheckBox.Checked;
            File4Enabled = _fileRows[3].CheckBox.Checked;
            File1Path = _fileRows[0].Path;
            File2Path = _fileRows[1].Path;
            File3Path = _fileRows[2].Path;
            File4Path = _fileRows[3].Path;

            Accepted = true;
            Close();
        }

        private sealed class DayRow
        {
            public DayRow(string name, CheckBox checkBox, DateTimePicker timePicker)
            {
                Name = name;
                CheckBox = checkBox;
                TimePicker = timePicker;
            }

            public string Name { get; }
            public CheckBox CheckBox { get; }
            public DateTimePicker TimePicker { get; }
        }

        private sealed class FileRow
        {
            public FileRow(int slotIndex, CheckBox checkBox, TextBox textBox, Button browseButton, Button actionButton, string actionText, string? path)
            {
                SlotIndex = slotIndex;
                CheckBox = checkBox;
                TextBox = textBox;
                BrowseButton = browseButton;
                ActionButton = actionButton;
                ActionText = actionText;
                Path = path;
            }

            public int SlotIndex { get; }
            public CheckBox CheckBox { get; }
            public TextBox TextBox { get; }
            public Button BrowseButton { get; }
            public Button ActionButton { get; }
            public string ActionText { get; }
            public bool IsDefaultAction => string.Equals(ActionText, "Default", StringComparison.OrdinalIgnoreCase);
            public string? Path { get; set; }
        }
    }
}