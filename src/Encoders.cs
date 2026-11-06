using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShadBuffer
{
    public sealed class EncoderChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public Dictionary<string, object> Settings { get; set; }
    }

    public sealed class EncoderSupport
    {
        public string Brand { get; set; } = Encoders.BrandCpu;
        public bool Hevc { get; set; }
        public bool Av1 { get; set; }
    }

    public static class Encoders
    {
        public const string BrandNvidia = "nvidia";
        public const string BrandAmd = "amd";
        public const string BrandIntel = "intel";
        public const string BrandCpu = "cpu";

        public const string CodecH264 = "h264";
        public const string CodecHevc = "hevc";
        public const string CodecAv1 = "av1";
        public const string CodecCpu = "cpu";

        public static EncoderSupport Detect(string obsRoot, Context ctx)
        {
            string bin = Path.Combine(obsRoot, "bin", "64bit");

            string nvenc = Probe(Path.Combine(bin, "obs-nvenc-test.exe"), ctx);
            if (NvencWorks(nvenc))
            {
                return new EncoderSupport { Brand = BrandNvidia, Hevc = NvencSupports(nvenc, "hevc"), Av1 = NvencSupports(nvenc, "av1") };
            }

            string amf = Probe(Path.Combine(bin, "obs-amf-test.exe"), ctx);
            if (AmfWorks(amf))
            {
                return new EncoderSupport { Brand = BrandAmd, Hevc = AdapterSupports(amf, "supports_hevc"), Av1 = AdapterSupports(amf, "supports_av1") };
            }

            string qsv = Probe(Path.Combine(bin, "obs-qsv-test.exe"), ctx);
            if (QsvWorks(qsv))
            {
                return new EncoderSupport { Brand = BrandIntel, Hevc = AdapterSupports(qsv, "supports_hevc"), Av1 = AdapterSupports(qsv, "supports_av1") };
            }

            return new EncoderSupport();
        }

        public static bool NvencWorks(string output)
        {
            return Value(ParseSections(output), "general", "nvenc_supported") == "true" && NvencSupports(output, "h264");
        }

        public static bool NvencSupports(string output, string codec)
        {
            return Value(ParseSections(output), codec, "codec_supported") == "1";
        }

        public static bool AmfWorks(string output)
        {
            return ParseSections(output).Values.Any(section => Is(section, "is_amd") && Is(section, "supports_avc"));
        }

        public static bool QsvWorks(string output)
        {
            return ParseSections(output).Values.Any(section => Is(section, "is_intel"));
        }

        public static bool AdapterSupports(string output, string key)
        {
            return ParseSections(output).Values.Any(section => Is(section, key));
        }

        public static EncoderChoice Build(string brand, bool hevc, bool av1, string codec, int quality)
        {
            if (codec == CodecCpu || brand == BrandCpu)
            {
                return X264(quality);
            }
            if ((codec == CodecHevc && !hevc) || (codec == CodecAv1 && !av1))
            {
                codec = CodecH264;
            }
            switch (brand)
            {
                case BrandNvidia:
                    return Nvenc(codec, quality);
                case BrandAmd:
                    return Amf(codec, quality);
                case BrandIntel:
                    return Qsv(codec, quality);
                default:
                    return X264(quality);
            }
        }

        private static EncoderChoice Nvenc(string codec, int quality)
        {
            return new EncoderChoice
            {
                Id = codec == CodecAv1 ? "obs_nvenc_av1_tex" : codec == CodecHevc ? "obs_nvenc_hevc_tex" : "obs_nvenc_h264_tex",
                Label = "NVIDIA NVENC",
                Settings = new Dictionary<string, object>
                {
                    { "rate_control", "CQP" },
                    { "cqp", quality },
                    { "preset", "p5" },
                    { "tune", "hq" },
                    { "multipass", "disabled" },
                    { "profile", codec == CodecH264 ? "high" : "main" },
                    { "keyint_sec", 2 },
                    { "bf", 2 },
                    { "lookahead", false },
                    { "gpu", 0 },
                },
            };
        }

        private static EncoderChoice Amf(string codec, int quality)
        {
            return new EncoderChoice
            {
                Id = codec == CodecAv1 ? "av1_texture_amf" : codec == CodecHevc ? "h265_texture_amf" : "h264_texture_amf",
                Label = "AMD AMF",
                Settings = new Dictionary<string, object>
                {
                    { "rate_control", "CQP" },
                    { "cqp", quality },
                    { "preset", "quality" },
                    { "profile", codec == CodecH264 ? "high" : "main" },
                    { "keyint_sec", 2 },
                    { "bf", 2 },
                },
            };
        }

        private static EncoderChoice Qsv(string codec, int quality)
        {
            return new EncoderChoice
            {
                Id = codec == CodecAv1 ? "obs_qsv11_av1" : codec == CodecHevc ? "obs_qsv11_hevc" : "obs_qsv11_v2",
                Label = "Intel Quick Sync",
                Settings = new Dictionary<string, object>
                {
                    { "rate_control", "CQP" },
                    { "cqp", quality },
                    { "target_usage", "TU4" },
                    { "profile", codec == CodecH264 ? "high" : "main" },
                    { "keyint_sec", 2 },
                    { "bframes", 2 },
                },
            };
        }

        private static EncoderChoice X264(int quality)
        {
            return new EncoderChoice
            {
                Id = "obs_x264",
                Label = "x264 (CPU)",
                Settings = new Dictionary<string, object>
                {
                    { "rate_control", "CRF" },
                    { "crf", quality },
                    { "preset", "veryfast" },
                    { "profile", "high" },
                    { "keyint_sec", 2 },
                },
            };
        }

        private static string Probe(string exe, Context ctx)
        {
            if (!File.Exists(exe))
            {
                return "";
            }
            try
            {
                int code = Shell.Run(exe, "", 20000, out string output);
                ctx.Log($"{Path.GetFileName(exe)} exit {code}");
                return output;
            }
            catch (Exception e)
            {
                ctx.Log($"{Path.GetFileName(exe)} failed: {e.Message}");
                return "";
            }
        }

        private static bool Is(Dictionary<string, string> section, string key)
        {
            return section.TryGetValue(key, out string value) && value == "true";
        }

        private static string Value(Dictionary<string, Dictionary<string, string>> sections, string section, string key)
        {
            return sections.TryGetValue(section, out var values) && values.TryGetValue(key, out string value) ? value : null;
        }

        private static Dictionary<string, Dictionary<string, string>> ParseSections(string output)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (string raw in (output ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = new Dictionary<string, string>();
                    sections[line.Substring(1, line.Length - 2)] = current;
                    continue;
                }
                int equals = line.IndexOf('=');
                if (current != null && equals > 0)
                {
                    current[line.Substring(0, equals)] = line.Substring(equals + 1);
                }
            }
            return sections;
        }
    }
}
