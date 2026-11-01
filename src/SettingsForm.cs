using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipQ
{
    // Read-only box that records the next key combination pressed in it.
    class HotkeyBox : TextBox
    {
        int value;
        public event EventHandler ValueChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            TextAlign = HorizontalAlignment.Center;
            ShortcutsEnabled = false;
            RightToLeft = RightToLeft.No;
            Cursor = Cursors.Hand;
            Value = 0;
        }

        public int Value
        {
            get { return value; }
            set
            {
                this.value = value;
                Text = value == 0 ? L.T("اضغط الاختصار هنا", "Press a shortcut here") : Theme.KeyText(value);
                ForeColor = value == 0 ? Theme.Muted : Theme.Text;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            var code = e.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin) return;
            if (e.Modifiers == Keys.None && (code == Keys.Back || code == Keys.Delete || code == Keys.Escape)) { Value = 0; return; }
            bool fKey = code >= Keys.F1 && code <= Keys.F24;
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0 && !fKey) return; // plain letters would block normal typing
            Value = (int)(code | e.Modifiers);
        }
    }

    // Main window: the shortcut list on the right; the selected shortcut's editor and the general options on the left.
    class SettingsForm : Form
    {
        const int W = 880, H = 612;          // client size before DPI scaling
        const int ListW = 380, SideW = 436;  // card widths

        public Settings Result { get; private set; }
        public bool AutoStartChecked { get { return autoStart.Checked; } }

        readonly List<Snippet> snippets;
        readonly SnippetList list = new SnippetList();
        readonly Label count;
        readonly Panel empty = new Panel();
        readonly List<Control> editorParts;
        readonly Label pickHint;
        readonly TextBox name = new TextBox();
        readonly HotkeyBox snipKeys = new HotkeyBox();
        readonly TextBox text = new TextBox();
        readonly HotkeyBox popupKeys = new HotkeyBox();
        readonly Toggle autoStart = new Toggle();
        readonly ToolTip tips = new ToolTip();
        bool loading;

        public SettingsForm(Settings current, bool autoStartOn)
        {
            snippets = current.Snippets.Select(s => new Snippet { Name = s.Name, Text = s.Text, Keys = s.Keys }).ToList();

            Text = L.T("ClipQ — الاختصارات", "ClipQ — Shortcuts");
            Icon = App.AppIcon;
            Font = Ui.Font(10f);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            RightToLeft = L.Dir;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(W), S(H));
            KeyPreview = true;

            // header
            At(this, W, new Logo { BackColor = Theme.Bg }, 24, 22, 46, 46);
            At(this, W, Ui.Label(L.T("الاختصارات", "Shortcuts"), 16f, FontStyle.Bold, Theme.Text, Theme.Bg), 84, 16, 400, 34);
            At(this, W, Ui.Label(L.T("تضغط الاختصار بأي مكان، وينلصق النص مباشرة", "Press a shortcut anywhere and the text is pasted instantly"), 10f, FontStyle.Regular, Theme.Muted, Theme.Bg), 84, 48, 400, 24);

            // start side (right in Arabic): the list
            var listCard = new Card();
            At(this, W, listCard, 24, 92, ListW, 444);
            At(listCard, ListW, Ui.Label(L.T("ردودك", "Snippets"), 11.5f, FontStyle.Bold, Theme.Text, Theme.Surface), 20, 14, L.Rtl ? 70 : 80, 28);
            count = Ui.Label("", 10f, FontStyle.Regular, Theme.Muted, Theme.Surface);
            At(listCard, ListW, count, L.Rtl ? 90 : 100, 14, 60, 28);
            At(listCard, ListW, Ui.Label(L.T("اسحب أي سطر حتى تغيّر ترتيبه", "Drag a row to reorder"), 9f, FontStyle.Regular, Theme.Muted, Theme.Surface), 20, 40, 200, 20);
            var add = new FlatButton(L.T("+ إضافة اختصار", "+ Add shortcut"), ButtonKind.Primary) { BackColor = Theme.Surface };
            add.Click += delegate { AddSnippet(); };
            AtLeft(listCard, add, 16, 16, 136, 36);

            list.SelectedIndexChanged += delegate { LoadSelected(); };
            list.Moved += MoveSnippet;
            At(listCard, ListW, list, 10, 68, ListW - 20, 364);

            // empty state, over the list
            empty.BackColor = Theme.Surface;
            At(listCard, ListW, empty, 10, 68, ListW - 20, 364);
            At(empty, ListW - 20, new Logo { BackColor = Theme.Surface }, (ListW - 20 - 52) / 2, 92, 52, 52);
            var e1 = Ui.Label(L.T("ما عندك اختصارات بعد", "No shortcuts yet"), 12f, FontStyle.Bold, Theme.Text, Theme.Surface);
            e1.TextAlign = ContentAlignment.MiddleCenter;
            At(empty, ListW - 20, e1, 0, 156, ListW - 20, 30);
            var e2 = Ui.Label(L.T("أضف رد تستخدمه هواية وخلّيله اختصار مثل Ctrl+1", "Add text you type often and give it a shortcut like Ctrl+1"), 9.5f, FontStyle.Regular, Theme.Muted, Theme.Surface);
            e2.TextAlign = ContentAlignment.MiddleCenter;
            At(empty, ListW - 20, e2, 0, 186, ListW - 20, 24);
            var addFirst = new FlatButton(L.T("+ أضف أول اختصار", "+ Add your first shortcut"), ButtonKind.Primary) { BackColor = Theme.Surface };
            addFirst.Click += delegate { AddSnippet(); };
            At(empty, ListW - 20, addFirst, (ListW - 20 - 170) / 2, 226, 170, 40);

            // end side top (left in Arabic): the selected shortcut
            var editCard = new Card();
            AtLeft(this, editCard, 24, 92, SideW, 300);
            var editTitle = Ui.Label(L.T("تعديل الاختصار", "Edit shortcut"), 11.5f, FontStyle.Bold, Theme.Text, Theme.Surface);
            At(editCard, SideW, editTitle, 20, 14, 200, 28);
            pickHint = Ui.Label(L.T("اختار اختصار من القائمة، أو اضغط «+ إضافة اختصار»", "Pick a shortcut from the list, or click “+ Add shortcut”"), 10f, FontStyle.Regular, Theme.Muted, Theme.Surface);
            pickHint.TextAlign = ContentAlignment.MiddleCenter;
            At(editCard, SideW, pickHint, 20, 120, SideW - 40, 40);

            var editor = editCard; // everything added below hides while nothing is selected
            var remove = new FlatButton(L.T("حذف", "Delete"), ButtonKind.Danger) { BackColor = Theme.Surface };
            remove.Click += delegate { RemoveSelected(); };
            AtLeft(editor, remove, 16, 12, 72, 32);

            At(editor, SideW, FieldLabel(L.T("الاسم", "Name")), 20, 52, 210, 20);
            name.RightToLeft = L.Dir;
            name.TextChanged += delegate { EditSelected(); };
            name.HandleCreated += delegate { Native.SendMessage(name.Handle, 0x1501, (IntPtr)1, L.T("مثلاً: خطوات مرماز", "e.g. Welcome reply")); };
            At(editor, SideW, new Field(name, false), 20, 74, 210, 40);

            At(editor, SideW, FieldLabel(L.T("الاختصار", "Shortcut")), 246, 52, 170, 20);
            snipKeys.ValueChanged += delegate { EditSelected(); };
            var keysField = new Field(snipKeys, false);
            At(editor, SideW, keysField, 246, 74, 170, 40);
            snipKeys.BackColor = Theme.Surface2;
            snipKeys.Value = 0;

            At(editor, SideW, FieldLabel(L.T("النص", "Text")), 20, 128, 80, 20);
            var vars = new[] {
                new[] { L.T("+ اليوم", "+ Day"), "{day}", L.T("اسم اليوم، مثل الأحد", "The weekday, e.g. Sunday") },
                new[] { L.T("+ الوقت", "+ Time"), "{time}", L.T("الوقت وقت اللصق، مثل 5:21 م", "The time when pasted, e.g. 5:21 PM") },
                new[] { L.T("+ التاريخ", "+ Date"), "{date}", L.T("تاريخ اليوم، مثل 1/11/2026", "Today's date, e.g. 1/11/2026") } };
            for (int i = 0; i < vars.Length; i++)
            {
                var v = vars[i];
                var chip = new FlatButton(v[0], ButtonKind.Chip) { BackColor = Theme.Surface, TabStop = false };
                chip.Click += delegate { InsertVar(v[1]); };
                tips.SetToolTip(chip, v[2] + L.T(" — يكتب ", " — inserts ") + v[1]);
                AtLeft(editor, chip, 20 + i * 84, 124, 78, 26);
            }

            text.Multiline = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.AcceptsReturn = true;
            text.RightToLeft = L.Dir;
            text.TextChanged += delegate { EditSelected(); };
            Theme.StyleScrollbars(text);
            At(editor, SideW, new Field(text, true), 20, 156, SideW - 40, 124);
            editorParts = editCard.Controls.Cast<Control>().Where(c => c != pickHint && c != editTitle).ToList();

            // end side bottom: general options
            var optCard = new Card();
            AtLeft(this, optCard, 24, 408, SideW, 128);
            At(optCard, SideW, Ui.Label(L.T("الإعدادات", "Settings"), 11.5f, FontStyle.Bold, Theme.Text, Theme.Surface), 20, 14, 200, 28);
            At(optCard, SideW, Ui.Label(L.T("اختصار سجل النسخ", "History shortcut"), 10f, FontStyle.Regular, Theme.Text, Theme.Surface), 20, 52, 200, 32);
            var popupField = new Field(popupKeys, false);
            AtLeft(optCard, popupField, 20, 48, 170, 40);
            popupKeys.BackColor = Theme.Surface2;
            popupKeys.Value = current.PopupKeys;
            At(optCard, SideW, Ui.Label(L.T("التشغيل التلقائي مع Windows", "Start with Windows"), 10f, FontStyle.Regular, Theme.Text, Theme.Surface), 20, 92, 260, 28);
            autoStart.BackColor = Theme.Surface;
            autoStart.Checked = autoStartOn;
            AtLeft(optCard, autoStart, 20, 96, 42, 22);

            // footer
            var cancel = new FlatButton(L.T("إلغاء", "Cancel"), ButtonKind.Ghost) { BackColor = Theme.Bg };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            AtLeft(this, cancel, 24, 552, 100, 40);
            var save = new FlatButton(L.T("حفظ", "Save"), ButtonKind.Primary) { BackColor = Theme.Bg };
            save.Click += delegate { Save(); };
            AtLeft(this, save, 132, 552, 120, 40);
            At(this, W, Ui.Label(L.T("Ctrl+S للحفظ   •   Esc للإغلاق", "Ctrl+S to save   •   Esc to close"), 9f, FontStyle.Regular, Theme.Muted, Theme.Bg), 24, 552, 300, 40);

            RefreshList(snippets.Count > 0 ? 0 : -1);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleFrame(Handle, Theme.Bg);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (snippets.Count > 0) list.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Esc arrives here only when the focused box doesn't take it (the shortcut boxes use Esc to clear).
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessDialogKey(keyData);
        }

        static int S(int v) { return Theme.S(v); }

        // Places c by its distance from the line's start edge: the right edge in Arabic, the left edge in English.
        static void At(Control parent, int parentW, Control c, int start, int top, int w, int h)
        {
            c.Bounds = new Rectangle(L.Rtl ? S(parentW - start - w) : S(start), S(top), S(w), S(h));
            parent.Controls.Add(c);
            c.BringToFront();
        }

        // Places c by its distance from the line's end edge: the left edge in Arabic, the right edge in English.
        static void AtLeft(Control parent, Control c, int end, int top, int w, int h)
        {
            c.Bounds = new Rectangle(L.Rtl ? S(end) : parent.ClientSize.Width - S(end) - S(w), S(top), S(w), S(h));
            parent.Controls.Add(c);
            c.BringToFront();
        }

        static Label FieldLabel(string text)
        {
            return Ui.Label(text, 9.5f, FontStyle.Bold, Theme.Muted, Theme.Surface);
        }

        int Selected { get { return list.SelectedIndex; } }

        void RefreshList(int select)
        {
            loading = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var s in snippets) list.Items.Add(s);
            list.EndUpdate();
            list.SelectedIndex = select;
            loading = false;
            count.Text = snippets.Count > 0 ? "(" + snippets.Count + ")" : "";
            empty.Visible = snippets.Count == 0;
            list.Visible = snippets.Count > 0;
            LoadSelected();
        }

        void LoadSelected()
        {
            if (loading) return;
            loading = true;
            int i = Selected;
            bool has = i >= 0;
            foreach (var c in editorParts) c.Visible = has;
            pickHint.Visible = !has;
            name.Text = has ? snippets[i].Name ?? "" : "";
            text.Text = has ? snippets[i].Text ?? "" : "";
            snipKeys.Value = has ? snippets[i].Keys : 0;
            loading = false;
        }

        void EditSelected()
        {
            if (loading) return;
            int i = Selected;
            if (i < 0) return;
            snippets[i].Name = name.Text;
            snippets[i].Text = text.Text;
            snippets[i].Keys = snipKeys.Value;
            list.Invalidate(list.GetItemRectangle(i));
        }

        void AddSnippet()
        {
            snippets.Add(new Snippet { Text = "" });
            RefreshList(snippets.Count - 1);
            name.Focus();
        }

        void RemoveSelected()
        {
            int i = Selected;
            if (i < 0) return;
            snippets.RemoveAt(i);
            RefreshList(Math.Min(i, snippets.Count - 1));
        }

        void MoveSnippet(int from, int insertBefore)
        {
            int to = insertBefore > from ? insertBefore - 1 : insertBefore;
            if (from < 0 || from >= snippets.Count || to == from) return;
            var s = snippets[from];
            snippets.RemoveAt(from);
            snippets.Insert(to, s);
            RefreshList(to);
        }

        void InsertVar(string token)
        {
            text.Focus();
            text.SelectedText = token;
        }

        void Save()
        {
            var kept = snippets.Where(s => !string.IsNullOrEmpty(s.Text)).ToList();
            if (kept.Any(s => s.Keys == 0))
            {
                Warn(L.T("في نص بدون اختصار. اضغط الأزرار بمربع «الاختصار».", "A snippet has no shortcut. Click its Shortcut box and press the keys."));
                return;
            }
            var used = new HashSet<int>();
            foreach (var k in new[] { popupKeys.Value }.Concat(kept.Select(s => s.Keys)))
            {
                if (k != 0 && !used.Add(k))
                {
                    Warn(L.T("الاختصار " + Theme.KeyText(k) + " مستخدم أكثر من مرة.", "The shortcut " + Theme.KeyText(k) + " is used more than once."));
                    return;
                }
            }
            Result = new Settings { PopupKeys = popupKeys.Value, Snippets = kept };
            DialogResult = DialogResult.OK;
        }

        static void Warn(string message)
        {
            MessageBox.Show(message, "ClipQ", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, L.MsgOptions);
        }
    }
}
