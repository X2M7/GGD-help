using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GgdHelp
{
    internal sealed class LauncherForm : Form
    {
        public event Action ToggleRequested;
        public event Action LockRequested;
        public event Action MenuRequested;
        public event Action<Point> PositionChanged;
        private readonly Label handle;
        private readonly Label hint;
        private readonly Button toggle;
        private readonly Button mode;
        private readonly Button menu;
        private readonly ToolTip tips;
        private float dpiScale = 1f;
        private Font normalFont;
        private Font smallFont;
        private Font handleFont;
        private bool arranging;
        private static readonly Color PanelColor = Color.FromArgb(16, 32, 39);
        private static readonly Color TextColor = Color.FromArgb(237, 247, 247);
        private static readonly Color MintColor = Color.FromArgb(142, 226, 195);

        public LauncherForm()
        {
            Text = "GGD-help · 悬浮开关";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = PanelColor;
            ForeColor = TextColor;
            DoubleBuffered = true;
            tips = new ToolTip();
            handle = new Label { Text = "⠿", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.SizeAll, ForeColor = MintColor };
            handle.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) NativeWindows.BeginMove(this);
            };
            hint = new Label { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter, ForeColor = MintColor };
            toggle = MakeButton("收起蒙版");
            toggle.Name = toggle.AccessibleName = "launcher-toggle";
            toggle.BackColor = Color.FromArgb(41, 77, 64);
            mode = MakeButton("穿透");
            mode.Name = mode.AccessibleName = "launcher-lock";
            menu = MakeButton("···");
            menu.Name = menu.AccessibleName = "launcher-menu";
            toggle.Click += delegate { if (ToggleRequested != null) ToggleRequested(); };
            mode.Click += delegate { if (LockRequested != null) LockRequested(); };
            menu.Click += delegate { if (MenuRequested != null) MenuRequested(); };
            Controls.AddRange(new Control[] { handle, toggle, mode, menu, hint });
            tips.SetToolTip(handle, "拖动这里移动悬浮开关");
            tips.SetToolTip(menu, "显示菜单");
            ApplyScale(NativeWindows.GetDpiScale(Screen.PrimaryScreen));
            MoveToScreen(Screen.PrimaryScreen, null);
            UpdateState(true, false, null);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeWindows.WS_EX_NOACTIVATE | NativeWindows.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private Button MakeButton(string text)
        {
            Button button = new Button {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = PanelColor,
                ForeColor = TextColor, Cursor = Cursors.Hand, TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 77, 83);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(63, 96, 99);
            return button;
        }

        public void UpdateState(bool visible, bool locked, string warning)
        {
            toggle.Text = visible ? "收起蒙版" : "显示蒙版";
            mode.Text = locked ? "编辑" : "穿透";
            tips.SetToolTip(toggle, "显示 / 隐藏蒙版 · Ctrl+Shift+G");
            tips.SetToolTip(mode, locked ? "允许鼠标编辑记录 · Ctrl+Shift+L" : "让鼠标穿透到游戏 · Ctrl+Shift+L");
            hint.Text = String.IsNullOrEmpty(warning)
                ? (locked ? "鼠标穿透 · Ctrl+Shift+L 编辑" : "记录模式 · Ctrl+Shift+G 开关")
                : warning;
            hint.ForeColor = String.IsNullOrEmpty(warning) ? MintColor : Color.FromArgb(255, 200, 139);
            tips.SetToolTip(hint, hint.Text);
        }

        public void MoveToScreen(Screen target, Point? savedPosition)
        {
            if (target == null) target = Screen.PrimaryScreen;
            ApplyScale(NativeWindows.GetDpiScale(target));
            Rectangle area = target.WorkingArea;
            Point desired = savedPosition ?? new Point(area.Right - Width - Px(16), area.Top + Px(16));
            Location = NativeWindows.ClampToWorkingArea(desired, Size, target);
        }

        private int Px(int value) { return (int)Math.Round(value * dpiScale); }

        private void ApplyScale(float scale)
        {
            // WinForms may retain an equivalent Font instead of assigning the
            // new instance. Replacing then disposing it at the same DPI would
            // leave controls holding a disposed font.
            if (normalFont != null && Math.Abs(scale - dpiScale) < 0.001f) return;
            arranging = true;
            SuspendLayout();
            try
            {
                dpiScale = scale;
                Font previousNormal = normalFont, previousSmall = smallFont, previousHandle = handleFont;
                normalFont = new Font("Microsoft YaHei UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                smallFont = new Font("Microsoft YaHei UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                handleFont = new Font("Segoe UI", 24f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                toggle.Font = mode.Font = menu.Font = normalFont;
                hint.Font = smallFont;
                handle.Font = handleFont;
                ClientSize = new Size(Px(280), Px(64));
                handle.SetBounds(Px(6), Px(6), Px(25), Px(36));
                toggle.SetBounds(Px(35), Px(6), Px(129), Px(36));
                mode.SetBounds(Px(169), Px(6), Px(61), Px(36));
                menu.SetBounds(Px(235), Px(6), Px(36), Px(36));
                hint.SetBounds(Px(35), Px(44), Px(236), Px(16));
                if (previousNormal != null) previousNormal.Dispose();
                if (previousSmall != null) previousSmall.Dispose();
                if (previousHandle != null) previousHandle.Dispose();
            }
            finally { ResumeLayout(false); arranging = false; }
            Invalidate();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (!arranging && IsHandleCreated && PositionChanged != null) PositionChanged(Location);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            Location = NativeWindows.ClampToWorkingArea(Location, Size, Screen.FromRectangle(Bounds));
            if (PositionChanged != null) PositionChanged(Location);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(79, 111, 112)))
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) // MA_NOACTIVATE still delivers the mouse click.
            {
                message.Result = new IntPtr(3);
                return;
            }
            if (message.Msg == NativeWindows.WM_DPICHANGED)
            {
                int dpi = (int)(message.WParam.ToInt64() & 0xffff);
                NativeWindows.NativeRect rectangle = (NativeWindows.NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeWindows.NativeRect));
                ApplyScale(dpi / 96f);
                Location = NativeWindows.ClampToWorkingArea(new Point(rectangle.Left, rectangle.Top), Size,
                    Screen.FromPoint(new Point(rectangle.Left, rectangle.Top)));
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                if (normalFont != null) normalFont.Dispose();
                if (smallFont != null) smallFont.Dispose();
                if (handleFont != null) handleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
