using System;
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

            string[] dayLabels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            DayLaunchSetting[] daySettings =
            {
                _editedSettings.Mon, _editedSettings.Tue, _editedSettings.Wed, _editedSettings.Thu,
                _editedSettings.Fri, _editedSettings.Sat, _editedSettings.Sun
            };

            _dayRows = dayLabels
                .Select((label, index) => CreateDayRow(label, daySettings[index], dayCheckX, dayLabelX, dayTimeX, dayRowStartY + dayRowGap * index))
                .ToArray();

            var selectFilesLabel = CreateSectionLabel("Select file(s) to launch", rightColumnX, topHeaderY);
            Controls.Add(selectFilesLabel);

            int fileRowY = topHeaderY + fileSectionLabelGap;
            var fileDefinitions = new[]
            {
                new { SlotIndex = 1, Path = _editedSettings.File1Path, Enabled = _editedSettings.File1Enabled, ActionText = "Default" },
                new { SlotIndex = 2, Path = _editedSettings.File2Path, Enabled = _editedSettings.File2Enabled, ActionText = "Default" },
                new { SlotIndex = 3, Path = _editedSettings.File3Path, Enabled = _editedSettings.File3Enabled, ActionText = "Clear" },
                new { SlotIndex = 4, Path = _editedSettings.File4Path, Enabled = _editedSettings.File4Enabled, ActionText = "Clear" }
            };

            _fileRows = fileDefinitions
                .Select((definition, index) => CreateFileRow(
                    definition.SlotIndex,
                    definition.Path,
                    definition.Enabled,
                    fileRowY + fileRowGap * index,
                    fileCheckX,
                    fileTextX,
                    fileTextWidth,
                    fileBrowseX,
                    fileBrowseWidth,
                    fileActionX,
                    fileActionWidth,
                    definition.ActionText))
                .ToArray();

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
            return new DayRow(checkBox, timePicker);
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

            var browseButton = CreateSystemButton("Browse...", browseX, y, browseWidth, 32, AnchorStyles.Top | AnchorStyles.Left);
            browseButton.Enabled = true;

            var actionButton = CreateSystemButton(actionText, actionX, y, actionWidth, 32, AnchorStyles.Top | AnchorStyles.Left);
            actionButton.Enabled = !string.IsNullOrWhiteSpace(path);

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
            DayLaunchSetting[] daySettings =
            {
                _editedSettings.Mon, _editedSettings.Tue, _editedSettings.Wed, _editedSettings.Thu,
                _editedSettings.Fri, _editedSettings.Sat, _editedSettings.Sun
            };

            for (int i = 0; i < _dayRows.Length; i++)
            {
                daySettings[i].Enabled = _dayRows[i].CheckBox.Checked;
                daySettings[i].Time = _dayRows[i].TimePicker.Value.ToString("HH:mm");
            }
        }

        private void ApplyFileRowsToSettings()
        {
            Action<int, FileRow> apply = (slotIndex, row) =>
            {
                switch (slotIndex)
                {
                    case 1:
                        _editedSettings.File1Enabled = row.CheckBox.Checked;
                        _editedSettings.File1Path = row.Path;
                        break;
                    case 2:
                        _editedSettings.File2Enabled = row.CheckBox.Checked;
                        _editedSettings.File2Path = row.Path;
                        break;
                    case 3:
                        _editedSettings.File3Enabled = row.CheckBox.Checked;
                        _editedSettings.File3Path = row.Path;
                        break;
                    case 4:
                        _editedSettings.File4Enabled = row.CheckBox.Checked;
                        _editedSettings.File4Path = row.Path;
                        break;
                }
            };

            foreach (var row in _fileRows)
            {
                apply(row.SlotIndex, row);
            }
        }

        private sealed class DayRow
        {
            public DayRow(CheckBox checkBox, DateTimePicker timePicker)
            {
                CheckBox = checkBox;
                TimePicker = timePicker;
            }

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