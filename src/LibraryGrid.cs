using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShadBuffer
{
    // the folder and clip grids, drawn by hand so a big library doesn't mean thousands of controls
    public sealed class LibraryGrid : PaintedControl
    {
        private const int Inset = 24;
        private const int FolderWidth = 186;
        private const int FolderHeight = 214;
        private const int FolderGapX = 12;
        private const int FolderGapY = 16;
        private const int ItemWidth = 232;
        private const int ItemGapX = 16;
        private const int ItemGapY = 20;
        private const int ItemTextHeight = 50;
        private const int WheelStep = 90;
        private const int BarWidth = 6;
        private const int BarMinHeight = 40;

        private readonly MediaCache cache;
        private List<LibraryFolder> folders;
        private List<LibraryFile> files;
        private string noun = "clip";
        private string empty = "";
        private int scroll;
        private int hover = -1;
        private bool draggingBar;
        private int dragOffset;

        public event Action<LibraryFolder> FolderOpened;
        public event Action<LibraryFile> FileOpened;

        public LibraryGrid(MediaCache cache)
        {
            this.cache = cache;
            SetStyle(ControlStyles.Selectable, true);
            cache.Updated += Invalidate;
        }

        public void ShowFolders(List<LibraryFolder> folders, string noun, string empty)
        {
            this.folders = folders;
            files = null;
            this.noun = noun;
            this.empty = empty;
            Reset();
        }

        public void ShowFiles(List<LibraryFile> files)
        {
            folders = null;
            this.files = files;
            Reset();
        }

        // sorting and rescanning shouldn't throw you back to the top
        public void UpdateFolders(List<LibraryFolder> folders)
        {
            this.folders = folders;
            hover = -1;
            Invalidate();
        }

        public void UpdateFiles(List<LibraryFile> files)
        {
            this.files = files;
            hover = -1;
            Invalidate();
        }

        private void Reset()
        {
            scroll = 0;
            hover = -1;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cache.Updated -= Invalidate;
            }
            base.Dispose(disposing);
        }

        private int Count => folders?.Count ?? files?.Count ?? 0;

        private void Metrics(out int columns, out int tileWidth, out int tileHeight, out int gapX, out int gapY)
        {
            int area = Math.Max(1, Width - Ui.S(Inset) * 2);
            bool showingFolders = folders != null;
            gapX = Ui.S(showingFolders ? FolderGapX : ItemGapX);
            gapY = Ui.S(showingFolders ? FolderGapY : ItemGapY);
            int least = Ui.S(showingFolders ? FolderWidth : ItemWidth);
            columns = Math.Max(1, (area + gapX) / (least + gapX));
            tileWidth = (area - gapX * (columns - 1)) / columns;
            tileHeight = showingFolders ? Ui.S(FolderHeight) : tileWidth * 9 / 16 + Ui.S(ItemTextHeight);
        }

        private int ContentHeight()
        {
            Metrics(out int columns, out int _, out int tileHeight, out int _, out int gapY);
            int rows = (Count + columns - 1) / columns;
            return Ui.S(Inset) * 2 + rows * tileHeight + Math.Max(0, rows - 1) * gapY;
        }

        private int MaxScroll => Math.Max(0, ContentHeight() - Height);

        private Rectangle Tile(int index)
        {
            Metrics(out int columns, out int tileWidth, out int tileHeight, out int gapX, out int gapY);
            int column = index % columns;
            int row = index / columns;
            return new Rectangle(
                Ui.S(Inset) + column * (tileWidth + gapX),
                Ui.S(Inset) + row * (tileHeight + gapY) - scroll,
                tileWidth,
                tileHeight);
        }

        private int TileAt(Point point)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Tile(i).Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        private Rectangle BarThumb()
        {
            int content = ContentHeight();
            if (content <= Height)
            {
                return Rectangle.Empty;
            }
            int height = Math.Max(Ui.S(BarMinHeight), Height * Height / content);
            int top = (int)((long)(Height - height) * scroll / Math.Max(1, MaxScroll));
            return new Rectangle(Width - Ui.S(BarWidth + 4), top, Ui.S(BarWidth), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            scroll = Math.Min(scroll, MaxScroll);

            if (Count == 0)
            {
                TextRenderer.DrawText(graphics, empty, Ui.Text(14), ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            for (int i = 0; i < Count; i++)
            {
                Rectangle tile = Tile(i);
                if (tile.Bottom < 0 || tile.Top > Height)
                {
                    continue;
                }
                if (folders != null)
                {
                    DrawFolder(graphics, folders[i], tile, i == hover);
                }
                else
                {
                    DrawFile(graphics, files[i], tile, i == hover);
                }
            }

            Rectangle bar = BarThumb();
            if (!bar.IsEmpty)
            {
                Ui.FillRound(graphics, draggingBar ? Theme.Muted : Theme.Border, bar, Ui.S(BarWidth) / 2);
            }
        }

        private void DrawFolder(Graphics graphics, LibraryFolder folder, Rectangle tile, bool hovered)
        {
            if (hovered)
            {
                Ui.FillRound(graphics, Theme.Panel, tile, Ui.S(10));
            }
            int shapeX = tile.X + (tile.Width - Ui.S(132)) / 2;
            int shapeY = tile.Y + Ui.S(16);
            FolderArt.Draw(graphics, shapeX, shapeY, folder.Name, hovered ? Theme.Panel : BackColor);

            int y = shapeY + Ui.S(112);
            int pad = Ui.S(8);
            int width = tile.Width - pad * 2;
            Ui.DrawLine(graphics, folder.Name, Ui.Strong(15), new Rectangle(tile.X + pad, y, width, Ui.S(22)), Theme.Text, TextFormatFlags.HorizontalCenter);
            y += Ui.S(26);
            string count = Library.Plural(folder.Files.Count, noun) + ", " + Library.SizeText(folder.Size);
            Ui.DrawLine(graphics, count, Ui.Text(12.5F), new Rectangle(tile.X + pad, y, width, Ui.S(18)), Theme.Muted, TextFormatFlags.HorizontalCenter);
            y += Ui.S(23);
            Ui.DrawLine(graphics, Library.When(folder.Modified), Ui.Text(12.5F), new Rectangle(tile.X + pad, y, width, Ui.S(18)), Theme.Muted, TextFormatFlags.HorizontalCenter);
        }

        private void DrawFile(Graphics graphics, LibraryFile file, Rectangle tile, bool hovered)
        {
            var thumb = new Rectangle(tile.X, tile.Y, tile.Width, tile.Width * 9 / 16);
            VideoThumb.Draw(graphics, thumb, cache.Get(file.Path), hovered ? Theme.Accent : Theme.Border, 1F, 9, 8, 12);
            int y = thumb.Bottom + Ui.S(8);
            Ui.DrawLine(graphics, Library.When(file.Modified), Ui.Strong(14), new Rectangle(tile.X, y, tile.Width, Ui.S(21)), Theme.Text);
            y += Ui.S(23);
            Ui.DrawLine(graphics, Library.SizeText(file.Size), Ui.Text(12.5F), new Rectangle(tile.X, y, tile.Width, Ui.S(18)), Theme.Muted);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingBar)
            {
                Rectangle bar = BarThumb();
                int room = Math.Max(1, Height - bar.Height);
                scroll = Math.Max(0, Math.Min(MaxScroll, (int)((long)(e.Y - dragOffset) * MaxScroll / room)));
                Invalidate();
                return;
            }
            int found = TileAt(e.Location);
            Change(ref hover, found);
            Cursor = found >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Rectangle bar = BarThumb();
            if (!bar.IsEmpty && e.X >= bar.X - Ui.S(4))
            {
                draggingBar = true;
                dragOffset = bar.Contains(e.Location) ? e.Y - bar.Y : bar.Height / 2;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingBar)
            {
                draggingBar = false;
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            int index = TileAt(e.Location);
            if (index < 0)
            {
                return;
            }
            if (folders != null)
            {
                FolderOpened?.Invoke(folders[index]);
            }
            else if (files != null)
            {
                FileOpened?.Invoke(files[index]);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(e.Delta) * Ui.S(WheelStep)));
            Invalidate();
        }
    }

    public static class FolderArt
    {
        // drawn in its own 132x106 box
        public static void Draw(Graphics graphics, int x, int y, string name, Color behind)
        {
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(x, y);
            graphics.ScaleTransform(Ui.Scale, Ui.Scale);
            using (var path = new GraphicsPath())
            using (var back = new SolidBrush(Theme.FolderBack))
            using (var front = new SolidBrush(Theme.Accent))
            using (var edge = new SolidBrush(Theme.FolderEdge))
            using (GraphicsPath body = Rounded(4, 33, 124, 70, 10))
            {
                path.AddArc(8, 13, 18, 18, 180, 90);
                path.AddLine(17, 13, 51, 13);
                path.AddLine(51, 13, 62, 24);
                path.AddLine(62, 24, 115, 24);
                path.AddArc(106, 24, 18, 18, 270, 90);
                path.AddLine(124, 33, 124, 45);
                path.AddLine(124, 45, 8, 45);
                path.CloseFigure();
                graphics.FillPath(back, path);
                graphics.FillPath(front, body);
                graphics.FillRectangle(edge, 14, 33, 104, 2);
            }
            graphics.Restore(state);

            var badge = new Rectangle(x + Ui.S(78), y + Ui.S(52), Ui.S(44), Ui.S(44));
            Ui.FillRound(graphics, Library.BadgeColor(name), badge, Ui.S(11));
            Ui.DrawRound(graphics, Theme.Background, badge, Ui.S(11), Ui.S(2));
            Ui.DrawLine(graphics, Library.Monogram(name), Ui.Text(15, FontStyle.Bold), badge, Color.White, TextFormatFlags.HorizontalCenter);
        }

        private static GraphicsPath Rounded(int x, int y, int width, int height, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public static class VideoThumb
    {
        public static void Draw(Graphics graphics, Rectangle box, MediaInfo info, Color border, float borderWidth, int radius, int chipInset, float chipFont)
        {
            int round = Ui.S(radius);
            Ui.FillRound(graphics, Theme.Panel, box, round);
            if (info?.Thumb != null)
            {
                using (GraphicsPath clip = Ui.Round(box, round))
                {
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(clip);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.Clear(Color.Black);
                    graphics.DrawImage(info.Thumb, Fit(info.Thumb.Size, box));
                    graphics.Restore(state);
                }
            }
            else
            {
                Ui.DrawIcon(graphics, Icons.Video, box, Theme.Border, 24);
            }
            Ui.DrawRound(graphics, border, box, round, borderWidth);

            if (info?.Length != null)
            {
                string text = Library.LengthText(info.Length.Value);
                Font font = Ui.Text(chipFont);
                int width = Ui.Measure(text, font) + Ui.S(12);
                int height = Ui.S((int)chipFont + 8);
                var chip = new Rectangle(box.Right - Ui.S(chipInset) - width, box.Bottom - Ui.S(chipInset) - height, width, height);
                Ui.FillRound(graphics, Theme.Background, chip, Ui.S(5));
                Ui.DrawLine(graphics, text, font, chip, Theme.Text, TextFormatFlags.HorizontalCenter);
            }
        }

        private static Rectangle Fit(Size image, Rectangle box)
        {
            float scale = Math.Min((float)box.Width / image.Width, (float)box.Height / image.Height);
            int width = (int)(image.Width * scale);
            int height = (int)(image.Height * scale);
            return new Rectangle(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
        }
    }
}
