using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShadOBS
{
    // stays in the tray, listens for the overlay hotkey and watches obs
    public sealed class Companion : ApplicationContext
    {
        private const string MutexName = @"Local\ShadOBS.Companion";
        private const string QuitEventName = @"Local\ShadOBS.Quit";
        private const int OverlayHotkeyId = 1;
        private const int TestHotkeyId = 2;
        private const int WmHotkey = 0x0312;
        private const int QuitWaitMs = 5000;
        private const int SaveDelayMs = 300;

        private readonly Context ctx;
        private readonly NotifyIcon tray;
        private readonly HotkeyWindow window;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle quitEvent;
        private readonly Icon iconOn = MakeIcon(Color.FromArgb(235, 64, 64));
        private readonly Icon iconOff = MakeIcon(Color.FromArgb(120, 120, 128));

        private OverlayForm overlay;
        private Settings draft;
        private int checkedObsId;

        public Companion(Context ctx)
        {
            this.ctx = ctx;
            quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);

            window = new HotkeyWindow(ToggleOverlay);
            tray = new NotifyIcon { Icon = iconOff, Text = Context.AppName, Visible = true };
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleOverlay();
                }
            };

            RegisterOverlayHotkey();

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, __) => Tick();
            timer.Start();
        }

        public static int Run(Context ctx, bool atLogin)
        {
            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first)
                {
                    return 0;
                }
                using (var companion = new Companion(ctx))
                {
                    if (atLogin)
                    {
                        companion.StartObsAtLogin();
                    }
                    Application.Run(companion);
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        // left off stays off across a reboot, so obs only starts if recording is meant to be on
        public void StartObsAtLogin()
        {
            Task.Run(() =>
            {
                try
                {
                    if (Settings.Load(ctx).ReplayEnabled && !Obs.IsRunning(ctx))
                    {
                        Setup.StartObs(ctx, Obs.FindRoot(ctx));
                    }
                }
                catch (Exception e)
                {
                    ctx.Log(e.ToString());
                    tray.ShowBalloonTip(5000, Context.AppName, "OBS didn't start, so nothing is recording. Press the overlay hotkey to try again.", ToolTipIcon.Warning);
                }
            });
        }

        public static bool IsRunning()
        {
            return Mutex.TryOpenExisting(MutexName, out Mutex mutex) && Dispose(mutex);
        }

        public static void EnsureRunning(Context ctx)
        {
            if (!IsRunning() && File.Exists(ctx.InstalledExe))
            {
                // run from my own folder or the helper locks whatever folder setup ran from
                Process.Start(new ProcessStartInfo(ctx.InstalledExe, "--tray")
                {
                    UseShellExecute = true,
                    WorkingDirectory = ctx.AppDir,
                })?.Dispose();
            }
        }

        // setup has to overwrite my exe, so it asks the running one to get out of the way
        public static void RequestQuit()
        {
            if (!EventWaitHandle.TryOpenExisting(QuitEventName, out EventWaitHandle quit))
            {
                return;
            }
            using (quit)
            {
                quit.Set();
            }
            Stopwatch wait = Stopwatch.StartNew();
            while (IsRunning() && wait.ElapsedMilliseconds < QuitWaitMs)
            {
                Thread.Sleep(100);
            }
        }

        public void ToggleOverlay()
        {
            if (overlay != null)
            {
                overlay.CloseIfIdle();
                return;
            }
            draft = draft ?? Settings.Load(ctx);
            overlay = new OverlayForm(ctx, draft, this);
            // closing the form skips the box's leave event, so the hotkey comes back here too
            overlay.FormClosed += (_, __) =>
            {
                overlay = null;
                RegisterOverlayHotkey();
            };
            overlay.Show();
        }

        public void PauseOverlayHotkey(bool paused)
        {
            if (paused)
            {
                UnregisterHotKey(window.Handle, OverlayHotkeyId);
            }
            else
            {
                RegisterOverlayHotkey();
            }
        }

        // tells the overlay if another app already owns the combo
        public bool IsHotkeyFree(Hotkey hotkey)
        {
            if (!RegisterHotKey(window.Handle, TestHotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
            {
                return false;
            }
            UnregisterHotKey(window.Handle, TestHotkeyId);
            return true;
        }

        public void OnApplied(Settings saved)
        {
            draft = null;
            RegisterOverlayHotkey();
        }

        public void SaveClip()
        {
            overlay?.CloseIfIdle();
            Task.Run(async () =>
            {
                // the game needs focus back first so the clip gets sorted under it
                await Task.Delay(SaveDelayMs);
                if (!ObsControl.Send(ctx, ObsControl.Save))
                {
                    tray.ShowBalloonTip(3000, Context.AppName, "Couldn't save a clip because OBS isn't recording on the ShadOBS profile.", ToolTipIcon.Warning);
                }
            });
        }

        public void OpenClips()
        {
            overlay?.CloseIfIdle();
            string folder = Settings.Load(ctx).ClipsDir;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tray.Visible = false;
                tray.Dispose();
                UnregisterHotKey(window.Handle, OverlayHotkeyId);
                window.DestroyHandle();
                quitEvent.Dispose();
                iconOn.Dispose();
                iconOff.Dispose();
            }
            base.Dispose(disposing);
        }

        private static bool Dispose(IDisposable item)
        {
            item.Dispose();
            return true;
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open overlay", null, (_, __) => ToggleOverlay());
            menu.Items.Add("Save clip", null, (_, __) => SaveClip());
            menu.Items.Add("Open clips", null, (_, __) => OpenClips());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Set up or remove...", null, (_, __) => OpenSetupWindow());
            menu.Items.Add("Quit", null, (_, __) => QuitAll());
            return menu;
        }

        private void OpenSetupWindow()
        {
            if (File.Exists(ctx.InstalledExe))
            {
                Process.Start(new ProcessStartInfo(ctx.InstalledExe) { UseShellExecute = true })?.Dispose();
            }
        }

        private void QuitAll()
        {
            if (Obs.IsRunning(ctx))
            {
                ObsControl.Send(ctx, ObsControl.Quit);
            }
            ExitThread();
        }

        // windows keeps the old combo if the same id registers twice, so i always clear it first
        private void RegisterOverlayHotkey()
        {
            UnregisterHotKey(window.Handle, OverlayHotkeyId);
            Hotkey hotkey = Hotkey.Parse(Settings.Load(ctx).OverlayHotkey);
            if (hotkey == null || !RegisterHotKey(window.Handle, OverlayHotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
            {
                tray.ShowBalloonTip(5000, Context.AppName, "The overlay hotkey is taken by another app. Open the overlay from this tray icon to change it.", ToolTipIcon.Warning);
            }
        }

        private void Tick()
        {
            if (quitEvent.WaitOne(0))
            {
                overlay?.Close();
                ExitThread();
                return;
            }

            ObsStatus status = ObsControl.ReadStatus(ctx);
            tray.Icon = status.Replay ? iconOn : iconOff;
            string text = status.Replay ? "Recording" : status.ObsRunning ? "Replay off" : "OBS isn't running";
            tray.Text = Truncate(Context.AppName + ": " + text);

            EnforceReplayOff(status);
        }

        // obs always starts its buffer at login, so i turn it back off if the user left it off
        private void EnforceReplayOff(ObsStatus status)
        {
            if (!status.ScriptRunning || !status.Replay)
            {
                return;
            }
            int id = Obs.ProcessId(ctx);
            if (id == checkedObsId)
            {
                return;
            }
            checkedObsId = id;
            if (!Settings.Load(ctx).ReplayEnabled)
            {
                Task.Run(() => ObsControl.Send(ctx, ObsControl.Stop));
            }
        }

        private static string Truncate(string text)
        {
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private static Icon MakeIcon(Color dot)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var ring = new SolidBrush(Color.FromArgb(39, 39, 42)))
            using (var fill = new SolidBrush(dot))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(ring, 1, 1, 30, 30);
                graphics.FillEllipse(fill, 8, 8, 16, 16);
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly Action pressed;

            public HotkeyWindow(Action pressed)
            {
                this.pressed = pressed;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmHotkey && message.WParam.ToInt32() == OverlayHotkeyId)
                {
                    pressed();
                }
                base.WndProc(ref message);
            }
        }
    }
}
