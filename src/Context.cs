using System;
using System.IO;

namespace ShadBuffer
{
    public sealed class Context
    {
        public const string AppName = "ShadBuffer";

        public string ConfigDir { get; set; }
        public string AppDir { get; set; }
        public string ClipsDir { get; set; }
        public string SandboxObsRoot { get; set; }
        public Action<string> Progress { get; set; } = _ => { };

        public bool IsSandbox => SandboxObsRoot != null;
        public string StatePath => Path.Combine(AppDir, "state.json");
        public string SettingsPath => Path.Combine(AppDir, "settings.json");
        public string CommandPath => Path.Combine(AppDir, "command.txt");
        public string StatusPath => Path.Combine(AppDir, "status.txt");
        public string ScriptPath => Path.Combine(AppDir, "shadbuffer.lua");
        public string SoundPath => Path.Combine(AppDir, "saved.wav");
        public string LogPath => Path.Combine(AppDir, "setup.log");
        public string InstalledExe => Path.Combine(AppDir, AppName + ".exe");
        public string ProfileDir => Path.Combine(ConfigDir, "basic", "profiles", AppName);
        public string CollectionPath => Path.Combine(ConfigDir, "basic", "scenes", AppName + ".json");
        public string UserIniPath => Path.Combine(ConfigDir, "user.ini");

        public static Context ForThisUser()
        {
            return new Context
            {
                ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio"),
                AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName),
                ClipsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Clips"),
            };
        }

        // points everything at a portable obs copy so tests never touch the real config
        public static Context ForSandbox(string obsRoot)
        {
            return new Context
            {
                SandboxObsRoot = obsRoot,
                ConfigDir = Path.Combine(obsRoot, "config", "obs-studio"),
                AppDir = Path.Combine(obsRoot, "shadbuffer-app"),
                ClipsDir = Path.Combine(obsRoot, "clips"),
            };
        }

        public void Report(string message)
        {
            Log(message);
            Progress(message);
        }

        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }

    public sealed class SetupException : Exception
    {
        public bool Cancelled { get; }

        public SetupException(string message, bool cancelled = false) : base(message)
        {
            Cancelled = cancelled;
        }
    }
}
