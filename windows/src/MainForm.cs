using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
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
        private bool _resourcesReleased;
        private readonly bool _uiSmoke;
        private readonly Icon _brandIcon;
        private readonly Image _brandArtwork;
        private readonly ContextMenuStrip _trayMenu;
        private readonly List<Font> _ownedFonts = new List<Font>();
        private readonly List<Label> _wrappingLabels = new List<Label>();

        internal MainForm() : this(false) { }

        private MainForm(bool uiSmoke)
        {
            _uiSmoke = uiSmoke;
            Text = "Parrot for Windows";
            AccessibleName = "Parrot for Windows dictation";
            AccessibleDescription = "Local toggle-to-dictate preview using Parakeet speech recognition.";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 460);
            MinimumSize = new Size(520, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            BackColor = Brand.Background;
            ForeColor = Brand.Cream;
            Font = OwnFont(10F, FontStyle.Regular);
            DoubleBuffered = true;
            _brandIcon = Brand.LoadIcon();
            try { _brandArtwork = Brand.LoadArtwork(); }
            catch { _brandIcon.Dispose(); throw; }
            Icon = _brandIcon;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoScroll = true;
            layout.Padding = new Padding(28, 24, 28, 24);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel heading = new TableLayoutPanel();
            heading.AutoSize = true;
            heading.Dock = DockStyle.Top;
            heading.ColumnCount = 2;
            heading.RowCount = 1;
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heading.Margin = new Padding(0, 0, 0, 20);
            PictureBox bird = new PictureBox();
            bird.Image = _brandArtwork;
            bird.SizeMode = PictureBoxSizeMode.Zoom;
            bird.Size = new Size(48, 48);
            bird.Margin = new Padding(0);
            bird.TabStop = false;
            bird.AccessibleName = "Parrot bird";
            heading.Controls.Add(bird, 0, 0);
            Label title = MakeLabel("PARROT", 21F, FontStyle.Bold, Brand.Cream);
            title.Margin = new Padding(0, 3, 0, 0);
            title.AccessibleName = "Parrot for Windows";
            heading.Controls.Add(title, 1, 0);
            layout.Controls.Add(heading, 0, 0);

            Label preview = MakeLabel("WINDOWS PREVIEW  /  LOCAL DICTATION", 9F, FontStyle.Bold, Brand.Amber);
            preview.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(preview, 0, 1);

            TableLayoutPanel statusPanel = new TableLayoutPanel();
            statusPanel.AutoSize = true;
            statusPanel.Dock = DockStyle.Top;
            statusPanel.BackColor = Brand.Panel;
            statusPanel.Padding = new Padding(20);
            statusPanel.Margin = new Padding(0, 0, 0, 18);
            statusPanel.ColumnCount = 1;
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _statusLabel = new AccessibleStatusLabel();
            _statusLabel.AutoSize = true;
            _statusLabel.Font = OwnFont(20F, FontStyle.Bold);
            _statusLabel.ForeColor = Brand.Cream;
            _statusLabel.Margin = new Padding(0, 0, 0, 9);
            _statusLabel.SetStatus("Ready", "Nothing is recording.");
            statusPanel.Controls.Add(_statusLabel);
            _detailLabel = MakeLabel("Nothing is recording. Start when you are ready.", 10F, FontStyle.Regular, Brand.Muted);
            _detailLabel.AccessibleName = "Dictation status details";
            _detailLabel.Margin = new Padding(0);
            statusPanel.Controls.Add(_detailLabel);
            statusPanel.SizeChanged += delegate {
                _detailLabel.MaximumSize = new Size(Math.Max(80, statusPanel.ClientSize.Width - statusPanel.Padding.Horizontal), 0);
            };
            layout.Controls.Add(statusPanel, 0, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.Dock = DockStyle.Top;
            actions.WrapContents = true;
            actions.Margin = new Padding(0, 0, 0, 14);
            _toggleButton = MakeButton("Start recording", "Start recording", true);
            _toggleButton.TabIndex = 0;
            _toggleButton.Click += delegate { ToggleCapture(); };
            actions.Controls.Add(_toggleButton);
            _cancelButton = MakeButton("Cancel", "Cancel and discard dictation", false);
            _cancelButton.TabIndex = 1;
            _cancelButton.Enabled = false;
            _cancelButton.Click += delegate { CancelCapture(); };
            actions.Controls.Add(_cancelButton);
            _quitButton = MakeButton("Quit", "Quit Parrot", false);
            _quitButton.TabIndex = 2;
            _quitButton.Click += delegate { QuitApplication(); };
            actions.Controls.Add(_quitButton);
            layout.Controls.Add(actions, 0, 3);

            Label shortcut = MakeLabel("Ctrl+Alt+Space toggles recording. Esc cancels.", 9F, FontStyle.Regular, Brand.Cream);
            shortcut.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(shortcut, 0, 4);
            _wrappingLabels.Add(shortcut);
            Label processing = MakeLabel("Parakeet TDT 0.6B v2 int8 · CPU processing on this PC.\nClosing the window keeps Parrot in the tray. Use Quit to exit.", 9F, FontStyle.Regular, Brand.Muted);
            processing.Margin = new Padding(0);
            layout.Controls.Add(processing, 0, 5);
            _wrappingLabels.Add(processing);
            layout.SizeChanged += delegate {
                int width = Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                foreach (Label label in _wrappingLabels) label.MaximumSize = new Size(width, 0);
            };
            Controls.Add(layout);
            AcceptButton = _toggleButton;

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            _trayMenu = trayMenu;
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
            _trayIcon.Icon = _brandIcon;
            _trayIcon.Text = "Parrot for Windows: Ready";
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = !uiSmoke;
            _trayIcon.DoubleClick += delegate { ShowAndActivate(); };

            Load += HandleLoad;
            FormClosing += HandleFormClosing;
        }

        private void HandleLoad(object sender, EventArgs eventArgs)
        {
            if (_uiSmoke) return;
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
            _statusLabel.ForeColor = phase == CapturePhase.Listening || phase == CapturePhase.Blocked
                ? Brand.Red : Brand.Cream;
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

            if (_uiSmoke) return;

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

        internal void ShowAndActivate()
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
                    eventArgs.Cancel = true;
                    return;
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
            if (_resourcesReleased) return;
            _resourcesReleased = true;
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

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }

        private Font OwnFont(float size, FontStyle style)
        {
            Font font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            _ownedFonts.Add(font);
            return font;
        }

        private Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = OwnFont(size, style);
            label.ForeColor = color;
            label.UseMnemonic = false;
            return label;
        }

        private Button MakeButton(string text, string accessibleName, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.AccessibleName = accessibleName;
            button.AutoSize = true;
            button.MinimumSize = new Size(primary ? 154 : 82, 40);
            button.Padding = new Padding(10, 5, 10, 5);
            button.Margin = new Padding(0, 0, 10, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Brand.Amber : Brand.Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Brand.Cream : Brand.Raised;
            button.FlatAppearance.MouseDownBackColor = primary ? Brand.Muted : Brand.Line;
            button.BackColor = primary ? Brand.Amber : Brand.Panel;
            button.ForeColor = primary
                ? (SystemInformation.HighContrast ? SystemColors.HighlightText : Brand.Background)
                : Brand.Cream;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseResources();
                if (_trayMenu != null) _trayMenu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (_brandArtwork != null) _brandArtwork.Dispose();
                if (_brandIcon != null) _brandIcon.Dispose();
                foreach (Font font in _ownedFonts) font.Dispose();
                _ownedFonts.Clear();
            }
        }

        // A render-only path. Load is guarded: no visible tray, hotkey, model or audio.
        internal static int RunUISmoke(string path)
        {
            Directory.CreateDirectory(path);
            CapturePhase[] phases = { CapturePhase.Ready, CapturePhase.Listening, CapturePhase.Finishing, CapturePhase.Blocked };
            string[] details = {
                "Nothing is recording. Start when you are ready.",
                "Recording on this PC. Press Ctrl+Alt+Space to finish.",
                "Processing your speech on this PC. You can still cancel.",
                "Ctrl+Alt+Space is unavailable. Use Start recording or close the conflicting app."
            };
            using (MainForm form = new MainForm(true))
            {
                form.CreateControl();
                form.Show();
                Application.DoEvents();
                if (!form._statusLabel.Visible) throw new InvalidOperationException("Native render observer has hidden controls.");
                for (int index = 0; index < phases.Length; index++)
                {
                    form.SetState(phases[index], details[index], index != 2, index == 1 || index == 2);
                    form.PerformLayout();
                    form.SaveSmokeBitmap(Path.Combine(path, PhaseText(phases[index]).ToLowerInvariant() + ".png"));
                }
                form.ClientSize = new Size(504, 401);
                form.PerformLayout();
                form.SaveSmokeBitmap(Path.Combine(path, "minimum.png"));
                if (form._controller != null || form._hotkeyRegistered || form._trayIcon.Visible)
                    throw new InvalidOperationException("UI smoke unexpectedly initialized runtime resources.");
            }
            foreach (float factor in new float[] { 1.5F, 2F })
            {
                using (MainForm form = new MainForm(true))
                {
                    form.AutoScaleMode = AutoScaleMode.None;
                    form.Scale(new SizeF(factor, factor));
                    form.CreateControl();
                    form.Show();
                    Application.DoEvents();
                    form.PerformLayout();
                    form.SaveSmokeBitmap(Path.Combine(path, "layout-scale-" + ((int)(factor * 100)).ToString() + ".png"));
                }
            }
            return 0;
        }

        private void SaveSmokeBitmap(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, ImageFormat.Png);
            }
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
