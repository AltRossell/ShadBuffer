using System.IO;
using System.Web.Script.Serialization;

namespace ShadBuffer
{
    public sealed class Settings
    {
        public const string CaptureGame = "game";
        public const string CaptureDesktop = "desktop";
        public const string DeviceDefault = "default";
        public const string DeviceOff = "off";
        public const string NoiseOff = "off";
        public const string NoiseRnnoise = "rnnoise";
        public const string NoiseSpeex = "speex";

        public int ClipSeconds { get; set; } = 120;
        public string SaveHotkey { get; set; } = "Alt+F10";
        public string OverlayHotkey { get; set; } = "Alt+X";
        public string ClipsDir { get; set; }
        public bool SaveSound { get; set; } = true;
        public bool SortByGame { get; set; } = true;

        public string Capture { get; set; } = CaptureGame;
        public string MonitorId { get; set; } = "";
        public int Quality { get; set; } = 20;
        public int Fps { get; set; } = 60;
        public int OutputHeight { get; set; } = 0;
        public string Codec { get; set; } = "h264";

        public bool RecordMic { get; set; } = true;
        public string MicDevice { get; set; } = DeviceDefault;
        public string NoiseSuppression { get; set; } = NoiseOff;
        public string DesktopDevice { get; set; } = DeviceDefault;
        public bool SeparateTracks { get; set; } = true;

        public bool StartAtLogin { get; set; } = true;
        public bool RunAsAdmin { get; set; } = true;
        public bool GameBarOff { get; set; } = true;

        public bool ReplayEnabled { get; set; } = true;

        public static Settings Load(Context ctx)
        {
            Settings settings = File.Exists(ctx.SettingsPath)
                ? new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(ctx.SettingsPath))
                : new Settings();
            if (string.IsNullOrEmpty(settings.ClipsDir))
            {
                settings.ClipsDir = ctx.ClipsDir;
            }
            return settings;
        }

        public void Save(Context ctx)
        {
            Directory.CreateDirectory(ctx.AppDir);
            File.WriteAllText(ctx.SettingsPath, ToJson());
        }

        public Settings Clone()
        {
            return new JavaScriptSerializer().Deserialize<Settings>(ToJson());
        }

        public string ToJson()
        {
            return new JavaScriptSerializer().Serialize(this);
        }

        public bool SameAs(Settings other)
        {
            Settings a = Clone();
            Settings b = other.Clone();
            a.ReplayEnabled = b.ReplayEnabled = true;
            return a.ToJson() == b.ToJson();
        }

        // anything obs only reads at launch, startup and overlay changes don't need a restart
        public bool NeedsObsRestart(Settings other)
        {
            Settings a = Clone();
            Settings b = other.Clone();
            a.ReplayEnabled = b.ReplayEnabled = true;
            a.StartAtLogin = b.StartAtLogin = true;
            a.GameBarOff = b.GameBarOff = true;
            a.OverlayHotkey = b.OverlayHotkey = "";
            return a.ToJson() != b.ToJson();
        }
    }
}
