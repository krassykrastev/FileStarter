using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class SettingsForm : Form
    {
        private readonly Label _daysHeaderLabel;
        private readonly CheckBox _monCheckBox;
        private readonly CheckBox _tueCheckBox;
        private readonly CheckBox _wedCheckBox;
        private readonly CheckBox _thuCheckBox;
        private readonly CheckBox _friCheckBox;
        private readonly CheckBox _satCheckBox;
        private readonly CheckBox _sunCheckBox;
        private readonly DateTimePicker _monTimePicker;
        private readonly DateTimePicker _tueTimePicker;
        private readonly DateTimePicker _wedTimePicker;
        private readonly DateTimePicker _thuTimePicker;
        private readonly DateTimePicker _friTimePicker;
        private readonly DateTimePicker _satTimePicker;
        private readonly DateTimePicker _sunTimePicker;
        private readonly Label _vacationModeLabel;
        private readonly CheckBox _autoStartOffFromCheckBox;
        private readonly DateTimePicker _autoStartOffFromDatePicker;
        private readonly CheckBox _autoStartOffUntilCheckBox;
        private readonly DateTimePicker _autoStartOffUntilDatePicker;
        private readonly CheckBox _file1CheckBox;
        private readonly CheckBox _file2CheckBox;
        private readonly CheckBox _file3CheckBox;
        private readonly CheckBox _file4CheckBox;
        private readonly TextBox _file1TextBox;
        private readonly TextBox _file2TextBox;
        private readonly TextBox _file3TextBox;
        private readonly TextBox _file4TextBox;
        private readonly Button _file1BrowseButton;
        private readonly Button _file2BrowseButton;
        private readonly Button _file3BrowseButton;
        private readonly Button _file4BrowseButton;
        private readonly Button _file1DefaultButton;
        private readonly Button _file2DefaultButton;
        private readonly Button _file3ClearButton;
        private readonly Button _file4ClearButton;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private string? _file1Path;
        private string? _file2Path;
        private string? _file3Path;
        private string? _file4Path;

        public bool Accepted { get; private set; } = false;
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
            _file1Path = current.File1Path;
            _file2Path = current.File2Path;
            _file3Path = current.File3Path;
            _file4Path = current.File4Path;

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

            (_monCheckBox, _monTimePicker) = CreateDayRow("Mon", dayCheckX, dayLabelX, dayTimeX, dayRowStartY, current.Mon);
            (_tueCheckBox, _tueTimePicker) = CreateDayRow("Tue", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap, current.Tue);
            (_wedCheckBox, _wedTimePicker) = CreateDayRow("Wed", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 2, current.Wed);
            (_thuCheckBox, _thuTimePicker) = CreateDayRow("Thu", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 3, current.Thu);
            (_friCheckBox, _friTimePicker) = CreateDayRow("Fri", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 4, current.Fri);
            (_satCheckBox, _satTimePicker) = CreateDayRow("Sat", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 5, current.Sat);
            (_sunCheckBox, _sunTimePicker) = CreateDayRow("Sun", dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 6, current.Sun);

            WireDayRow(_monCheckBox, _monTimePicker);
            WireDayRow(_tueCheckBox, _tueTimePicker);
            WireDayRow(_wedCheckBox, _wedTimePicker);
            WireDayRow(_thuCheckBox, _thuTimePicker);
            WireDayRow(_friCheckBox, _friTimePicker);
            WireDayRow(_satCheckBox, _satTimePicker);
            WireDayRow(_sunCheckBox, _sunTimePicker);

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
            (_file1CheckBox, _file1TextBox, _file1BrowseButton) = CreateFileRow(1, "MS Teams", _file1Path, current.File1Enabled, fileRowY, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth);
            (_file2CheckBox, _file2TextBox, _file2BrowseButton) = CreateFileRow(2, "MS Outlook", _file2Path, current.File2Enabled, fileRowY + fileRowGap, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth);
            (_file3CheckBox, _file3TextBox, _file3BrowseButton) = CreateFileRow(3, "not yet selected", _file3Path, current.File3Enabled, fileRowY + fileRowGap * 2, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth);
            (_file4CheckBox, _file4TextBox, _file4BrowseButton) = CreateFileRow(4, "not yet selected", _file4Path, current.File4Enabled, fileRowY + fileRowGap * 3, fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth);

            _file1DefaultButton = CreateSecondaryButton("Default", fileActionX, fileRowY);
            _file2DefaultButton = CreateSecondaryButton("Default", fileActionX, fileRowY + fileRowGap);
            _file3ClearButton = CreateSecondaryButton("Clear", fileActionX, fileRowY + fileRowGap * 2);
            _file4ClearButton = CreateSecondaryButton("Clear", fileActionX, fileRowY + fileRowGap * 3);

            _file1DefaultButton.Width = fileActionWidth;
            _file2DefaultButton.Width = fileActionWidth;
            _file3ClearButton.Width = fileActionWidth;
            _file4ClearButton.Width = fileActionWidth;

            _file1DefaultButton.Click += (_, __) => RestoreFile1Default();
            _file2DefaultButton.Click += (_, __) => RestoreFile2Default();
            _file3ClearButton.Click += (_, __) => ClearFile3();
            _file4ClearButton.Click += (_, __) => ClearFile4();

            Controls.Add(_file1DefaultButton);
            Controls.Add(_file2DefaultButton);
            Controls.Add(_file3ClearButton);
            Controls.Add(_file4ClearButton);

            _file1CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;
            _file2CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;
            _file3CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;
            _file4CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;

            WireFileRow(_file1CheckBox, _file1TextBox, _file1BrowseButton);
            WireFileRow(_file2CheckBox, _file2TextBox, _file2BrowseButton);
            WireFileRow(_file3CheckBox, _file3TextBox, _file3BrowseButton);
            WireFileRow(_file4CheckBox, _file4TextBox, _file4BrowseButton);

            _file1BrowseButton.Click += (_, __) => BrowseForFile(1);
            _file2BrowseButton.Click += (_, __) => BrowseForFile(2);
            _file3BrowseButton.Click += (_, __) => BrowseForFile(3);
            _file4BrowseButton.Click += (_, __) => BrowseForFile(4);

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

            int btnY = ClientSize.Height - buttonHeight - bottomMargin;
            _okButton = new Button
            {
                Text = "OK",
                Width = buttonWidth,
                Height = buttonHeight,
                Location = new Point(leftMargin, btnY),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.System
            };
            _okButton.Click += (_, __) => OnOk();

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = buttonWidth,
                Height = buttonHeight,
                Location = new Point(ClientSize.Width - leftMargin - buttonWidth, btnY),
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

        private (CheckBox checkBox, DateTimePicker timePicker) CreateDayRow(string dayAbbrev, int checkX, int dayX, int timeX, int y, DayLaunchSetting currentDaySetting)
        {
            var cb = new CheckBox
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
                Location = new Point(timeX, y)
            };
            if (TimeSpan.TryParse(currentDaySetting.Time, out var t))
                timePicker.Value = DateTime.Today.Add(t);
            else
                timePicker.Value = DateTime.Today.AddHours(9);
            Controls.Add(cb);
            Controls.Add(dayLabel);
            Controls.Add(timePicker);
            return (cb, timePicker);
        }

        private void WireDayRow(CheckBox checkBox, DateTimePicker timePicker)
        {
            timePicker.Enabled = checkBox.Checked;
            timePicker.CalendarMonthBackground = Color.White;
            checkBox.CheckedChanged += (_, __) => { timePicker.Enabled = checkBox.Checked; };
        }

        private void WireFutureOffControls()
        {
            _autoStartOffFromDatePicker.Enabled = _autoStartOffFromCheckBox.Checked;
            _autoStartOffUntilCheckBox.Enabled = _autoStartOffFromCheckBox.Checked;
            _autoStartOffUntilDatePicker.Enabled = _autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked;
            _autoStartOffCheckBox_CheckedChanged(null, EventArgs.Empty);
            _autoStartOffFromCheckBox.CheckedChanged += _autoStartOffCheckBox_CheckedChanged;
            _autoStartOffUntilCheckBox.CheckedChanged += (_, __) =>
            {
                _autoStartOffUntilDatePicker.Enabled = _autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked;
            };
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
            _autoStartOffFromDatePicker.Enabled = _autoStartOffFromCheckBox.Checked;
            _autoStartOffUntilCheckBox.Enabled = _autoStartOffFromCheckBox.Checked;
            _autoStartOffUntilDatePicker.Enabled = _autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked;
        }

        private void WireFileRow(CheckBox checkBox, TextBox textBox, Button browseButton)
        {
            textBox.Enabled = checkBox.Checked;
            browseButton.Enabled = true;
            checkBox.CheckedChanged += (_, __) => { textBox.Enabled = checkBox.Checked; };
        }

        private (CheckBox checkBox, TextBox textBox, Button browseButton) CreateFileRow(int fileIndex, string defaultText, string? path, bool isChecked, int y, int checkX, int textX, int textWidth, int browseX, int browseWidth)
        {
            var cb = new CheckBox
            {
                AutoSize = true,
                Location = new Point(checkX, y + 4),
                Checked = isChecked
            };
            var tb = new TextBox
            {
                ReadOnly = true,
                TabStop = false,
                Width = textWidth,
                Location = new Point(textX, y + 4),
                BackColor = BackColor,
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.Default,
                Text = SettingsManager.GetFileLineText(fileIndex, path, defaultText)
            };
            var browse = new Button
            {
                Text = "Browse...",
                Width = browseWidth,
                Height = 32,
                Location = new Point(browseX, y),
                FlatStyle = FlatStyle.System
            };
            Controls.Add(cb);
            Controls.Add(tb);
            Controls.Add(browse);
            return (cb, tb, browse);
        }

        private Button CreateSecondaryButton(string text, int x, int y)
        {
            return new Button
            {
                Text = text,
                Height = 32,
                Location = new Point(x, y),
                FlatStyle = FlatStyle.System
            };
        }

        private void BrowseForFile(int fileIndex)
        {
            using var dlg = new OpenFileDialog
            {
                Title = $"Select file for File {fileIndex}",
                Filter = "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return;
            switch (fileIndex)
            {
                case 1:
                    _file1Path = dlg.FileName;
                    _file1TextBox.Text = SettingsManager.GetFileLineText(1, _file1Path, "MS Teams");
                    break;
                case 2:
                    _file2Path = dlg.FileName;
                    _file2TextBox.Text = SettingsManager.GetFileLineText(2, _file2Path, "MS Outlook");
                    break;
                case 3:
                    _file3Path = dlg.FileName;
                    _file3TextBox.Text = SettingsManager.GetFileLineText(3, _file3Path, "not yet selected");
                    break;
                case 4:
                    _file4Path = dlg.FileName;
                    _file4TextBox.Text = SettingsManager.GetFileLineText(4, _file4Path, "not yet selected");
                    break;
            }
            UpdateFileActionButtonStates();
        }

        private void RestoreFile1Default()
        {
            _file1Path = null;
            _file1TextBox.Text = SettingsManager.GetFileLineText(1, _file1Path, "MS Teams");
            UpdateFileActionButtonStates();
        }

        private void RestoreFile2Default()
        {
            _file2Path = null;
            _file2TextBox.Text = SettingsManager.GetFileLineText(2, _file2Path, "MS Outlook");
            UpdateFileActionButtonStates();
        }

        private void ClearFile3()
        {
            _file3Path = null;
            _file3TextBox.Text = SettingsManager.GetFileLineText(3, _file3Path, "not yet selected");
            _file3CheckBox.Checked = false;
            UpdateFileActionButtonStates();
        }

        private void ClearFile4()
        {
            _file4Path = null;
            _file4TextBox.Text = SettingsManager.GetFileLineText(4, _file4Path, "not yet selected");
            _file4CheckBox.Checked = false;
            UpdateFileActionButtonStates();
        }

        private void UpdateFileActionButtonStates()
        {
            _file1DefaultButton.Enabled = !string.IsNullOrWhiteSpace(_file1Path);
            _file2DefaultButton.Enabled = !string.IsNullOrWhiteSpace(_file2Path);
            _file3ClearButton.Enabled = !string.IsNullOrWhiteSpace(_file3Path);
            _file4ClearButton.Enabled = !string.IsNullOrWhiteSpace(_file4Path);
        }

        private void FileCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox changedCheckBox)
            {
                EnsureAtLeastOneFileChecked(changedCheckBox);
            }
        }

        private void EnsureAtLeastOneFileChecked(CheckBox changedCheckBox)
        {
            int checkedCount =
                (_file1CheckBox.Checked ? 1 : 0) +
                (_file2CheckBox.Checked ? 1 : 0) +
                (_file3CheckBox.Checked ? 1 : 0) +
                (_file4CheckBox.Checked ? 1 : 0);
            if (checkedCount == 0)
            {
                changedCheckBox.Checked = true;
            }
        }

        private bool ValidateCustomPathExists(string slotName, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            if (File.Exists(path))
                return true;

            MessageBox.Show(
                this,
                $"{slotName} has a custom path selected, but the file no longer exists. Please browse for a valid file or restore the default path.",
                $"Missing {slotName} file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private void OnOk()
        {
            if (_file3CheckBox.Checked && string.IsNullOrWhiteSpace(_file3Path))
            {
                MessageBox.Show(this, "File 3 is selected, but no file has been chosen. Please browse for a file or uncheck File 3.", "Missing File 3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_file4CheckBox.Checked && string.IsNullOrWhiteSpace(_file4Path))
            {
                MessageBox.Show(this, "File 4 is selected, but no file has been chosen. Please browse for a file or uncheck File 4.", "Missing File 4", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateCustomPathExists("File 1", _file1Path))
                return;
            if (!ValidateCustomPathExists("File 2", _file2Path))
                return;
            if (!ValidateCustomPathExists("File 3", _file3Path))
                return;
            if (!ValidateCustomPathExists("File 4", _file4Path))
                return;

            if (_autoStartOffFromCheckBox.Checked)
            {
                if (_autoStartOffFromDatePicker.Value.Date < DateTime.Today)
                {
                    MessageBox.Show(this, "The start date cannot be in the past.", "Invalid start date", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_autoStartOffUntilCheckBox.Checked)
                {
                    if (_autoStartOffUntilDatePicker.Value.Date < _autoStartOffFromDatePicker.Value.Date)
                    {
                        MessageBox.Show(this, "The 'Turn auto-start OFF until' date cannot be earlier than the 'from' date.", "Invalid end date", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            MonEnabled = _monCheckBox.Checked;
            TueEnabled = _tueCheckBox.Checked;
            WedEnabled = _wedCheckBox.Checked;
            ThuEnabled = _thuCheckBox.Checked;
            FriEnabled = _friCheckBox.Checked;
            SatEnabled = _satCheckBox.Checked;
            SunEnabled = _sunCheckBox.Checked;
            MonTimeHHmm = _monTimePicker.Value.ToString("HH:mm");
            TueTimeHHmm = _tueTimePicker.Value.ToString("HH:mm");
            WedTimeHHmm = _wedTimePicker.Value.ToString("HH:mm");
            ThuTimeHHmm = _thuTimePicker.Value.ToString("HH:mm");
            FriTimeHHmm = _friTimePicker.Value.ToString("HH:mm");
            SatTimeHHmm = _satTimePicker.Value.ToString("HH:mm");
            SunTimeHHmm = _sunTimePicker.Value.ToString("HH:mm");
            AutoStartOffFromEnabled = _autoStartOffFromCheckBox.Checked;
            AutoStartOffFromDate = _autoStartOffFromCheckBox.Checked ? _autoStartOffFromDatePicker.Value.Date : null;
            AutoStartOffUntilEnabled = _autoStartOffUntilCheckBox.Checked;
            AutoStartOffUntilDate = (_autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked)
                ? _autoStartOffUntilDatePicker.Value.Date
                : null;
            File1Enabled = _file1CheckBox.Checked;
            File2Enabled = _file2CheckBox.Checked;
            File3Enabled = _file3CheckBox.Checked;
            File4Enabled = _file4CheckBox.Checked;
            File1Path = _file1Path;
            File2Path = _file2Path;
            File3Path = _file3Path;
            File4Path = _file4Path;
            Accepted = true;
            Close();
        }
    }
}