using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class SettingsForm : Form
    {
        private static readonly Color EnabledTextColor = Color.Black;
        private static readonly Color DisabledTextColor = SystemColors.GrayText;
        private static readonly Font SectionHeaderFont = new("Segoe UI", 9.5F, FontStyle.Regular);
        private static readonly Color SectionHeaderColor = Color.FromArgb(32, 32, 32);

        private readonly AppSettings _editedSettings;
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
        public AppSettings? ResultSettings { get; private set; }

        public SettingsForm(AppSettings current)
        {
            _editedSettings = SettingsManager.Clone(current);

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

            _daysHeaderLabel = CreateSectionLabel("Auto-start schedule", leftMargin, topHeaderY);
            Controls.Add(_daysHeaderLabel);

            _dayRows = new[]
            {
                CreateDayRow("Mon", _editedSettings.Mon, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 0,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Mon.Enabled = enabled; settings.Mon.Time = time; }),
                CreateDayRow("Tue", _editedSettings.Tue, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 1,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Tue.Enabled = enabled; settings.Tue.Time = time; }),
                CreateDayRow("Wed", _editedSettings.Wed, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 2,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Wed.Enabled = enabled; settings.Wed.Time = time; }),
                CreateDayRow("Thu", _editedSettings.Thu, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 3,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Thu.Enabled = enabled; settings.Thu.Time = time; }),
                CreateDayRow("Fri", _editedSettings.Fri, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 4,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Fri.Enabled = enabled; settings.Fri.Time = time; }),
                CreateDayRow("Sat", _editedSettings.Sat, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 5,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Sat.Enabled = enabled; settings.Sat.Time = time; }),
                CreateDayRow("Sun", _editedSettings.Sun, dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * 6,
                    delegate(AppSettings settings, bool enabled, string time) { settings.Sun.Enabled = enabled; settings.Sun.Time = time; })
            };

            foreach (var row in _dayRows)
            {
                row.CheckBox.CheckedChanged += DayCheckBox_CheckedChanged;
            }

            var selectFilesLabel = CreateSectionLabel("Select file(s) to launch", rightColumnX, topHeaderY);
            Controls.Add(selectFilesLabel);

            int fileRowY = topHeaderY + fileSectionLabelGap;
            _fileRows = new[]
            {
                CreateFileRow(1, _editedSettings.File1Path, _editedSettings.File1Enabled, fileRowY + fileRowGap * 0,
                    fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth,
                    "Default",
                    delegate(AppSettings settings, bool enabled, string? path) { settings.File1Enabled = enabled; settings.File1Path = path; }),

                CreateFileRow(2, _editedSettings.File2Path, _editedSettings.File2Enabled, fileRowY + fileRowGap * 1,
                    fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth,
                    "Default",
                    delegate(AppSettings settings, bool enabled, string? path) { settings.File2Enabled = enabled; settings.File2Path = path; }),

                CreateFileRow(3, _editedSettings.File3Path, _editedSettings.File3Enabled, fileRowY + fileRowGap * 2,
                    fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth,
                    "Clear",
                    delegate(AppSettings settings, bool enabled, string? path) { settings.File3Enabled = enabled; settings.File3Path = path; }),

                CreateFileRow(4, _editedSettings.File4Path, _editedSettings.File4Enabled, fileRowY + fileRowGap * 3,
                    fileCheckX, fileTextX, fileTextWidth, fileBrowseX, fileBrowseWidth, fileActionX, fileActionWidth,
                    "Clear",
                    delegate(AppSettings settings, bool enabled, string? path) { settings.File4Enabled = enabled; settings.File4Path = path; })
            };

            foreach (var row in _fileRows)
            {
                row.CheckBox.CheckedChanged += FileCheckBox_CheckedChanged;
                row.CheckBox.CheckedChanged += (_, __) => UpdateFileRowEnabledState(row);
                row.BrowseButton.Click += (_, __) => BrowseForFile(row);
                row.ActionButton.Click += (_, __) => ApplyRowAction(row);
                UpdateFileRowEnabledState(row);
            }

            UpdateFileActionButtonStates();

            _vacationModeLabel = CreateSectionLabel("Vacation mode", rightColumnX, vacationLabelY);
            Controls.Add(_vacationModeLabel);

            _autoStartOffFromCheckBox = new CheckBox
            {
                Text = "Turn auto-start OFF from this date",
                AutoSize = true,
                Location = new Point(rightColumnX, futureOffFromY + 4),
                Checked = _editedSettings.AutoStartOffFromEnabled,
                ForeColor = _editedSettings.AutoStartOffFromEnabled ? EnabledTextColor : DisabledTextColor
            };
            Controls.Add(_autoStartOffFromCheckBox);

            DateTime initialFromDate = _editedSettings.AutoStartOffFromDate.HasValue && _editedSettings.AutoStartOffFromDate.Value.Date >= DateTime.Today
                ? _editedSettings.AutoStartOffFromDate.Value.Date
                : DateTime.Today;

            _autoStartOffFromDatePicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Width = futureOffDateWidth,
                Location = new Point(futureOffDateX, futureOffFromY),
                MinDate = DateTime.Today,
                Value = initialFromDate,
                Enabled = _editedSettings.AutoStartOffFromEnabled
            };
            Controls.Add(_autoStartOffFromDatePicker);

            _autoStartOffUntilCheckBox = new CheckBox
            {
                Text = "Turn auto-start OFF until this date",
                AutoSize = true,
                Location = new Point(rightColumnX, futureOffUntilY + 4),
                Checked = _editedSettings.AutoStartOffUntilEnabled,
                ForeColor = _editedSettings.AutoStartOffUntilEnabled ? EnabledTextColor : DisabledTextColor
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
                Value = _editedSettings.AutoStartOffUntilDate.HasValue && _editedSettings.AutoStartOffUntilDate.Value.Date >= initialUntilMinDate
                    ? _editedSettings.AutoStartOffUntilDate.Value.Date
                    : initialUntilMinDate,
                Enabled = _editedSettings.AutoStartOffFromEnabled && _editedSettings.AutoStartOffUntilEnabled
            };
            Controls.Add(_autoStartOffUntilDatePicker);

            WireFutureOffControls();

            int buttonY = ClientSize.Height - buttonHeight - bottomMargin;

            _okButton = CreateSystemButton("OK", leftMargin, buttonY, buttonWidth, buttonHeight, AnchorStyles.Left | AnchorStyles.Bottom);
            _okButton.Click += (_, __) => OnOk();

            _cancelButton = CreateSystemButton("Cancel", ClientSize.Width - leftMargin - buttonWidth, buttonY, buttonWidth, buttonHeight, AnchorStyles.Right | AnchorStyles.Bottom);
            _cancelButton.Click += (_, __) => Close();

            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            ResumeLayout(false);
            PerformLayout();
        }

        private static Label CreateSectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = SectionHeaderFont,
                ForeColor = SectionHeaderColor,
                Location = new Point(x, y)
            };
        }

        private static Button CreateSystemButton(string text, int x, int y, int width, int height, AnchorStyles anchor)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = height,
                Location = new Point(x, y),
                Anchor = anchor,
                FlatStyle = FlatStyle.System
            };
        }

        private DayRow CreateDayRow(string dayAbbrev, DayLaunchSetting currentDaySetting, int checkX, int dayX, int timeX, int y, Action<AppSettings, bool, string> applyToSettings)
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
                Location = new Point(dayX, y + 6),
                ForeColor = currentDaySetting.Enabled ? EnabledTextColor : DisabledTextColor
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

            checkBox.CheckedChanged += (_, __) =>
            {
                timePicker.Enabled = checkBox.Checked;
                dayLabel.ForeColor = checkBox.Checked ? EnabledTextColor : DisabledTextColor;
            };

            Controls.Add(checkBox);
            Controls.Add(dayLabel);
            Controls.Add(timePicker);

            return new DayRow(checkBox, timePicker, applyToSettings);
        }

        private FileRow CreateFileRow(int slotIndex, string? path, bool isChecked, int y, int checkX, int textX, int textWidth, int browseX, int browseWidth, int actionX, int actionWidth, string actionText, Action<AppSettings, bool, string?> applyToSettings)
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

            var browseButton = CreateSystemButton("Browse...", browseX, y, browseWidth, 32, AnchorStyles.Top | AnchorStyles.Left);
            var actionButton = CreateSystemButton(actionText, actionX, y, actionWidth, 32, AnchorStyles.Top | AnchorStyles.Left);
            actionButton.Enabled = !string.IsNullOrWhiteSpace(path);

            Controls.Add(checkBox);
            Controls.Add(textBox);
            Controls.Add(browseButton);
            Controls.Add(actionButton);

            return new FileRow(slotIndex, checkBox, textBox, browseButton, actionButton, actionText, path, applyToSettings);
        }

        private void WireFutureOffControls()
        {
            _autoStartOffCheckBox_CheckedChanged(null, EventArgs.Empty);
            _autoStartOffFromCheckBox.CheckedChanged += _autoStartOffCheckBox_CheckedChanged;
            _autoStartOffUntilCheckBox.CheckedChanged += (_, __) =>
            {
                _autoStartOffUntilCheckBox.ForeColor = _autoStartOffUntilCheckBox.Checked ? EnabledTextColor : DisabledTextColor;
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
            bool fromChecked = _autoStartOffFromCheckBox.Checked;
            _autoStartOffFromCheckBox.ForeColor = fromChecked ? EnabledTextColor : DisabledTextColor;
            _autoStartOffFromDatePicker.Enabled = fromChecked;
            if (!fromChecked)
            {
                _autoStartOffUntilCheckBox.Checked = false;
            }
            _autoStartOffUntilCheckBox.Enabled = fromChecked;
            _autoStartOffUntilCheckBox.ForeColor = _autoStartOffUntilCheckBox.Checked ? EnabledTextColor : DisabledTextColor;
            _autoStartOffUntilDatePicker.Enabled = fromChecked && _autoStartOffUntilCheckBox.Checked;
        }

        private void UpdateFileRowEnabledState(FileRow row)
        {
            bool isEnabled = row.CheckBox.Checked;
            row.TextBox.Enabled = isEnabled;
            row.BrowseButton.Enabled = true;
            row.ActionButton.Enabled = !string.IsNullOrWhiteSpace(row.Path);
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
            UpdateFileRowEnabledState(row);
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
            UpdateFileRowEnabledState(row);
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
            if (sender is CheckBox changedCheckBox)
            {
                PreventLastCheckedItemFromBeingUnchecked(changedCheckBox, _fileRows.Select(r => r.CheckBox));
            }
        }

        private void DayCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox changedCheckBox)
            {
                PreventLastCheckedItemFromBeingUnchecked(changedCheckBox, _dayRows.Select(r => r.CheckBox));
            }
        }

        private static void PreventLastCheckedItemFromBeingUnchecked(CheckBox changedCheckBox, IEnumerable<CheckBox> checkBoxes)
        {
            int checkedCount = 0;
            foreach (var checkBox in checkBoxes)
            {
                if (checkBox.Checked)
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
                    row.CheckBox.Checked = false;
                    UpdateFileRowEnabledState(row);
                }
            }

            UpdateFileActionButtonStates();

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

            ApplyDayRowsToSettings();
            ApplyFileRowsToSettings();

            _editedSettings.AutoStartOffFromEnabled = _autoStartOffFromCheckBox.Checked;
            _editedSettings.AutoStartOffFromDate = _autoStartOffFromCheckBox.Checked ? _autoStartOffFromDatePicker.Value.Date : null;
            _editedSettings.AutoStartOffUntilEnabled = _autoStartOffUntilCheckBox.Checked;
            _editedSettings.AutoStartOffUntilDate = (_autoStartOffFromCheckBox.Checked && _autoStartOffUntilCheckBox.Checked)
                ? _autoStartOffUntilDatePicker.Value.Date
                : null;

            ResultSettings = _editedSettings;
            Accepted = true;
            Close();
        }

        private void ApplyDayRowsToSettings()
        {
            foreach (var row in _dayRows)
            {
                row.ApplyToSettings(_editedSettings, row.CheckBox.Checked, row.TimePicker.Value.ToString("HH:mm"));
            }
        }

        private void ApplyFileRowsToSettings()
        {
            foreach (var row in _fileRows)
            {
                row.ApplyToSettings(_editedSettings, row.CheckBox.Checked, row.Path);
            }
        }

        private sealed class DayRow
        {
            public DayRow(CheckBox checkBox, DateTimePicker timePicker, Action<AppSettings, bool, string> applyToSettings)
            {
                CheckBox = checkBox;
                TimePicker = timePicker;
                ApplyToSettings = applyToSettings;
            }

            public CheckBox CheckBox { get; }
            public DateTimePicker TimePicker { get; }
            public Action<AppSettings, bool, string> ApplyToSettings { get; }
        }

        private sealed class FileRow
        {
            public FileRow(int slotIndex, CheckBox checkBox, TextBox textBox, Button browseButton, Button actionButton, string actionText, string? path, Action<AppSettings, bool, string?> applyToSettings)
            {
                SlotIndex = slotIndex;
                CheckBox = checkBox;
                TextBox = textBox;
                BrowseButton = browseButton;
                ActionButton = actionButton;
                ActionText = actionText;
                Path = path;
                ApplyToSettings = applyToSettings;
            }

            public int SlotIndex { get; }
            public CheckBox CheckBox { get; }
            public TextBox TextBox { get; }
            public Button BrowseButton { get; }
            public Button ActionButton { get; }
            public string ActionText { get; }
            public bool IsDefaultAction => string.Equals(ActionText, "Default", StringComparison.OrdinalIgnoreCase);
            public string? Path { get; set; }
            public Action<AppSettings, bool, string?> ApplyToSettings { get; }
        }
    }
}