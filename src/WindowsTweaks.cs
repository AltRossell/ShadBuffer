using System;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace ShadBuffer
{
    public static class WindowsTweaks
    {
        private const string GameConfigStore = @"System\GameConfigStore";
        private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        private const string GpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string GameDvrEnabled = "GameDVR_Enabled";
        private const string AppCaptureEnabled = "AppCaptureEnabled";
        private const string HighPerformance = "GpuPreference=2;";

        public static void TurnOffGameBar(State state)
        {
            Remember(state, GameConfigStore, GameDvrEnabled);
            Remember(state, GameDvr, AppCaptureEnabled);
            SetDword(GameConfigStore, GameDvrEnabled, 0);
            SetDword(GameDvr, AppCaptureEnabled, 0);
        }

        public static void RestoreGameBar(State state)
        {
            RestoreDword(state, GameConfigStore, GameDvrEnabled);
            RestoreDword(state, GameDvr, AppCaptureEnabled);
        }

        public static void SetGpuPreference(State state, string obsExe)
        {
            Remember(state, GpuPreferences, obsExe);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(GpuPreferences))
            {
                key.SetValue(obsExe, HighPerformance, RegistryValueKind.String);
            }
        }

        public static void RestoreGpuPreference(State state)
        {
            if (string.IsNullOrEmpty(state.ObsExe))
            {
                return;
            }
            string previous = Previous(state, GpuPreferences, state.ObsExe);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(GpuPreferences))
            {
                if (previous == null)
                {
                    key.DeleteValue(state.ObsExe, false);
                }
                else
                {
                    key.SetValue(state.ObsExe, previous, RegistryValueKind.String);
                }
            }
        }

        // starts the tray helper at login, which then starts obs, none of this needs admin
        public static void SetTrayAtLogin(string exe, bool enabled)
        {
            StartupTask.DeleteTray();
            bool taskMade = enabled && StartupTask.RegisterTray(WindowsIdentity.GetCurrent().User.Value, exe);

            // the slower startup entry is only the fallback if windows won't make the task
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled && !taskMade)
                {
                    key.SetValue(Context.AppName, $"\"{exe}\" --tray --login", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(Context.AppName, false);
                }
            }
        }

        public static void CreateStartMenuShortcut(string exe)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic link = shell.CreateShortcut(StartMenuShortcut());
            link.TargetPath = exe;
            link.Description = "Set up or remove ShadBuffer";
            link.Save();
        }

        public static void DeleteStartMenuShortcut()
        {
            string path = StartMenuShortcut();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string StartMenuShortcut()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Context.AppName + ".lnk");
        }

        private static string StateKey(string path, string value)
        {
            return path + "|" + value;
        }

        // only the first run counts, later runs would just remember my own values
        private static void Remember(State state, string path, string value)
        {
            string key = StateKey(path, value);
            if (state.Registry.ContainsKey(key))
            {
                return;
            }
            using (RegistryKey registry = Registry.CurrentUser.OpenSubKey(path))
            {
                state.Registry[key] = registry?.GetValue(value)?.ToString();
            }
        }

        private static string Previous(State state, string path, string value)
        {
            return state.Registry.TryGetValue(StateKey(path, value), out string previous) ? previous : null;
        }

        private static void SetDword(string path, string value, int data)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue(value, data, RegistryValueKind.DWord);
            }
        }

        private static void RestoreDword(State state, string path, string value)
        {
            if (!state.Registry.ContainsKey(StateKey(path, value)))
            {
                return;
            }
            string previous = Previous(state, path, value);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
            {
                if (previous == null)
                {
                    key.DeleteValue(value, false);
                }
                else
                {
                    key.SetValue(value, int.Parse(previous), RegistryValueKind.DWord);
                }
            }
        }
    }
}
