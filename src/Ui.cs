using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClipQ
{
    // Small owner-drawn building blocks in DropQR's style. Text is drawn by hand in the build's direction (see L).
    static class Ui
    {
        public static readonly TextFormatFlags Line = L.StartAligned | TextFormatFlags.VerticalCenter
                                                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(style == FontStyle.Bold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular);
        }

        // The brand mark: a rounded square in the indigo→cyan gradient with a white Q.
        public static void DrawLogo(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = Theme.RoundRect(r, Math.Max(2, r.Width * 10 / 44)))
            using (var brush = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width + 1, r.Height + 1), Theme.BrandFrom, Theme.BrandTo, 45f))
            using (var font = new Font("Segoe UI", r.Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillPath(brush, path);
                g.DrawString("Q", font, Brushes.White, new RectangleF(r.X, r.Y + r.Height * 0.02f, r.Width, r.Height), sf);
            }
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(r, radius))
            {
                if (fill != Color.Empty) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            }
        }

        public static Label Label(string text, float size, FontStyle style, Color color, Color back)
        {
            return new Label
            {
                Text = text,
                Font = Font(size, style),
                ForeColor = color,
                BackColor = back,
                AutoSize = false,
                RightToLeft = L.Dir,
                TextAlign = ContentAlignment.MiddleLeft, // right-to-left flips this, so Arabic text sits on the right

                UseMnemonic = false
            };
        }
    }

    class Painted : Control
    {
        public Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            RightToLeft = RightToLeft.No;
        }
    }

    class Logo : Painted
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.DrawLogo(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
        }
    }

    // A white (or deep navy) card with a thin border and generous corners.
    class Card : Panel
    {
        public Card()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(16), Theme.Surface, Theme.Border);
        }
    }

    enum ButtonKind { Primary, Ghost, Danger, Chip }

    class FlatButton : Painted
    {
        public ButtonKind Kind;
        bool hover, down;

        public FlatButton(string text, ButtonKind kind)
        {
            Text = text;
            Kind = kind;
            Font = Ui.Font(kind == ButtonKind.Chip ? 9f : 10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, down ? 1 : 0, Width - 1, Height - 2);
            bool on = Enabled && (hover || Focused);
            Color fill, border, text;
            switch (Kind)
            {
                case ButtonKind.Primary: fill = on ? Theme.AccentHover : Theme.Accent; border = Color.Empty; text = Theme.OnAccent; break;
                case ButtonKind.Danger: fill = Color.Empty; border = on ? Theme.Danger : Theme.Border; text = Theme.Danger; break;
                case ButtonKind.Chip: fill = Theme.AccentSoft; border = on ? Theme.Accent : Color.Empty; text = Theme.Accent; break;
                default: fill = Theme.Surface2; border = on ? Theme.Accent : Theme.Border; text = Theme.Text; break;
            }
            if (!Enabled) text = Theme.Muted;
            int radius = Kind == ButtonKind.Chip ? r.Height / 2 : Theme.S(10);
            Ui.FillRound(g, r, radius, fill, border);
            TextRenderer.DrawText(g, Text, Font, r, text, L.Rtl ? Ui.Center | TextFormatFlags.RightToLeft : Ui.Center);
        }
    }

    // A rounded inset field that hosts a borderless TextBox; its border turns indigo while typing.
    class Field : Panel
    {
        public readonly TextBox Box;

        public Field(TextBox box, bool multiline)
        {
            Box = box;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(12), Theme.S(multiline ? 10 : 9), Theme.S(12), Theme.S(multiline ? 6 : 0));
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Surface2;
            box.ForeColor = Theme.Text;
            box.Font = Ui.Font(10.5f);
            box.Dock = DockStyle.Fill;
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Controls.Add(box);
            Cursor = Cursors.IBeam;
            Click += delegate { box.Focus(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10), Theme.Surface2, Box.Focused ? Theme.Accent : Theme.Border);
        }
    }

    // On/off switch. "On" moves the knob toward the line's end: left in Arabic, right in English.
    class Toggle : Painted
    {
        bool value;
        public event EventHandler CheckedChanged;

        public Toggle() { Cursor = Cursors.Hand; SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        public bool Checked
        {
            get { return value; }
            set { this.value = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Checked = !Checked; base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            var off = Theme.Dark ? Color.FromArgb(0x2f, 0x36, 0x54) : Color.FromArgb(0xcf, 0xd4, 0xe2);
            Ui.FillRound(g, r, r.Height / 2, value ? Theme.Accent : off, Focused ? Theme.AccentHover : Color.Empty);
            int pad = Theme.S(3), d = r.Height - pad * 2;
            int x = value == L.Rtl ? r.X + pad : r.Right - pad - d;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, r.Y + pad, d, d);
        }
    }

    // The shortcut list: one card-like row per snippet (name, text preview, key chip), drag to reorder.
    class SnippetList : ListBox
    {
        public event Action<int, int> Moved; // from, insert-before index
        int hoverIndex = -1, dropIndex = -1, downIndex = -1;
        Point downAt;
        readonly Font nameFont = Ui.Font(10.5f, FontStyle.Bold), textFont = Ui.Font(9.5f), chipFont = Ui.Font(9f, FontStyle.Bold);

        public SnippetList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Theme.S(60);
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            BackColor = Theme.Surface;
            RightToLeft = RightToLeft.No;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Theme.StyleScrollbars(this);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var s = (Snippet)Items[e.Index];
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);

            var r = new Rectangle(e.Bounds.X + Theme.S(4), e.Bounds.Y + Theme.S(3), e.Bounds.Width - Theme.S(8), e.Bounds.Height - Theme.S(6));
            bool selected = (e.State & DrawItemState.Selected) != 0;
            if (selected) Ui.FillRound(g, r, Theme.S(12), Theme.AccentSoft, Theme.Dark ? Color.Empty : Color.FromArgb(0xd9, 0xdc, 0xfb));
            else if (e.Index == hoverIndex) Ui.FillRound(g, r, Theme.S(12), Theme.Surface2, Color.Empty);

            // key chip at the row's end: left in Arabic, right in English
            var key = Theme.KeyText(s.Keys);
            var size = TextRenderer.MeasureText(g, key, chipFont, Size.Empty, TextFormatFlags.NoPadding);
            int chipH = Theme.S(26), chipW = size.Width + Theme.S(18);
            int chipX = L.Rtl ? r.X + Theme.S(12) : r.Right - Theme.S(12) - chipW;
            var chip = new Rectangle(chipX, r.Y + (r.Height - chipH) / 2, chipW, chipH);
            Ui.FillRound(g, chip, chipH / 2, selected ? Theme.Surface : Theme.Surface2, Theme.Border);
            TextRenderer.DrawText(g, key, chipFont, chip, s.Keys == 0 ? Theme.Muted : Theme.Text, Ui.Center);

            var text = r.Height > 0 ? Preview(s.Text) : "";
            int areaW = r.Width - Theme.S(14) - chipW - Theme.S(24);
            var area = L.Rtl ? new Rectangle(chip.Right + Theme.S(12), r.Y, areaW, r.Height)
                             : new Rectangle(r.X + Theme.S(14), r.Y, areaW, r.Height);
            var name = (s.Name ?? "").Trim();
            if (name.Length > 0)
            {
                int half = area.Height / 2;
                TextRenderer.DrawText(g, name, nameFont, new Rectangle(area.X, area.Y + Theme.S(4), area.Width, half - Theme.S(2)), Theme.Text, Ui.Line);
                TextRenderer.DrawText(g, text.Length > 0 ? text : L.T("(اكتب النص)", "(type the text)"), textFont,
                    new Rectangle(area.X, area.Y + half, area.Width, half - Theme.S(6)), Theme.Muted, Ui.Line);
            }
            else
                TextRenderer.DrawText(g, text.Length > 0 ? text : L.T("اختصار جديد — اكتب النص", "New shortcut — type the text"),
                    text.Length > 0 ? nameFont : textFont, area, text.Length > 0 ? Theme.Text : Theme.Muted, Ui.Line);

            // drop marker while dragging
            int line = -1;
            if (dropIndex == e.Index) line = e.Bounds.Top;
            else if (dropIndex == Items.Count && e.Index == Items.Count - 1) line = e.Bounds.Bottom - Theme.S(2);
            if (line >= 0) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X + Theme.S(8), line, e.Bounds.Width - Theme.S(16), Theme.S(2));
        }

        static string Preview(string text)
        {
            var s = (text ?? "").Replace("\r", "").Replace('\n', ' ').Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
            if (e.Button == MouseButtons.Left && downIndex >= 0 &&
                (Math.Abs(e.X - downAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - downAt.Y) > SystemInformation.DragSize.Height))
            {
                int from = downIndex;
                downIndex = -1;
                DoDragDrop(new DataObject("ClipQ.Row", from), DragDropEffects.Move);
                dropIndex = -1;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            downIndex = e.Button == MouseButtons.Left ? IndexFromPoint(e.Location) : -1;
            downAt = e.Location;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { downIndex = -1; base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        int InsertIndex(Point screen)
        {
            var p = PointToClient(screen);
            int i = IndexFromPoint(p);
            if (i < 0) return Items.Count;
            var b = GetItemRectangle(i);
            return p.Y > b.Top + b.Height / 2 ? i + 1 : i;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ClipQ.Row")) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            int i = InsertIndex(new Point(e.X, e.Y));
            if (i != dropIndex) { dropIndex = i; Invalidate(); }
            base.OnDragOver(e);
        }

        protected override void OnDragLeave(EventArgs e) { dropIndex = -1; Invalidate(); base.OnDragLeave(e); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            int to = InsertIndex(new Point(e.X, e.Y));
            dropIndex = -1;
            Invalidate();
            if (e.Data.GetDataPresent("ClipQ.Row") && Moved != null) Moved((int)e.Data.GetData("ClipQ.Row"), to);
            base.OnDragDrop(e);
        }
    }
}
