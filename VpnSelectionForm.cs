using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    public sealed class VpnSelectionForm : Form
    {
        private readonly ListBox _vpnListBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public string? SelectedConnectionName { get; private set; }

        public VpnSelectionForm(IEnumerable<string> vpnConnections)
        {
            Text = "Select VPN connection";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            BackColor = Color.White;
            ClientSize = new Size(430, 290);

            var instructionLabel = new Label
            {
                AutoSize = false,
                Text = "Choose the Windows VPN connection that FileStarter should connect before launching your selected files/apps.",
                Location = new Point(16, 16),
                Size = new Size(ClientSize.Width - 32, 40)
            };

            _vpnListBox = new ListBox
            {
                Location = new Point(16, 68),
                Size = new Size(ClientSize.Width - 32, 150),
                IntegralHeight = false
            };

            foreach (var connection in vpnConnections.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                _vpnListBox.Items.Add(connection.Trim());
            }

            if (_vpnListBox.Items.Count > 0)
                _vpnListBox.SelectedIndex = 0;

            _vpnListBox.DoubleClick += (_, __) => ConfirmSelection();

            int buttonY = ClientSize.Height - 48;
            _okButton = new Button
            {
                Text = "OK",
                Width = 100,
                Height = 32,
                Location = new Point(16, buttonY),
                FlatStyle = FlatStyle.System
            };
            _okButton.Click += (_, __) => ConfirmSelection();

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = 100,
                Height = 32,
                Location = new Point(ClientSize.Width - 116, buttonY),
                FlatStyle = FlatStyle.System
            };
            _cancelButton.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(instructionLabel);
            Controls.Add(_vpnListBox);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        private void ConfirmSelection()
        {
            if (_vpnListBox.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(
                    this,
                    "Please select one VPN connection.",
                    "Select VPN connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SelectedConnectionName = selected.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}