using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShadBuffer
{
    public static class Icons
    {
        public const string Gear = "\uE713";
        public const string Close = "\uE8BB";
        public const string Film = "\uE8B2";
        public const string Video = "\uE714";
        public const string History = "\uE81C";
        public const string Photo = "\uEB9F";
        public const string Camera = "\uE722";
        public const string Mic = "\uE720";
        public const string Play = "\uF5B0";
        public const string Pause = "\uE769";
        public const string Stop = "\uE73B";
        public const string Left = "\uE76B";
        public const string Right = "\uE76C";
        public const string Folder = "\uEC50";
        public const string Copy = "\uE8C8";
        public const string Upload = "\uE898";
        public const string Link = "\uE71B";
        public const string Retry = "\uE72C";
        public const string Delete = "\uE74D";
    }

    // the overlay is laid out in 96 dpi pixels and scaled to whichever monitor it opens on
    public static class Ui
    {
        private const string TextFace = "Segoe UI";
        private const string StrongFace = "Segoe UI Semibold";
        private const string IconFace = "Segoe MDL2 Assets";
        private const int MonitorNearest = 2;
        private const int EffectiveDpi = 0;
        private const float BaseDpi = 96F;

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly Graphics Measuring = Graphics.FromImage(new Bitmap(1, 1));
        private static readonly StringFormat Centered = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };

        public static float Scale { get; private set; } = 1F;

        public static void UseMonitorOf(IntPtr window)
        {
            IntPtr monitor = MonitorFromWindow(window, MonitorNearest);
            Scale = GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out uint _) == 0 ? dpiX / BaseDpi : 1F;
        }

        public static int S(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static Font Text(float px, FontStyle style = FontStyle.Regular)
        {
            return Get(TextFace, px, style);
        }

        public static Font Strong(float px)
        {
            return Get(StrongFace, px, FontStyle.Regular);
        }

        public static Font Icon(float px)
        {
            return Get(IconFace, px, FontStyle.Regular);
        }

        public static GraphicsPath Round(Rectangle box, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
            var path = new GraphicsPath();
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics graphics, Color color, Rectangle box, int radius)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(box, radius))
            using (var brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRound(Graphics graphics, Color color, Rectangle box, int radius, float width = 1F)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(box, radius))
            using (var pen = new Pen(color, width))
            {
                graphics.DrawPath(pen, path);
            }
        }

        // cleartype gives icons coloured edges, so these go through plain antialiasing
        public static void DrawIcon(Graphics graphics, string icon, Rectangle box, Color color, float px)
        {
            TextRenderingHint hint = graphics.TextRenderingHint;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var brush = new SolidBrush(color))
            {
                graphics.DrawString(icon, Icon(px), brush, box, Centered);
            }
            graphics.TextRenderingHint = hint;
        }

        public static void DrawLine(Graphics graphics, string text, Font font, Rectangle box, Color color, TextFormatFlags extra = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(graphics, text, font, box, color,
                extra | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // measuring without a device context comes back about 10px too wide
        public static int Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(Measuring, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }

        // null means it worked, otherwise it's a message that's safe to show
        public static async Task<string> RunSafely(Context ctx, Action work)
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

        private static Font Get(string face, float px, FontStyle style)
        {
            float size = px * Scale;
            string key = face + "|" + size + "|" + style;
            if (!Fonts.TryGetValue(key, out Font font))
            {
                font = new Font(face, size, style, GraphicsUnit.Pixel);
                Fonts[key] = font;
            }
            return font;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}
