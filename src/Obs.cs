using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ShadBuffer
{
    public static class Obs
    {
        public static readonly Version MinVersion = new Version(31, 0);
        public const string LaunchArgs = "--profile \"ShadBuffer\" --collection \"ShadBuffer\" --startreplaybuffer --minimize-to-tray";

        private const uint WmClose = 0x0010;
        private const int CloseRetryMs = 1000;
        private const int PopupCloseAttempts = 5;
        private const uint QueryLimitedInformation = 0x1000;

        public static string ExePath(string root)
        {
            return Path.Combine(root, "bin", "64bit", "obs64.exe");
        }

        public static string FindRoot(Context ctx)
        {
            if (ctx.IsSandbox)
            {
                return ctx.SandboxObsRoot;
            }
            return FindInstalledRoot();
        }

        public static string FindInstalledRoot()
        {
            string fromRegistry = null;
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = machine.OpenSubKey(@"SOFTWARE\OBS Studio"))
            {
                fromRegistry = key?.GetValue(null) as string;
            }

            string programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string candidate in new[] { fromRegistry, Path.Combine(programFiles, "obs-studio") })
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(ExePath(candidate)))
                {
                    return candidate;
                }
            }
            return null;
        }

        public static bool IsNewEnough(string root)
        {
            string raw = FileVersionInfo.GetVersionInfo(ExePath(root)).ProductVersion ?? "";
            string numeric = new string(raw.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return Version.TryParse(numeric, out Version version) && version >= MinVersion;
        }

        public static bool IsRunning(Context ctx)
        {
            return Running(ctx).Count > 0;
        }

        public static int ProcessId(Context ctx)
        {
            Process first = Running(ctx).FirstOrDefault();
            return first?.Id ?? 0;
        }

        // obs can hang on its way out, ending its task stops an admin obs without a prompt
        public static void ForceEnd(Context ctx)
        {
            if (!ctx.IsSandbox)
            {
                StartupTask.End();
                return;
            }
            foreach (Process process in Running(ctx))
            {
                process.Kill();
            }
        }

        public static bool WaitForExit(Context ctx, int timeoutMs)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                if (!IsRunning(ctx))
                {
                    return true;
                }
                Thread.Sleep(250);
            }
            return false;
        }

        // asks obs to close the normal way so it doesn't flag a crash on next launch
        public static bool Close(Context ctx, int timeoutMs)
        {
            Stopwatch timer = Stopwatch.StartNew();
            for (int attempt = 1; timer.ElapsedMilliseconds < timeoutMs; attempt++)
            {
                if (!IsRunning(ctx))
                {
                    return true;
                }
                PostClose(ctx, attempt <= PopupCloseAttempts);
                if (WaitForExit(ctx, CloseRetryMs))
                {
                    return true;
                }
            }
            return !IsRunning(ctx);
        }

        // popups go first, closing the main window while one is up can hang obs on its way out
        private static void PostClose(Context ctx, bool popupsFirst)
        {
            var ids = new HashSet<uint>(Running(ctx).Select(p => (uint)p.Id));
            var main = new List<IntPtr>();
            var popups = new List<IntPtr>();

            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint processId);
                if (ids.Contains(processId))
                {
                    if (IsMainWindow(window))
                    {
                        main.Add(window);
                    }
                    else if (IsWindowVisible(window) && HasTitle(window))
                    {
                        popups.Add(window);
                    }
                }
                return true;
            }, IntPtr.Zero);

            foreach (IntPtr window in popupsFirst && popups.Count > 0 ? popups : main)
            {
                PostMessageW(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private static bool HasTitle(IntPtr window)
        {
            var title = new StringBuilder(2);
            return GetWindowTextW(window, title, title.Capacity) > 0;
        }

        // limited rights are enough here, so this works on an admin obs from a non admin app
        public static DateTime? StartTimeUtc(Context ctx)
        {
            int id = ProcessId(ctx);
            if (id == 0)
            {
                return null;
            }
            IntPtr handle = OpenProcess(QueryLimitedInformation, false, id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return GetProcessTimes(handle, out long created, out _, out _, out _)
                    ? DateTime.FromFileTimeUtc(created)
                    : (DateTime?)null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // a visible window that isn't the main one, so the error can say what's in the way
        public static string VisiblePopup(Context ctx)
        {
            var ids = new HashSet<uint>(Running(ctx).Select(p => (uint)p.Id));
            string found = null;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint processId);
                if (ids.Contains(processId) && IsWindowVisible(window) && !IsMainWindow(window))
                {
                    var title = new StringBuilder(256);
                    GetWindowTextW(window, title, title.Capacity);
                    if (title.Length > 0)
                    {
                        found = title.ToString();
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static List<Process> Running(Context ctx)
        {
            Process[] all = Process.GetProcessesByName("obs64");
            if (!ctx.IsSandbox)
            {
                return all.ToList();
            }
            return all.Where(p => IsInside(p, ctx.SandboxObsRoot)).ToList();
        }

        private static bool IsInside(Process process, string root)
        {
            try
            {
                return process.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // the rest of the title is translated so i only match the name and version at the start
        private static bool IsMainWindow(IntPtr window)
        {
            var title = new StringBuilder(256);
            GetWindowTextW(window, title, title.Capacity);
            string text = title.ToString();
            return text.Length > 4 && text.StartsWith("OBS ") && char.IsDigit(text[4]);
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
