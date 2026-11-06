using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GgdHelp
{
    internal class OverlayForm : Form
    {
        public event Action<int> Hotkey;
        public event Action EscapeRequested;
        public event Action CloseRequested;
        public event Action ScreenGeometryChanged;
        public bool Exiting;
        public OverlayForm()
        {
            Text = "GGD-help V" + Program.Version + " · 会议蒙版";
            Name = "overlay"; AccessibleName = "overlay";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = CardPalette.TransparentKey; TransparencyKey = CardPalette.TransparentKey;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { CreateParams value = base.CreateParams; value.ExStyle |= NativeWindows.WS_EX_TOOLWINDOW; return value; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Exiting && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true; if (CloseRequested != null) CloseRequested();
            }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { if (EscapeRequested != null) EscapeRequested(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeWindows.WM_HOTKEY) { if (Hotkey != null) Hotkey(m.WParam.ToInt32()); return; }
            base.WndProc(ref m);
            if (m.Msg == NativeWindows.WM_DPICHANGED && ScreenGeometryChanged != null) ScreenGeometryChanged();
        }
    }

    internal sealed class CardBackgroundForm : OverlayForm
    {
        private readonly List<Rectangle> rectangles = new List<Rectangle>();
        public CardBackgroundForm()
        {
            Text = "GGD-help · 透明底板"; Name = "card-background"; AccessibleName = Name;
            Exiting = true;
            // Clip the window itself instead of combining color-key and alpha
            // across a full display. An empty initial region prevents a dark
            // full-screen frame before the first card layout is ready.
            TransparencyKey = Color.Empty;
            BackColor = CardPalette.Dark;
            Region shape = new Region();
            shape.MakeEmpty();
            Region = shape;
        }
        public void UpdateCards(IEnumerable<Rectangle> values, double opacity)
        {
            List<Rectangle> next = new List<Rectangle>(values);
            bool changed = next.Count != rectangles.Count;
            for (int i = 0; !changed && i < next.Count; i++) changed = next[i] != rectangles[i];
            if (changed) {
                Region shape = new Region();
                shape.MakeEmpty();
                foreach (Rectangle rect in next) shape.Union(rect);
                Region previous = Region;
                Region = shape;
                if (previous != null) previous.Dispose();
                rectangles.Clear(); rectangles.AddRange(next);
            }
            double alpha = Math.Max(0.15, Math.Min(0.95, opacity / 100));
            if (Math.Abs(Opacity - alpha) > 0.001) Opacity = alpha;
            NativeWindows.SetClickThrough(this, true);
            if (changed) Invalidate();
        }
    }
}
