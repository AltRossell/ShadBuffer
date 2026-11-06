using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ShadBuffer
{
    public sealed class ObsStatus
    {
        public bool ObsRunning { get; set; }
        public bool ScriptRunning { get; set; }
        public bool Replay { get; set; }
        public bool Recording { get; set; }
        public DateTime? RecordingSinceUtc { get; set; }
        public bool? Mic { get; set; }
        public string Game { get; set; }
    }

    // the lua script picks these commands up from a file, so a non admin app can drive an admin obs
    public static class ObsControl
    {
        public const string Start = "start";
        public const string Stop = "stop";
        public const string Save = "save";
        public const string Quit = "quit";
        public const string RecordStart = "recordstart";
        public const string RecordStop = "recordstop";
        public const string Screenshot = "screenshot";
        public const string MicOn = "micon";
        public const string MicOff = "micoff";

        private const int AckTimeoutMs = 2500;
        private const int ObsExitTimeoutMs = 20000;
        private const int ForceEndWaitMs = 8000;
        private const int ScriptWaitMs = 30000;
        private const string OtherProfile = "OBS is open on a different profile, so ShadBuffer can't control it.";

        public static ObsStatus ReadStatus(Context ctx)
        {
            var status = new ObsStatus { ObsRunning = Obs.IsRunning(ctx) };
            if (!status.ObsRunning)
            {
                return status;
            }

            // a crash leaves the old file saying "on", so anything older than this obs doesn't count
            DateTime? started = Obs.StartTimeUtc(ctx);
            if (started != null && File.GetLastWriteTimeUtc(ctx.StatusPath) < started.Value)
            {
                return status;
            }

            try
            {
                using (var stream = new FileStream(ctx.StatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int equals = line.IndexOf('=');
                        if (equals <= 0)
                        {
                            continue;
                        }
                        string key = line.Substring(0, equals);
                        string value = line.Substring(equals + 1);
                        if (key == "script")
                        {
                            status.ScriptRunning = value == "on";
                        }
                        else if (key == "replay")
                        {
                            status.Replay = value == "on";
                        }
                        else if (key == "recording")
                        {
                            status.Recording = value == "on";
                        }
                        else if (key == "recordstart" && long.TryParse(value, out long seconds) && seconds > 0)
                        {
                            status.RecordingSinceUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                        }
                        else if (key == "mic" && value != "none")
                        {
                            status.Mic = value == "on";
                        }
                        else if (key == "game")
                        {
                            status.Game = value;
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            return status;
        }

        // false means nothing picked it up, usually obs is on a different profile
        public static bool Send(Context ctx, string command)
        {
            Directory.CreateDirectory(ctx.AppDir);
            string temp = ctx.CommandPath + ".tmp";
            File.WriteAllText(temp, command);
            if (File.Exists(ctx.CommandPath))
            {
                File.Delete(ctx.CommandPath);
            }
            File.Move(temp, ctx.CommandPath);

            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < AckTimeoutMs)
            {
                if (!File.Exists(ctx.CommandPath))
                {
                    return true;
                }
                Thread.Sleep(50);
            }

            try
            {
                File.Delete(ctx.CommandPath);
            }
            catch (IOException)
            {
            }
            return false;
        }

        public static void SetReplay(Context ctx, bool on)
        {
            Settings settings = Settings.Load(ctx);
            settings.ReplayEnabled = on;
            settings.Save(ctx);

            if (!Obs.IsRunning(ctx))
            {
                if (on)
                {
                    Setup.StartObs(ctx, Obs.FindRoot(ctx));
                }
                return;
            }
            if (!Send(ctx, on ? Start : Stop))
            {
                throw new SetupException(OtherProfile);
            }
        }

        public static void SetMic(Context ctx, bool on)
        {
            Settings settings = Settings.Load(ctx);
            settings.RecordMic = on;
            settings.Save(ctx);

            if (Obs.IsRunning(ctx) && !Send(ctx, on ? MicOn : MicOff))
            {
                throw new SetupException(OtherProfile);
            }
        }

        public static void ToggleRecording(Context ctx)
        {
            EnsureScriptRunning(ctx);
            if (!Send(ctx, ReadStatus(ctx).Recording ? RecordStop : RecordStart))
            {
                throw new SetupException(OtherProfile);
            }
        }

        public static void TakeScreenshot(Context ctx)
        {
            EnsureScriptRunning(ctx);
            if (!Send(ctx, Screenshot))
            {
                throw new SetupException(OtherProfile);
            }
        }

        // with replay left off obs isn't open after a reboot, but record and screenshot still need it
        private static void EnsureScriptRunning(Context ctx)
        {
            if (!Obs.IsRunning(ctx))
            {
                Setup.StartObs(ctx, Obs.FindRoot(ctx));
            }
            Stopwatch timer = Stopwatch.StartNew();
            while (!ReadStatus(ctx).ScriptRunning)
            {
                if (timer.ElapsedMilliseconds > ScriptWaitMs)
                {
                    throw new SetupException(OtherProfile);
                }
                Thread.Sleep(250);
            }
        }

        public static void Apply(Context ctx, Settings next)
        {
            Settings current = Settings.Load(ctx);
            next.CopyLiveValues(current);
            if (current.NeedsObsRestart(next) && ReadStatus(ctx).Recording)
            {
                throw new SetupException("Stop recording first, applying these changes restarts OBS.");
            }
            State state = State.Load(ctx);

            if (current.RunAsAdmin != next.RunAsAdmin && !ctx.IsSandbox)
            {
                Elevated.Request(ctx, Elevated.ModeTask, false, next);
            }

            if (current.StartAtLogin != next.StartAtLogin && !ctx.IsSandbox)
            {
                WindowsTweaks.SetTrayAtLogin(ctx.InstalledExe, next.StartAtLogin);
            }

            if (current.GameBarOff != next.GameBarOff && !ctx.IsSandbox)
            {
                if (next.GameBarOff)
                {
                    WindowsTweaks.TurnOffGameBar(state);
                }
                else
                {
                    WindowsTweaks.RestoreGameBar(state);
                }
                state.Save(ctx);
            }

            if (current.NeedsObsRestart(next))
            {
                RestartWith(ctx, next, state);
            }
            next.Save(ctx);
        }

        private static void RestartWith(Context ctx, Settings next, State state)
        {
            bool wasRunning = Obs.IsRunning(ctx);
            if (wasRunning)
            {
                if (!Send(ctx, Quit))
                {
                    throw new SetupException("OBS is open on a different profile. Close it and apply again.");
                }
                if (!Obs.WaitForExit(ctx, ObsExitTimeoutMs))
                {
                    string popup = Obs.VisiblePopup(ctx);
                    ctx.Log("OBS is stuck closing, ending it. Popup: " + (popup ?? "none"));
                    Obs.ForceEnd(ctx);
                    if (!Obs.WaitForExit(ctx, ForceEndWaitMs))
                    {
                        throw new SetupException(popup == null
                            ? "OBS didn't close. Close it and apply again."
                            : $"OBS didn't close because \"{popup}\" is open. Close that popup and apply again.");
                    }
                }
            }

            string root = Obs.FindRoot(ctx);
            Setup.WriteObsConfig(ctx, next, state, root);

            if (wasRunning || next.ReplayEnabled)
            {
                DateTime startedUtc = DateTime.UtcNow;
                Setup.StartObs(ctx, root);
                Setup.WaitForReplayBuffer(ctx, startedUtc);
            }
        }
    }
}
