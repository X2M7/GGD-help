using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GgdHelp
{
    // Keep the native selection/editing HWNDs and their accessibility providers.
    // Only their presentation is replaced; mouse capture, repeat and keyboard
    // navigation remain the standard ComboBox / NumericUpDown implementations.
    internal static class SelectorDrawing
    {
        internal static readonly Color Surface = Color.FromArgb(20, 38, 48);
        internal static readonly Color Hover = Color.FromArgb(31, 56, 65);
        internal static readonly Color Pressed = Color.FromArgb(42, 77, 80);
        internal static readonly Color Disabled = Color.FromArgb(122, 143, 151);

        internal static void Arrow(Graphics graphics, Rectangle area, bool up, Color color, float scale)
        {
            int halfWidth = Math.Max(2, (int)Math.Round(3 * scale));
            int halfHeight = Math.Max(1, (int)Math.Round(2 * scale));
            int x = area.Left + area.Width / 2, y = area.Top + area.Height / 2;
            Point[] points = up
                ? new[] { new Point(x - halfWidth, y + halfHeight), new Point(x + halfWidth, y + halfHeight), new Point(x, y - halfHeight) }
                : new[] { new Point(x - halfWidth, y - halfHeight), new Point(x + halfWidth, y - halfHeight), new Point(x, y + halfHeight) };
            using (Brush brush = new SolidBrush(color)) graphics.FillPolygon(brush, points);
        }

        internal static void Border(Graphics graphics, Rectangle rectangle, Color color)
        {
            if (rectangle.Width < 2 || rectangle.Height < 2) return;
            using (Pen pen = new Pen(color)) graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
        }
    }

    internal sealed class DarkComboBox : ComboBox
    {
        private float scale = 1f;
        private int logicalHeight = 28;
        private bool hover;

        public DarkComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = SelectorDrawing.Surface;
            ForeColor = CardPalette.Ink;
            IntegralHeight = false;
            MaxDropDownItems = 12;
            Cursor = Cursors.Hand;
            ApplyScale(1, 28);
        }

        // A native ComboBox derives its collapsed height from ItemHeight. Set
        // that metric instead of asking SetBounds for a height Windows ignores.
        public void ApplyScale(double value, int height)
        {
            scale = (float)Math.Max(.35, Math.Min(8, value));
            logicalHeight = Math.Max(20, height);
            UpdateMetrics();
        }

        private void UpdateMetrics()
        {
            int desired = (int)Math.Round(logicalHeight * scale);
            ItemHeight = Math.Max(Font.Height + Math.Max(2, (int)Math.Round(3 * scale)), desired - 6);
            Height = PreferredHeight;
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (logicalHeight > 0) UpdateMetrics();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool collapsed = (e.State & DrawItemState.ComboBoxEdit) != 0;
            Color fill = selected && !collapsed ? SelectorDrawing.Pressed : BackColor;
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            Rectangle textArea = Rectangle.Inflate(e.Bounds, -Math.Max(3, (int)Math.Round(5 * scale)), 0);
            TextRenderer.DrawText(e.Graphics, text, Font, textArea, Enabled ? ForeColor : SelectorDrawing.Disabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (selected && !collapsed) {
                using (Brush brush = new SolidBrush(CardPalette.Mint))
                    e.Graphics.FillRectangle(brush, e.Bounds.X, e.Bounds.Y, Math.Max(2, (int)Math.Round(2 * scale)), e.Bounds.Height);
            }
        }

        private void DrawCollapsed(Graphics graphics)
        {
            Rectangle client = ClientRectangle;
            if (client.Width < 2 || client.Height < 2) return;
            bool active = Enabled && (hover || Focused || DroppedDown);
            using (Brush brush = new SolidBrush(BackColor)) graphics.FillRectangle(brush, client);
            int arrowWidth = Math.Max(8, (int)Math.Round(18 * scale));
            Rectangle arrow = new Rectangle(Math.Max(1, client.Right - arrowWidth), 1, Math.Min(client.Width - 2, arrowWidth - 1), client.Height - 2);
            using (Brush brush = new SolidBrush(active ? SelectorDrawing.Hover : BackColor)) graphics.FillRectangle(brush, arrow);
            Color border = active ? CardPalette.Mint : CardPalette.Border;
            SelectorDrawing.Border(graphics, client, border);
            int padding = Math.Max(2, (int)Math.Round(4 * scale));
            Rectangle textArea = new Rectangle(padding, 1, Math.Max(0, arrow.Left - padding - 2), client.Height - 2);
            string text = SelectedIndex < 0 ? Text : GetItemText(SelectedItem);
            TextRenderer.DrawText(graphics, text, Font, textArea, Enabled ? ForeColor : SelectorDrawing.Disabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            SelectorDrawing.Arrow(graphics, arrow, false, Enabled ? CardPalette.Mint : SelectorDrawing.Disabled, scale);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // Painting after native processing also covers its system-themed
            // arrow. PRINTCLIENT is used by DrawToBitmap and accessibility tools.
            if (message.Msg == 0x000F && IsHandleCreated) {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) DrawCollapsed(graphics);
            } else if ((message.Msg == 0x0318 || message.Msg == 0x0317) && message.WParam != IntPtr.Zero) {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) DrawCollapsed(graphics);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    }

    internal sealed class DarkNumericUpDown : NumericUpDown
    {
        private float scale = 1f;
        private int logicalHeight = 28;
        private bool arranging;
        private bool hover;
        private Control buttons;
        private TextBox edit;
        private ButtonWindow buttonWindow;

        public DarkNumericUpDown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            // Preserve the native border allowance in PreferredHeight so the
            // two arrow targets do not collapse to only a few pixels at 720p.
            // CreateParams suppresses the system edge; OnPaint draws our border.
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = SelectorDrawing.Surface;
            ForeColor = CardPalette.Ink;
            TextAlign = HorizontalAlignment.Center;
            foreach (Control child in Controls) {
                TextBox textbox = child as TextBox;
                if (textbox != null) edit = textbox;
                else buttons = child;
                child.MouseEnter += delegate { hover = true; Invalidate(); };
                child.MouseLeave += delegate { hover = ClientRectangle.Contains(PointToClient(Cursor.Position)); Invalidate(); };
                child.GotFocus += delegate { Invalidate(); };
                child.LostFocus += delegate { Invalidate(); };
            }
            if (buttons != null) {
                buttons.HandleCreated += delegate { AttachButtons(); };
                buttons.HandleDestroyed += delegate { DetachButtons(); };
                buttons.MouseMove += delegate { buttons.Invalidate(); };
                buttons.MouseLeave += delegate { buttons.Invalidate(); };
                buttons.MouseDown += delegate { buttons.Invalidate(); };
                buttons.MouseUp += delegate { buttons.Invalidate(); };
                if (buttons.IsHandleCreated) AttachButtons();
            }
            ApplyScale(1, 28);
        }

        protected override CreateParams CreateParams
        {
            get {
                CreateParams value = base.CreateParams;
                value.Style &= ~0x00800000; // The themed client border replaces WS_BORDER.
                value.ExStyle &= ~0x00000200; // WS_EX_CLIENTEDGE.
                return value;
            }
        }

        public void ApplyScale(double value, int height)
        {
            scale = (float)Math.Max(.35, Math.Min(8, value));
            logicalHeight = Math.Max(20, height);
            // .NET Framework constrains NumericUpDown to PreferredHeight even
            // when AutoSize is false. Keep that supported height; callers center
            // it within their logical row using the actual Height property.
            Height = PreferredHeight;
            ArrangeChildren(); Invalidate(true);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (logicalHeight > 0) ApplyScale(scale, logicalHeight);
        }

        private void ArrangeChildren()
        {
            if (arranging || buttons == null || edit == null || ClientSize.Width < 8) return;
            arranging = true;
            try {
                int width = Math.Min(Math.Max(8, (int)Math.Round(20 * scale)), Math.Max(8, ClientSize.Width / 2));
                int padding = Math.Max(2, (int)Math.Round(4 * scale));
                buttons.SetBounds(ClientSize.Width - width - 1, 1, width, Math.Max(1, ClientSize.Height - 2));
                edit.BackColor = BackColor;
                edit.ForeColor = ForeColor;
                int textHeight = edit.PreferredHeight;
                edit.SetBounds(padding, Math.Max(1, (ClientSize.Height - textHeight) / 2), Math.Max(1, buttons.Left - padding - 2), textHeight);
            } finally { arranging = false; }
        }

        protected override void OnTextBoxResize(object source, EventArgs e)
        {
            // The base callback resets both child bounds when our padded edit
            // is resized. Its data/validation behavior is unrelated to resize.
            if (!arranging) ArrangeChildren();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); ArrangeChildren();
        }

        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); ArrangeChildren(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); ArrangeChildren(); Invalidate(); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); ArrangeChildren(); Invalidate(true); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); ArrangeChildren(); Invalidate(true); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            SelectorDrawing.Border(e.Graphics, ClientRectangle, Enabled && (hover || ContainsFocus) ? CardPalette.Mint : CardPalette.Border);
        }

        private void AttachButtons()
        {
            DetachButtons();
            buttonWindow = new ButtonWindow(this, buttons);
            buttonWindow.AssignHandle(buttons.Handle);
        }

        private void DetachButtons()
        {
            if (buttonWindow == null) return;
            buttonWindow.ReleaseHandle(); buttonWindow = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DetachButtons();
            base.Dispose(disposing);
        }

        private sealed class ButtonWindow : NativeWindow
        {
            private readonly DarkNumericUpDown owner;
            private readonly Control child;
            internal ButtonWindow(DarkNumericUpDown control, Control buttonControl) { owner = control; child = buttonControl; }

            private void Draw(Graphics graphics)
            {
                Rectangle bounds = child.ClientRectangle;
                Point cursor = child.PointToClient(Cursor.Position);
                bool hovered = owner.Enabled && bounds.Contains(cursor);
                bool pressed = hovered && (Control.MouseButtons & MouseButtons.Left) != 0;
                int middle = bounds.Height / 2;
                Rectangle top = new Rectangle(0, 0, bounds.Width, middle);
                Rectangle bottom = new Rectangle(0, middle, bounds.Width, bounds.Height - middle);
                using (Brush brush = new SolidBrush(owner.BackColor)) graphics.FillRectangle(brush, bounds);
                if (hovered) {
                    using (Brush brush = new SolidBrush(pressed ? SelectorDrawing.Pressed : SelectorDrawing.Hover))
                        graphics.FillRectangle(brush, cursor.Y < middle ? top : bottom);
                }
                using (Pen pen = new Pen(CardPalette.Border)) {
                    graphics.DrawLine(pen, 0, 0, 0, bounds.Height);
                    graphics.DrawLine(pen, 0, middle, bounds.Width, middle);
                }
                Color ink = owner.Enabled ? CardPalette.Mint : SelectorDrawing.Disabled;
                SelectorDrawing.Arrow(graphics, top, true, ink, owner.scale);
                SelectorDrawing.Arrow(graphics, bottom, false, ink, owner.scale);
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x000F && child.IsHandleCreated) {
                    using (Graphics graphics = Graphics.FromHwnd(child.Handle)) Draw(graphics);
                } else if ((message.Msg == 0x0318 || message.Msg == 0x0317) && message.WParam != IntPtr.Zero) {
                    using (Graphics graphics = Graphics.FromHdc(message.WParam)) Draw(graphics);
                }
            }
        }
    }
}
