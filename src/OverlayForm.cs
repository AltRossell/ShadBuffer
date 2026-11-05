using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShadOBS
{
    // a normal solid window that only exists while it's open, it never touches the game
    public sealed class OverlayForm : Form
    {
        private const int LabelWidth = 170;
        private const int ControlLeft = 180;
        private const int ControlWidth = 308;
        private const int RowHeight = 36;
        private const int PanelLeft = 24;
        private const int PanelTop = 208;
        private const int PanelWidth = 492;
        private const int PanelHeight = 300;
        private const int CornerPreference = 33;
        private const int CornerRound = 2;
        private const int StatusGraceSeconds = 15;

        private readonly Context ctx;
        private readonly Settings draft;
        private readonly Companion owner;
        private readonly IntPtr previousWindow;
        private readonly Timer statusTimer;
        private readonly Label statusLabel;
        private readonly Label replayDetail;
        private readonly Label messageLabel;
        private readonly ToggleSwitch replaySwitch;
        private readonly Button applyButton;
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<Panel> tabPanels = new List<Panel>();
        private readonly Panel tabMarker;

        private Settings saved;
        private bool busy;
        private bool dialogOpen;
        private bool switching;
        private DateTime ignoreStatusUntil = DateTime.MinValue;
        private Label captureHelp;
        private FlatCombo monitorCombo;

        public OverlayForm(Context ctx, Settings draft, Companion owner)
        {
            this.ctx = ctx;
            this.draft = draft;
            this.owner = owner;
            saved = Settings.Load(ctx);
            previousWindow = GetForegroundWindow();

            Text = Context.AppName;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 590);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75F);

            Controls.Add(NewLabel(Context.AppName, new Point(PanelLeft, 14), new Size(200, 38), Theme.Text, 18F, FontStyle.Bold));
            statusLabel = NewLabel("", new Point(216, 24), new Size(300, 22), Theme.Muted, 9.75F);
            statusLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(statusLabel);

            Controls.Add(NewLabel("Instant replay", new Point(PanelLeft, 58), new Size(300, 24), Theme.Text, 11F, FontStyle.Bold));
            replayDetail = NewLabel("", new Point(PanelLeft, 82), new Size(400, 20), Theme.Muted, 9.25F);
            Controls.Add(replayDetail);
            replaySwitch = new ToggleSwitch { Location = new Point(468, 66), Checked = saved.ReplayEnabled };
            replaySwitch.Toggled += (_, __) => ToggleReplay();
            Controls.Add(replaySwitch);

            Button saveButton = NewButton("Save clip", new Point(PanelLeft, 114), new Size(240, 38));
            saveButton.BackColor = Theme.Accent;
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Click += (_, __) => owner.SaveClip();
            Button clipsButton = NewButton("Open clips", new Point(276, 114), new Size(240, 38));
            clipsButton.Click += (_, __) => owner.OpenClips();
            Controls.Add(saveButton);
            Controls.Add(clipsButton);

            tabMarker = new Panel { BackColor = Theme.Accent, Size = new Size(123, 2) };
            AddTab("Clips", BuildClipsTab());
            AddTab("Video", BuildVideoTab());
            AddTab("Audio", BuildAudioTab());
            AddTab("Startup", BuildStartupTab());
            Controls.Add(tabMarker);
            SelectTab(0);

            messageLabel = NewLabel("", new Point(PanelLeft, 516), new Size(360, 40), Theme.Muted, 9F);
            Controls.Add(messageLabel);
            Hotkey overlayKey = Hotkey.Parse(saved.OverlayHotkey);
            Controls.Add(NewLabel((overlayKey?.ToString() ?? "Esc") + " or Esc to close", new Point(PanelLeft, 558), new Size(300, 20), Theme.Muted, 8.5F));

            applyButton = NewButton("Apply", new Point(396, 524), new Size(120, 40));
            applyButton.Click += (_, __) => ApplyChanges();
            Controls.Add(applyButton);

            statusTimer = new Timer { Interval = 1000 };
            statusTimer.Tick += (_, __) => UpdateStatus();
            statusTimer.Start();

            UpdateStatus();
            UpdateDirty();

            Deactivate += (_, __) =>
            {
                if (!busy && !dialogOpen && !IsDisposed)
                {
                    BeginInvoke(new Action(Close));
                }
            };
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseIfIdle();
                }
            };
        }

        public void CloseIfIdle()
        {
            if (!busy)
            {
                Close();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int corner = CornerRound;
            DwmSetWindowAttribute(Handle, CornerPreference, ref corner, sizeof(int));

            Screen screen = previousWindow != IntPtr.Zero ? Screen.FromHandle(previousWindow) : Screen.PrimaryScreen;
            Rectangle area = screen.Bounds;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            statusTimer.Dispose();
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
            base.OnFormClosed(e);
        }

        private Panel BuildClipsTab()
        {
            Panel panel = NewTabPanel();
            int y = 0;

            AddRow(panel, ref y, "Clip length", NewCombo(new[]
            {
                new Choice("30 seconds", 30),
                new Choice("1 minute", 60),
                new Choice("2 minutes", 120),
                new Choice("3 minutes", 180),
                new Choice("5 minutes", 300),
                new Choice("10 minutes", 600),
            }, draft.ClipSeconds, value => draft.ClipSeconds = (int)value));

            AddRow(panel, ref y, "Save clip hotkey", NewHotkeyBox(draft.SaveHotkey, hotkey =>
            {
                draft.SaveHotkey = hotkey.ToString();
                if (hotkey.ToString() != saved.OverlayHotkey && !owner.IsHotkeyFree(hotkey))
                {
                    ShowMessage($"Another app also uses {hotkey}. It should still save clips, but that app may react to it too.");
                }
            }));

            AddRow(panel, ref y, "Overlay hotkey", NewHotkeyBox(draft.OverlayHotkey, hotkey =>
            {
                if (hotkey.ToString() != saved.OverlayHotkey && !owner.IsHotkeyFree(hotkey))
                {
                    ShowMessage("Another app already uses that combo, so pick a different one.");
                    return;
                }
                draft.OverlayHotkey = hotkey.ToString();
            }));

            var folder = new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Text = draft.ClipsDir,
                Width = ControlWidth - 48,
            };
            Button browse = NewButton("...", new Point(ControlLeft + ControlWidth - 40, y + 4), new Size(40, 26));
            browse.Click += (_, __) => PickFolder(folder);
            panel.Controls.Add(browse);
            AddRow(panel, ref y, "Save clips to", folder, folder.Width);

            AddToggleRow(panel, ref y, "Sound when saved", draft.SaveSound, value => draft.SaveSound = value);
            AddToggleRow(panel, ref y, "Sort clips by game", draft.SortByGame, value => draft.SortByGame = value);
            return panel;
        }

        private Panel BuildVideoTab()
        {
            Panel panel = NewTabPanel();
            int y = 0;

            AddRow(panel, ref y, "Capture", NewCombo(new[]
            {
                new Choice("Game capture", Settings.CaptureGame),
                new Choice("Desktop capture", Settings.CaptureDesktop),
            }, draft.Capture, value =>
            {
                draft.Capture = (string)value;
                UpdateCaptureHelp();
            }));

            captureHelp = NewLabel("", new Point(ControlLeft, y), new Size(ControlWidth, 52), Theme.Muted, 8.75F);
            panel.Controls.Add(captureHelp);
            y += 54;

            List<DisplayInfo> displays = SystemInfo.AllDisplays();
            DisplayInfo primary = displays.FirstOrDefault(d => d.IsPrimary);
            var monitors = displays
                .Select(d => new Choice($"Display {d.Number} ({d.Width}x{d.Height}){(d.IsPrimary ? ", main" : "")}", d.MonitorId))
                .ToArray();
            string currentMonitor = string.IsNullOrEmpty(draft.MonitorId) ? primary?.MonitorId : draft.MonitorId;
            monitorCombo = NewCombo(monitors, currentMonitor, value => draft.MonitorId = (string)value);
            AddRow(panel, ref y, "Monitor to record", monitorCombo);

            AddRow(panel, ref y, "Quality", NewCombo(new[]
            {
                new Choice("Highest (big files)", 16),
                new Choice("High", 20),
                new Choice("Medium", 24),
                new Choice("Low (small files)", 28),
            }, draft.Quality, value => draft.Quality = (int)value));

            AddRow(panel, ref y, "Frame rate", NewCombo(new[]
            {
                new Choice("30 fps", 30),
                new Choice("60 fps", 60),
                new Choice("120 fps", 120),
                new Choice("144 fps", 144),
            }, draft.Fps, value => draft.Fps = (int)value));

            var resolutions = new List<Choice> { new Choice("Same as monitor", 0) };
            foreach (int height in new[] { 1440, 1080, 720 })
            {
                if (primary == null || height < primary.Height)
                {
                    resolutions.Add(new Choice(height + "p", height));
                }
            }
            AddRow(panel, ref y, "Resolution", NewCombo(resolutions.ToArray(), draft.OutputHeight, value => draft.OutputHeight = (int)value));

            State state = State.Load(ctx);
            var codecs = new List<Choice>();
            if (state.EncoderBrand != Encoders.BrandCpu)
            {
                codecs.Add(new Choice("H.264 (plays everywhere)", Encoders.CodecH264));
                if (state.SupportsHevc)
                {
                    codecs.Add(new Choice("HEVC (smaller files)", Encoders.CodecHevc));
                }
                if (state.SupportsAv1)
                {
                    codecs.Add(new Choice("AV1 (smallest, less compatible)", Encoders.CodecAv1));
                }
            }
            codecs.Add(new Choice("CPU x264 (slowest)", Encoders.CodecCpu));
            AddRow(panel, ref y, "Encoder", NewCombo(codecs.ToArray(), draft.Codec, value => draft.Codec = (string)value));

            UpdateCaptureHelp();
            return panel;
        }

        private Panel BuildAudioTab()
        {
            Panel panel = NewTabPanel();
            int y = 0;

            FlatCombo micCombo = null;
            FlatCombo noiseCombo = null;
            AddToggleRow(panel, ref y, "Record mic", draft.RecordMic, value =>
            {
                draft.RecordMic = value;
                micCombo.Enabled = value;
                noiseCombo.Enabled = value;
            });

            var mics = new List<Choice> { new Choice("Windows default", Settings.DeviceDefault) };
            mics.AddRange(AudioDevices.Microphones().Select(d => new Choice(d.Name, d.Id)));
            micCombo = NewCombo(mics.ToArray(), draft.MicDevice, value => draft.MicDevice = (string)value);
            micCombo.Enabled = draft.RecordMic;
            AddRow(panel, ref y, "Microphone", micCombo);

            noiseCombo = NewCombo(new[]
            {
                new Choice("Off", Settings.NoiseOff),
                new Choice("RNNoise (better quality, more CPU)", Settings.NoiseRnnoise),
                new Choice("Speex (lower quality, less CPU)", Settings.NoiseSpeex),
            }, draft.NoiseSuppression, value => draft.NoiseSuppression = (string)value);
            noiseCombo.Enabled = draft.RecordMic;
            AddRow(panel, ref y, "Mic noise suppression", noiseCombo);

            var outputs = new List<Choice> { new Choice("Windows default", Settings.DeviceDefault) };
            outputs.AddRange(AudioDevices.Outputs().Select(d => new Choice(d.Name, d.Id)));
            outputs.Add(new Choice("Off", Settings.DeviceOff));
            AddRow(panel, ref y, "Game and desktop audio", NewCombo(outputs.ToArray(), draft.DesktopDevice, value => draft.DesktopDevice = (string)value));

            AddToggleRow(panel, ref y, "Separate audio tracks", draft.SeparateTracks, value => draft.SeparateTracks = value);
            panel.Controls.Add(NewLabel("Keeps a mix plus separate desktop and mic tracks for editing. Clips still play normally.", new Point(ControlLeft, y), new Size(ControlWidth, 40), Theme.Muted, 8.75F));
            return panel;
        }

        private Panel BuildStartupTab()
        {
            Panel panel = NewTabPanel();
            int y = 0;

            AddToggleRow(panel, ref y, "Start at login", draft.StartAtLogin, value => draft.StartAtLogin = value);
            AddToggleRow(panel, ref y, "Run OBS as admin", draft.RunAsAdmin, value => draft.RunAsAdmin = value);
            panel.Controls.Add(NewLabel("Lets OBS capture games running as admin and gives it more GPU priority. Changing this asks for admin approval.", new Point(ControlLeft, y), new Size(ControlWidth, 56), Theme.Muted, 8.75F));
            y += 58;
            AddToggleRow(panel, ref y, "Turn off Game Bar capture", draft.GameBarOff, value => draft.GameBarOff = value);
            return panel;
        }

        private void UpdateCaptureHelp()
        {
            bool desktop = draft.Capture == Settings.CaptureDesktop;
            captureHelp.Text = desktop
                ? "Records the whole monitor without touching the game. Notifications and other windows show up in clips."
                : "Clean footage of just the game. It hooks into fullscreen games, so some anti-cheats may block it or object.";
            monitorCombo.Enabled = desktop;
        }

        private void AddTab(string title, Panel panel)
        {
            int index = tabButtons.Count;
            Button button = NewButton(title, new Point(PanelLeft + index * 123, 164), new Size(123, 34));
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Theme.Background;
            button.Click += (_, __) => SelectTab(index);
            tabButtons.Add(button);
            tabPanels.Add(panel);
            Controls.Add(button);
            Controls.Add(panel);
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < tabPanels.Count; i++)
            {
                tabPanels[i].Visible = i == index;
                tabButtons[i].ForeColor = i == index ? Theme.Text : Theme.Muted;
            }
            Button selected = tabButtons[index];
            tabMarker.Location = new Point(selected.Left, selected.Bottom);
            tabMarker.Width = selected.Width;
            tabMarker.BringToFront();
        }

        private Panel NewTabPanel()
        {
            return new Panel
            {
                Location = new Point(PanelLeft, PanelTop),
                Size = new Size(PanelWidth, PanelHeight),
                BackColor = Theme.Background,
            };
        }

        private void AddRow(Panel panel, ref int y, string label, Control control, int width = ControlWidth)
        {
            panel.Controls.Add(NewLabel(label, new Point(0, y + 6), new Size(LabelWidth, 24), Theme.Text, 9.75F));
            control.Location = new Point(ControlLeft, y + 4);
            control.Width = width;
            panel.Controls.Add(control);
            y += RowHeight;
        }

        private void AddToggleRow(Panel panel, ref int y, string label, bool value, Action<bool> changed)
        {
            panel.Controls.Add(NewLabel(label, new Point(0, y + 6), new Size(LabelWidth, 24), Theme.Text, 9.75F));
            var toggle = new ToggleSwitch { Location = new Point(ControlLeft, y + 5), Checked = value };
            toggle.Toggled += (_, __) =>
            {
                changed(toggle.Checked);
                UpdateDirty();
            };
            panel.Controls.Add(toggle);
            y += RowHeight;
        }

        private FlatCombo NewCombo(Choice[] choices, object current, Action<object> changed)
        {
            var combo = new FlatCombo { Width = ControlWidth };
            combo.Items.AddRange(choices.Cast<object>().ToArray());
            int index = Array.FindIndex(choices, choice => Equals(choice.Value, current));
            combo.SelectedIndex = index >= 0 ? index : 0;
            combo.SelectedIndexChanged += (_, __) =>
            {
                changed(((Choice)combo.SelectedItem).Value);
                UpdateDirty();
            };
            return combo;
        }

        private HotkeyBox NewHotkeyBox(string current, Action<Hotkey> changed)
        {
            var box = new HotkeyBox { Text = current };
            box.CaptureChanged += capturing => owner.PauseOverlayHotkey(capturing);
            box.Captured += hotkey =>
            {
                ShowMessage("");
                changed(hotkey);
                UpdateDirty();
            };
            return box;
        }

        private static Label NewLabel(string text, Point location, Size size, Color color, float fontSize, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = size,
                ForeColor = color,
                Font = new Font("Segoe UI", fontSize, style),
                BackColor = Color.Transparent,
            };
        }

        private static Button NewButton(string text, Point location, Size size)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                UseVisualStyleBackColor = false,
                TabStop = false,
            };
            button.FlatAppearance.BorderColor = Theme.Border;
            return button;
        }

        private void PickFolder(TextBox folder)
        {
            dialogOpen = true;
            using (var dialog = new FolderBrowserDialog { SelectedPath = draft.ClipsDir, Description = "Where should clips be saved?" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    draft.ClipsDir = dialog.SelectedPath;
                    folder.Text = dialog.SelectedPath;
                    UpdateDirty();
                }
            }
            dialogOpen = false;
            Activate();
        }

        private async void ToggleReplay()
        {
            bool on = replaySwitch.Checked;
            switching = true;
            replaySwitch.Enabled = false;
            ignoreStatusUntil = DateTime.UtcNow.AddSeconds(StatusGraceSeconds);
            string failure = await RunSafely(() => ObsControl.SetReplay(ctx, on));
            switching = false;
            draft.ReplayEnabled = on;
            saved.ReplayEnabled = on;
            if (IsDisposed)
            {
                return;
            }
            if (failure != null)
            {
                ignoreStatusUntil = DateTime.MinValue;
                ShowMessage(failure);
            }
            UpdateStatus();
        }

        private async void ApplyChanges()
        {
            if (draft.SaveHotkey == draft.OverlayHotkey)
            {
                ShowMessage("The save and overlay hotkeys can't be the same.");
                return;
            }

            busy = true;
            applyButton.Enabled = false;
            ShowMessage(saved.NeedsObsRestart(draft) ? "Applying. Recording restarts for a few seconds..." : "Applying...");

            Settings next = draft.Clone();
            string failure = await RunSafely(() => ObsControl.Apply(ctx, next));

            busy = false;
            if (IsDisposed)
            {
                return;
            }
            if (failure == null)
            {
                saved = Settings.Load(ctx);
                owner.OnApplied(saved);
                ShowMessage("Saved.");
            }
            else
            {
                ShowMessage(failure);
            }
            UpdateDirty();
            UpdateStatus();
        }

        private async Task<string> RunSafely(Action work)
        {
            try
            {
                await Task.Run(work);
                return null;
            }
            catch (SetupException e)
            {
                ctx.Log(e.Message);
                return e.Message;
            }
            catch (Exception e)
            {
                ctx.Log(e.ToString());
                return "Something went wrong: " + e.Message;
            }
        }

        private void UpdateDirty()
        {
            if (applyButton == null)
            {
                return;
            }
            bool dirty = !draft.SameAs(saved);
            applyButton.Enabled = dirty && !busy;
            applyButton.BackColor = dirty ? Theme.Accent : Theme.Panel;
            applyButton.ForeColor = dirty ? Theme.Text : Theme.Muted;
        }

        private void ShowMessage(string text)
        {
            messageLabel.Text = text;
        }

        private void UpdateStatus()
        {
            ObsStatus status = ObsControl.ReadStatus(ctx);
            if (!status.ObsRunning)
            {
                statusLabel.Text = "OBS isn't running";
            }
            else if (!status.ScriptRunning)
            {
                statusLabel.Text = Obs.VisiblePopup(ctx) != null ? "OBS is waiting on a popup" : "OBS is on another profile";
            }
            else if (!status.Replay)
            {
                statusLabel.Text = "Not recording";
            }
            else if (!string.IsNullOrEmpty(status.Game))
            {
                statusLabel.Text = "Recording " + status.Game;
            }
            else
            {
                statusLabel.Text = saved.Capture == Settings.CaptureDesktop ? "Recording monitor" : "Recording, waiting for a game";
            }

            // obs takes a few seconds to catch up after a click, so the switch doesn't flick back
            if (DateTime.UtcNow >= ignoreStatusUntil || status.Replay == replaySwitch.Checked)
            {
                replaySwitch.Checked = status.Replay;
            }
            replaySwitch.Enabled = !switching && (!status.ObsRunning || status.ScriptRunning);
            replayDetail.Text = status.Replay ? "Keeping the last " + FormatLength(saved.ClipSeconds) : "Off, nothing is being kept";
        }

        private static string FormatLength(int seconds)
        {
            if (seconds < 60)
            {
                return seconds + " seconds";
            }
            int minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : minutes + " minutes";
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
