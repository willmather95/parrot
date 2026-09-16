using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Parrot.Windows
{
    internal sealed class AccessibleStatusLabel : Label
    {
        internal void SetStatus(string status, string detail)
        {
            Text = status;
            AccessibleName = "Dictation status: " + status;
            AccessibleDescription = detail;
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
                AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }
    }

    internal sealed class MainForm : Form, IParrotView
    {
        private const int HotkeyId = 0x5041;

        private readonly AccessibleStatusLabel _statusLabel;
        private readonly Label _detailLabel;
        private readonly Button _toggleButton;
        private readonly Button _cancelButton;
        private readonly Button _quitButton;
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _trayToggleItem;
        private readonly ToolStripMenuItem _trayCancelItem;
        private ParrotController _controller;
        private bool _hotkeyRegistered;
        private bool _quitting;

        internal MainForm()
        {
            Text = "Parrot for Windows";
            AccessibleName = "Parrot for Windows dictation";
            AccessibleDescription = "Local toggle-to-dictate preview using Parakeet speech recognition.";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(470, 285);
            MinimumSize = new Size(430, 275);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.Padding = new Padding(22);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label heading = new Label();
            heading.Text = "Parrot for Windows";
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 0, 0, 6);
            heading.Font = new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                SystemFonts.MessageBoxFont.Size + 3F,
                FontStyle.Bold);
            heading.AccessibleName = "Parrot for Windows";
            layout.Controls.Add(heading);

            Label preview = new Label();
            preview.Text = "Preview: Parakeet TDT 0.6B v2 int8, processed locally on CPU";
            preview.AutoSize = true;
            preview.ForeColor = SystemColors.GrayText;
            preview.Margin = new Padding(0, 0, 0, 18);
            layout.Controls.Add(preview);

            _statusLabel = new AccessibleStatusLabel();
            _statusLabel.Text = "Ready";
            _statusLabel.AutoSize = true;
            _statusLabel.Font = new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                SystemFonts.MessageBoxFont.Size + 1F,
                FontStyle.Bold);
            _statusLabel.AccessibleName = "Dictation status";
            _statusLabel.Margin = new Padding(0, 0, 0, 5);
            layout.Controls.Add(_statusLabel);

            _detailLabel = new Label();
            _detailLabel.Text = "Nothing is recording.";
            _detailLabel.AutoSize = true;
            _detailLabel.MaximumSize = new Size(410, 0);
            _detailLabel.AccessibleName = "Dictation status details";
            _detailLabel.Margin = new Padding(0, 0, 0, 18);
            layout.Controls.Add(_detailLabel);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.WrapContents = true;
            actions.Margin = new Padding(0, 0, 0, 14);

            _toggleButton = new Button();
            _toggleButton.Text = "Start recording";
            _toggleButton.AutoSize = true;
            _toggleButton.MinimumSize = new Size(128, 36);
            _toggleButton.AccessibleName = "Start recording";
            _toggleButton.Click += delegate { ToggleCapture(); };
            actions.Controls.Add(_toggleButton);

            _cancelButton = new Button();
            _cancelButton.Text = "Cancel";
            _cancelButton.AutoSize = true;
            _cancelButton.MinimumSize = new Size(84, 36);
            _cancelButton.AccessibleName = "Cancel and discard dictation";
            _cancelButton.Enabled = false;
            _cancelButton.Click += delegate { CancelCapture(); };
            actions.Controls.Add(_cancelButton);

            _quitButton = new Button();
            _quitButton.Text = "Quit";
            _quitButton.AutoSize = true;
            _quitButton.MinimumSize = new Size(76, 36);
            _quitButton.AccessibleName = "Quit Parrot";
            _quitButton.Click += delegate { QuitApplication(); };
            actions.Controls.Add(_quitButton);
            layout.Controls.Add(actions);

            Label shortcut = new Label();
            shortcut.Text = "Shortcut: Ctrl+Alt+Space toggles recording. Esc cancels.";
            shortcut.AutoSize = true;
            shortcut.ForeColor = SystemColors.GrayText;
            shortcut.Margin = new Padding(0);
            layout.Controls.Add(shortcut);

            Controls.Add(layout);
            AcceptButton = _toggleButton;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showItem = new ToolStripMenuItem("Show Parrot");
            showItem.Click += delegate { ShowAndActivate(); };
            trayMenu.Items.Add(showItem);
            _trayToggleItem = new ToolStripMenuItem("Start recording");
            _trayToggleItem.Click += delegate { ToggleCapture(); };
            trayMenu.Items.Add(_trayToggleItem);
            _trayCancelItem = new ToolStripMenuItem("Cancel");
            _trayCancelItem.Enabled = false;
            _trayCancelItem.Click += delegate { CancelCapture(); };
            trayMenu.Items.Add(_trayCancelItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem quitItem = new ToolStripMenuItem("Quit");
            quitItem.Click += delegate { QuitApplication(); };
            trayMenu.Items.Add(quitItem);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.Text = "Parrot for Windows: Ready";
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { ShowAndActivate(); };

            Load += HandleLoad;
            FormClosing += HandleFormClosing;
        }

        private void HandleLoad(object sender, EventArgs eventArgs)
        {
            SynchronizationContext context = SynchronizationContext.Current;
            if (context == null)
            {
                context = new WindowsFormsSynchronizationContext();
            }

            _controller = new ParrotController(this, context);
            _hotkeyRegistered = NativeMethods.RegisterHotKey(
                Handle,
                HotkeyId,
                NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
                NativeMethods.VkSpace);
            if (!_hotkeyRegistered)
            {
                SetState(
                    CapturePhase.Blocked,
                    "Ctrl+Alt+Space is unavailable. Use Start recording or close the conflicting app.",
                    true,
                    false);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                ToggleCapture();
            }

            base.WndProc(ref message);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape
                && _controller != null
                && (_controller.Phase == CapturePhase.Listening
                    || _controller.Phase == CapturePhase.Finishing))
            {
                CancelCapture();
                return true;
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        public void SetState(
            CapturePhase phase,
            string detail,
            bool toggleEnabled,
            bool cancelEnabled)
        {
            string status = PhaseText(phase);
            _statusLabel.SetStatus(status, detail);
            _detailLabel.Text = detail;
            _toggleButton.Enabled = toggleEnabled;
            _cancelButton.Enabled = cancelEnabled;
            _trayToggleItem.Enabled = toggleEnabled;
            _trayCancelItem.Enabled = cancelEnabled;

            bool listening = phase == CapturePhase.Listening;
            _toggleButton.Text = listening ? "Stop recording" : "Start recording";
            _toggleButton.AccessibleName = _toggleButton.Text;
            _trayToggleItem.Text = listening ? "Stop recording" : "Start recording";
            _trayIcon.Text = "Parrot for Windows: " + status;

            if (phase == CapturePhase.Listening || phase == CapturePhase.Finishing)
            {
                ShowStatusWithoutActivation(true);
            }
            else if (IsHandleCreated)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HwndNotTopMost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove
                        | NativeMethods.SwpNoSize
                        | NativeMethods.SwpNoActivate);
            }
        }

        private void ToggleCapture()
        {
            if (_controller != null)
            {
                _controller.Toggle();
            }
        }

        private void CancelCapture()
        {
            if (_controller != null)
            {
                _controller.Cancel();
            }
        }

        private void ShowStatusWithoutActivation(bool topMost)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
            NativeMethods.SetWindowPos(
                Handle,
                topMost ? NativeMethods.HwndTopMost : NativeMethods.HwndNotTopMost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove
                    | NativeMethods.SwpNoSize
                    | NativeMethods.SwpNoActivate
                    | NativeMethods.SwpShowWindow);
        }

        private void ShowAndActivate()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _toggleButton.Focus();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!_quitting && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                if (_controller != null
                    && (_controller.Phase == CapturePhase.Listening
                        || _controller.Phase == CapturePhase.Finishing))
                {
                    _controller.Cancel();
                }

                eventArgs.Cancel = true;
                Hide();
                return;
            }

            ReleaseResources();
        }

        private void QuitApplication()
        {
            _quitting = true;
            Close();
        }

        private void ReleaseResources()
        {
            if (_hotkeyRegistered && IsHandleCreated)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }

            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        private static string PhaseText(CapturePhase phase)
        {
            switch (phase)
            {
                case CapturePhase.Listening:
                    return "Listening";
                case CapturePhase.Finishing:
                    return "Finishing";
                case CapturePhase.Blocked:
                    return "Blocked";
                default:
                    return "Ready";
            }
        }
    }
}
