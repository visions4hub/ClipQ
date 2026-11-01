using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace ClipQ
{
    class Popup : Form
    {
        readonly App app;
        readonly TextBox search = new TextBox();
        readonly ListBox list = new ListBox();
        readonly Label empty = new Label();
        readonly Label hint;
        readonly Panel box;
        readonly ToolStripMenuItem pinItem;
        List<Item> shown = new List<Item>();

        public Popup(App app)
        {
            this.app = app;
            Text = "ClipQ";
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            RightToLeft = L.Dir;
            Font = Ui.Font(10f);
            ClientSize = new Size(Theme.S(420), Theme.S(460));
            Padding = new Padding(Theme.S(10));

            box = new Panel
            {
                Dock = DockStyle.Top,
                Height = Theme.S(42),
                Padding = new Padding(Theme.S(14), Theme.S(11), Theme.S(14), 0)
            };
            box.Paint += (s, e) => Ui.FillRound(e.Graphics, new Rectangle(0, 0, box.Width - 1, box.Height - 1), Theme.S(10),
                Theme.Surface2, search.Focused ? Theme.Accent : Theme.Border);
            search.GotFocus += delegate { box.Invalidate(); };
            search.LostFocus += delegate { box.Invalidate(); };
            search.BorderStyle = BorderStyle.None;
            search.Font = Ui.Font(11f);
            search.Dock = DockStyle.Fill;
            search.TextChanged += delegate { Reload(); };
            search.HandleCreated += delegate { Native.SendMessage(search.Handle, 0x1501, (IntPtr)1, L.T("ابحث…", "Search…")); };
            box.Controls.Add(search);

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Theme.S(36);
            list.IntegralHeight = false;
            list.DrawItem += DrawItem;
            list.DoubleClick += delegate { Choose(); };
            list.MouseDown += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= 0) list.SelectedIndex = i;
            };

            empty.Dock = DockStyle.Fill;
            empty.TextAlign = ContentAlignment.MiddleCenter;
            empty.Visible = false;

            hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Theme.S(26),
                Font = Ui.Font(8.5f),
                TextAlign = ContentAlignment.BottomCenter,
                Text = L.T("Enter لصق   •   Ctrl+P تثبيت   •   Del حذف   •   Esc إغلاق",
                           "Enter paste   •   Ctrl+P pin   •   Del delete   •   Esc close")
            };

            var menu = new ContextMenuStrip { RightToLeft = L.Dir };
            menu.Items.Add(L.T("لصق", "Paste"), null, delegate { Choose(); });
            pinItem = new ToolStripMenuItem(L.T("تثبيت", "Pin"), null, delegate { TogglePin(); });
            menu.Items.Add(pinItem);
            menu.Items.Add(L.T("حذف", "Delete"), null, delegate { DeleteSelected(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("مسح السجل", "Clear history"), null, delegate { app.ClearHistory(); });
            menu.Opening += (s, e) =>
            {
                var it = Selected;
                if (it == null) { e.Cancel = true; return; }
                pinItem.Text = it.Pinned ? L.T("إلغاء التثبيت", "Unpin") : L.T("تثبيت", "Pin");
            };
            list.ContextMenuStrip = menu;

            // Docking runs from the last-added control, so add the fill controls first.
            Controls.Add(list);
            Controls.Add(empty);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(8) });
            Controls.Add(box);
            Controls.Add(hint);

            Deactivate += delegate { Hide(); };
            KeyDown += OnKey;
            ApplyTheme();
        }

        // Re-reads light/dark each time it opens, so it follows a Windows theme change.
        void ApplyTheme()
        {
            Theme.Refresh();
            BackColor = list.BackColor = empty.BackColor = hint.BackColor = box.BackColor = Theme.Surface;
            search.BackColor = Theme.Surface2;
            search.ForeColor = list.ForeColor = Theme.Text;
            empty.ForeColor = hint.ForeColor = Theme.Muted;
            if (IsHandleCreated) StyleWindow();
            Invalidate(true);
        }

        void StyleWindow()
        {
            int round = 2; // Windows 11 rounded corners
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int border = Theme.ColorRef(Theme.Border);
            Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            Native.SetWindowTheme(list.Handle, Theme.Dark ? "DarkMode_Explorer" : null, null);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            StyleWindow();
        }

        public void ShowAtCursor()
        {
            ApplyTheme();
            search.Text = "";
            Reload();
            var p = Cursor.Position;
            var wa = Screen.FromPoint(p).WorkingArea;
            Location = new Point(
                Math.Max(wa.Left, Math.Min(p.X - Width / 2, wa.Right - Width)),
                Math.Max(wa.Top, Math.Min(p.Y + Theme.S(12), wa.Bottom - Height)));
            Show();
            Native.ForceForeground(Handle);
            Activate();
            search.Focus();
        }

        Item Selected
        {
            get
            {
                int i = list.SelectedIndex;
                return i >= 0 && i < shown.Count ? shown[i] : null;
            }
        }

        public void Reload()
        {
            var keep = Selected;
            var q = search.Text.Trim();
            shown = new List<Item>();
            foreach (var pinnedPass in new[] { true, false })
                foreach (var it in app.History)
                    if (it.Pinned == pinnedPass && (q.Length == 0 || it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                        shown.Add(it);

            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in shown) list.Items.Add(it);
            list.EndUpdate();

            if (shown.Count > 0) list.SelectedIndex = Math.Max(0, keep == null ? 0 : shown.IndexOf(keep));
            empty.Text = q.Length > 0 ? L.T("ما أكو نتائج", "No results")
                                      : L.T("السجل فارغ، انسخ أي نص وراح يطلع هنا", "Nothing here yet. Copy some text and it will show up here.");
            empty.Visible = shown.Count == 0;
            list.Visible = shown.Count > 0;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Hide(); break;
                case Keys.Enter: Choose(); break;
                case Keys.Down: MoveSelection(1); break;
                case Keys.Up: MoveSelection(-1); break;
                case Keys.Delete: DeleteSelected(); break;
                case Keys.P: if (e.Control) TogglePin(); else return; break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        void MoveSelection(int delta)
        {
            if (shown.Count == 0) return;
            list.SelectedIndex = Math.Max(0, Math.Min(shown.Count - 1, list.SelectedIndex + delta));
        }

        void Choose()
        {
            var it = Selected;
            if (it != null) app.Choose(it);
        }

        void TogglePin()
        {
            var it = Selected;
            if (it == null) return;
            it.Pinned = !it.Pinned;
            app.SaveHistory();
        }

        void DeleteSelected()
        {
            var it = Selected;
            if (it == null) return;
            app.History.Remove(it);
            app.SaveHistory();
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var it = shown[e.Index];
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);

            var r = Rectangle.Inflate(e.Bounds, -Theme.S(2), -Theme.S(2));
            if ((e.State & DrawItemState.Selected) != 0)
                Ui.FillRound(g, r, Theme.S(8), Theme.AccentSoft, Color.Empty);

            // The star sits at the line's start: right in Arabic, left in English.
            var star = L.Rtl ? new Rectangle(r.Right - Theme.S(30), r.Y, Theme.S(24), r.Height)
                             : new Rectangle(r.X + Theme.S(6), r.Y, Theme.S(24), r.Height);
            var textRect = L.Rtl ? new Rectangle(r.X + Theme.S(10), r.Y, r.Width - Theme.S(44), r.Height)
                                 : new Rectangle(r.X + Theme.S(34), r.Y, r.Width - Theme.S(44), r.Height);
            var preview = Preview(it.Text);
            var flags = TextFormatFlags.VerticalCenter | (L.Rtl ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.EndEllipsis
                      | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (IsRtl(preview)) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, preview, list.Font, textRect, Theme.Text, flags);
            if (it.Pinned)
                TextRenderer.DrawText(g, "★", list.Font, star, Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        static string Preview(string text)
        {
            var sb = new StringBuilder(Math.Min(text.Length, 200));
            bool space = false;
            foreach (var c in text)
            {
                if (sb.Length >= 200) break;
                if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; }
                else { sb.Append(c); space = false; }
            }
            return sb.ToString();
        }

        // Direction follows the first letter, so English text and links read left-to-right.
        static bool IsRtl(string s)
        {
            foreach (var c in s)
            {
                if ((c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= 'ﻼ')) return true;
                if (char.IsLetter(c)) return false;
            }
            return true;
        }
    }
}
