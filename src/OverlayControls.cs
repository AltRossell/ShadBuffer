using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShadBuffer
{
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(24, 24, 27);
        public static readonly Color Panel = Color.FromArgb(39, 39, 42);
        public static readonly Color Border = Color.FromArgb(63, 63, 70);
        public static readonly Color Text = Color.FromArgb(244, 244, 245);
        public static readonly Color Muted = Color.FromArgb(161, 161, 170);
        public static readonly Color Accent = Color.FromArgb(109, 94, 245);
        public static readonly Color Knob = Color.FromArgb(250, 250, 250);
    }

    public sealed class Choice
    {
        public string Label { get; }
        public object Value { get; }

        public Choice(string label, object value)
        {
            Label = label;
            Value = value;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public sealed class ToggleSwitch : Control
    {
        private bool isOn;

        public event EventHandler Toggled;

        public bool Checked
        {
            get => isOn;
            set
            {
                isOn = value;
                Invalidate();
            }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled)
            {
                Checked = !Checked;
                Toggled?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            int height = Height - 1;
            int width = Width - 1;
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, height, height, 90, 180);
                path.AddArc(width - height, 0, height, height, 270, 180);
                path.CloseFigure();
                Color track = isOn ? Theme.Accent : Theme.Border;
                if (!Enabled)
                {
                    track = Color.FromArgb(120, track);
                }
                using (var brush = new SolidBrush(track))
                {
                    graphics.FillPath(brush, path);
                }
            }

            int gap = Math.Max(3, height / 8);
            int knob = height - gap * 2;
            int x = isOn ? width - knob - gap : gap;
            using (var brush = new SolidBrush(Theme.Knob))
            {
                graphics.FillEllipse(brush, x, gap, knob, knob);
            }
        }
    }

    public sealed class FlatCombo : ComboBox
    {
        private const int WmPaint = 0x000F;

        public FlatCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            ItemHeight = 22;
        }

        // windows draws a white border and arrow on flat combos, so i paint over them
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmPaint)
            {
                return;
            }
            using (Graphics graphics = Graphics.FromHwnd(Handle))
            using (var border = new Pen(Theme.Border))
            using (var fill = new SolidBrush(Theme.Panel))
            using (var arrow = new Pen(Enabled ? Theme.Text : Theme.Muted, 1.5F))
            {
                int width = SystemInformation.VerticalScrollBarWidth + 2;
                Rectangle box = ClientRectangle;
                graphics.FillRectangle(fill, box.Right - width - 1, box.Top + 1, width, box.Height - 2);
                graphics.DrawRectangle(border, box.Left, box.Top, box.Width - 1, box.Height - 1);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int centerX = box.Right - width / 2 - 1;
                int centerY = box.Height / 2;
                graphics.DrawLines(arrow, new[]
                {
                    new Point(centerX - 4, centerY - 2),
                    new Point(centerX, centerY + 2),
                    new Point(centerX + 4, centerY - 2),
                });
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Panel))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // click it and press the combo you want
    public sealed class HotkeyBox : TextBox
    {
        private const string Prompt = "Press keys...";

        private string saved;

        public event Action<Hotkey> Captured;
        public event Action<bool> CaptureChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            saved = Text;
            Text = Prompt;
            CaptureChanged?.Invoke(true);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            if (Text == Prompt)
            {
                Text = saved;
            }
            CaptureChanged?.Invoke(false);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Escape)
            {
                Parent?.Focus();
                return true;
            }
            if (code == Keys.Tab)
            {
                return base.ProcessCmdKey(ref message, keyData);
            }

            Hotkey hotkey = Hotkey.FromKeyData(keyData);
            if (hotkey != null)
            {
                Text = hotkey.ToString();
                saved = Text;
                Captured?.Invoke(hotkey);
                Parent?.Focus();
            }
            return true;
        }
    }
}
