using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ShadBuffer
{
    public static class ProfileWriter
    {
        private const string SceneName = "Clips";
        private const string GameSource = "Game";
        private const string DesktopSource = "Desktop";
        private const string FallbackSaveHotkey = "Alt+F10";

        // with separate tracks, 1 is the mix, 2 is desktop, 3 is mic
        private const int SplitDesktopMixers = 3;
        private const int SplitMicMixers = 5;
        private const int SplitRecordTracks = 7;
        private const int SingleTrack = 1;

        public static void WriteProfile(Context ctx, Settings settings, EncoderChoice encoder, DisplayInfo canvas)
        {
            Directory.CreateDirectory(ctx.ProfileDir);
            Directory.CreateDirectory(settings.ClipsDir);
            Directory.CreateDirectory(settings.RecordingsDir);
            string clips = settings.ClipsDir.Replace('\\', '/');
            int tracks = settings.SeparateTracks ? SplitRecordTracks : SingleTrack;
            int memoryMb = SystemInfo.ReplayMemoryMb(settings.ClipSeconds);
            Hotkey hotkey = Hotkey.Parse(settings.SaveHotkey) ?? Hotkey.Parse(FallbackSaveHotkey);
            OutputSize(canvas, settings.OutputHeight, out int outputWidth, out int outputHeight);
            // hybrid survives a crash, but it flags every track as default so players pick the wrong one
            string format = settings.SeparateTracks ? "mp4" : "hybrid_mp4";

            string ini = $@"[General]
Name={Context.AppName}

[Output]
Mode=Advanced
FilenameFormatting=%CCYY-%MM-%DD %hh-%mm-%ss

[SimpleOutput]
FilePath={clips}
RecRBPrefix=Replay

[AdvOut]
Encoder={encoder.Id}
RecType=Standard
RecFilePath={clips}
RecFormat2={format}
RecUseRescale=false
RecTracks={tracks}
RecEncoder={encoder.Id}
RecAudioEncoder=ffmpeg_aac
RecRB=true
RecRBTime={settings.ClipSeconds}
RecRBSize={memoryMb}
Track1Bitrate=160
Track2Bitrate=160
Track3Bitrate=160
Track1Name=Mix
Track2Name=Desktop
Track3Name=Mic

[Video]
BaseCX={canvas.Width}
BaseCY={canvas.Height}
OutputCX={outputWidth}
OutputCY={outputHeight}
FPSType=1
FPSInt={settings.Fps}
ScaleType=bicubic
ColorFormat=NV12
ColorSpace=709
ColorRange=Partial

[Audio]
SampleRate=48000
ChannelSetup=Stereo

[Hotkeys]
ReplayBuffer={hotkey.ToObsJson()}
".Replace("\r\n", "\n");

            File.WriteAllText(Path.Combine(ctx.ProfileDir, "basic.ini"), ini, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(ctx.ProfileDir, "recordEncoder.json"), new JavaScriptSerializer().Serialize(encoder.Settings));
        }

        public static void WriteCollection(Context ctx, Settings settings, DisplayInfo canvas)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ctx.CollectionPath));

            bool captureGame = settings.Capture != Settings.CaptureDesktop;
            string desktopMonitor = captureGame ? SystemInfo.PrimaryDisplay().MonitorId : canvas.MonitorId;

            string desktopUuid = NewUuid();
            var sources = new List<object>();
            var items = new List<object> { SceneItem(1, DesktopSource, desktopUuid, canvas) };

            sources.Add(Source("monitor_capture", DesktopSource, desktopUuid, new Dictionary<string, object>
            {
                { "monitor_id", desktopMonitor },
                { "capture_cursor", true },
            }, 0));

            // desktop mode leaves the game source out completely so nothing can ever hook a game
            if (captureGame)
            {
                string gameUuid = NewUuid();
                sources.Add(Source("game_capture", GameSource, gameUuid, new Dictionary<string, object>
                {
                    { "capture_mode", "any_fullscreen" },
                    { "capture_cursor", true },
                    { "capture_overlays", false },
                    { "anti_cheat_hook", true },
                    { "allow_transparency", false },
                    { "limit_framerate", false },
                    { "capture_audio", false },
                }, 0));
                items.Add(SceneItem(2, GameSource, gameUuid, canvas));
            }

            // the first item draws at the bottom so the game sits over the desktop
            sources.Insert(0, Source("scene", SceneName, NewUuid(), new Dictionary<string, object>
            {
                { "id_counter", items.Count },
                { "custom_size", false },
                { "items", items },
            }, 0));

            var collection = new Dictionary<string, object>
            {
                { "name", Context.AppName },
                { "sources", sources },
                { "groups", new object[0] },
                { "scene_order", new object[] { new Dictionary<string, object> { { "name", SceneName } } } },
                { "current_scene", SceneName },
                { "current_program_scene", SceneName },
                { "current_transition", "Fade" },
                { "transition_duration", 300 },
                { "transitions", new object[0] },
                { "quick_transitions", new object[0] },
                { "saved_projectors", new object[0] },
                { "preview_locked", false },
                { "scaling_enabled", false },
                { "scaling_level", 0 },
                { "scaling_off_x", 0.0 },
                { "scaling_off_y", 0.0 },
                { "modules", new Dictionary<string, object>
                    {
                        { "scripts-tool", new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "path", ctx.ScriptPath.Replace('\\', '/') },
                                    { "settings", new Dictionary<string, object>
                                        {
                                            { "capture_game", captureGame },
                                            { "sort_by_game", settings.SortByGame },
                                            { "save_sound", settings.SaveSound },
                                            { "recordings_dir", settings.RecordingsDir.Replace('\\', '/') },
                                        }
                                    },
                                },
                            }
                        },
                    }
                },
                { "resolution", new Dictionary<string, object> { { "x", canvas.Width }, { "y", canvas.Height } } },
                { "version", 2 },
            };

            if (settings.DesktopDevice != Settings.DeviceOff)
            {
                collection["DesktopAudioDevice1"] = Source("wasapi_output_capture", "Desktop Audio", NewUuid(), Device(settings.DesktopDevice), settings.SeparateTracks ? SplitDesktopMixers : SingleTrack);
            }

            // always there and just muted, so the overlay can flip it without restarting obs
            var mic = Source("wasapi_input_capture", "Mic/Aux", NewUuid(), Device(settings.MicDevice), settings.SeparateTracks ? SplitMicMixers : SingleTrack);
            mic["muted"] = !settings.RecordMic;
            if (settings.NoiseSuppression != Settings.NoiseOff)
            {
                mic["filters"] = new object[] { NoiseFilter(settings.NoiseSuppression) };
            }
            collection["AuxAudioDevice1"] = mic;

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(ctx.CollectionPath, serializer.Serialize(collection));
        }

        public static void WriteAssets(Context ctx)
        {
            Directory.CreateDirectory(ctx.AppDir);
            CopyResource("shadbuffer.lua", ctx.ScriptPath);
            CopyResource("saved.wav", ctx.SoundPath);
        }

        public static void OutputSize(DisplayInfo canvas, int height, out int outputWidth, out int outputHeight)
        {
            if (height <= 0 || height >= canvas.Height)
            {
                outputWidth = canvas.Width;
                outputHeight = canvas.Height;
                return;
            }
            outputHeight = height;
            outputWidth = (int)Math.Round(canvas.Width * (double)height / canvas.Height);
            outputWidth -= outputWidth % 2;
        }

        private static void CopyResource(string name, string target)
        {
            using (Stream input = typeof(ProfileWriter).Assembly.GetManifestResourceStream(name))
            using (Stream output = File.Create(target))
            {
                input.CopyTo(output);
            }
        }

        private static string NewUuid()
        {
            return Guid.NewGuid().ToString();
        }

        private static Dictionary<string, object> Device(string id)
        {
            return new Dictionary<string, object> { { "device_id", id } };
        }

        private static Dictionary<string, object> Source(string id, string name, string uuid, Dictionary<string, object> settings, int mixers)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "uuid", uuid },
                { "id", id },
                { "versioned_id", id },
                { "settings", settings },
                { "mixers", mixers },
                { "sync", 0 },
                { "flags", 0 },
                { "volume", 1.0 },
                { "balance", 0.5 },
                { "enabled", true },
                { "muted", false },
                { "hotkeys", new Dictionary<string, object>() },
                { "monitoring_type", 0 },
                { "private_settings", new Dictionary<string, object>() },
            };
        }

        private static Dictionary<string, object> NoiseFilter(string method)
        {
            var settings = new Dictionary<string, object> { { "method", method } };
            Dictionary<string, object> filter = Source("noise_suppress_filter", "Noise Suppression", NewUuid(), settings, 0);
            filter["versioned_id"] = "noise_suppress_filter_v2";
            return filter;
        }

        private static Dictionary<string, object> SceneItem(int id, string name, string uuid, DisplayInfo canvas)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "source_uuid", uuid },
                { "visible", true },
                { "locked", false },
                { "rot", 0.0 },
                { "align", 5 },
                { "bounds_type", 2 },
                { "bounds_align", 0 },
                { "bounds_crop", false },
                { "crop_left", 0 },
                { "crop_top", 0 },
                { "crop_right", 0 },
                { "crop_bottom", 0 },
                { "id", id },
                { "group_item_backup", false },
                { "pos", Vector(0, 0) },
                { "scale", Vector(1, 1) },
                { "bounds", Vector(canvas.Width, canvas.Height) },
                { "scale_filter", "disable" },
                { "blend_method", "default" },
                { "blend_type", "normal" },
                { "private_settings", new Dictionary<string, object>() },
            };
        }

        private static Dictionary<string, object> Vector(double x, double y)
        {
            return new Dictionary<string, object> { { "x", x }, { "y", y } };
        }
    }
}
