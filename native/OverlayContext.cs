using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GgdHelp
{
    internal sealed class OverlayContext : ApplicationContext
    {
        private readonly string directory;
        private readonly EventWaitHandle restoreEvent;
        private AppState state;
        private Layout layout;
        private Screen display;
        private readonly OverlayForm overlay;
        private readonly CardBackgroundForm background;
        private readonly LauncherForm launcher;
        private SettingsForm settings;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip launcherMenu;
        private readonly List<PlayerCard> cards = new List<PlayerCard>();
        private readonly System.Windows.Forms.Timer saveTimer;
        private readonly System.Windows.Forms.Timer restoreTimer;
        private readonly System.Windows.Forms.Timer repaintTimer;
        private FlowLayoutPanel toolbar;
        private DarkNumericUpDown round;
        private Label summary;
        private Label saveStatus;
        private bool visible = true, locked, calibrating, updating, exiting, initializing = true;
        private string warning = "";
        private Font toolbarFont;

        public OverlayContext(string profileDirectory, EventWaitHandle restore)
        {
            directory = profileDirectory; restoreEvent = restore;
            state = StateStore.Load(directory);
            string startupMessage = StateStore.LastMessage;
            if (StateStore.LoadFailed && MessageBox.Show(startupMessage + "\r\n\r\n继续以空白记录启动？原始数据不会修改。\r\n选择“否”退出，先关闭旧版或修复记录后再启动。", "旧记录读取失败", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                throw new OperationCanceledException("已取消启动，原始记录保持不变。");
            overlay = new OverlayForm { Opacity = 0.90 }; background = new CardBackgroundForm(); launcher = new LauncherForm();
            saveTimer = new System.Windows.Forms.Timer { Interval = 250 };
            saveTimer.Tick += delegate { SaveNow(); };
            restoreTimer = new System.Windows.Forms.Timer { Interval = 250 };
            restoreTimer.Tick += delegate { if (restoreEvent.WaitOne(0)) Restore(); };
            repaintTimer = new System.Windows.Forms.Timer { Interval = 80 };
            repaintTimer.Tick += delegate {
                repaintTimer.Stop();
                if (!exiting && visible) { background.Refresh(); overlay.Refresh(); }
                if (!exiting) launcher.Refresh();
            };
            launcher.ToggleRequested += delegate { SetVisible(!visible); };
            launcher.LockRequested += delegate { SetLocked(!locked); };
            launcher.MenuRequested += ShowMenu;
            launcher.PositionChanged += delegate(Point point) { state.launcherX = point.X; state.launcherY = point.Y; SaveSoon(); };
            overlay.Hotkey += delegate(int id) { if (id == 1) SetVisible(!visible); else if (id == 2) SetLocked(!locked); };
            overlay.EscapeRequested += delegate { SetCalibration(false); if (settings != null) settings.Hide(); };
            overlay.CloseRequested += delegate { SetVisible(false); };
            overlay.ScreenGeometryChanged += ScheduleDisplayRefresh;
            overlay.Activated += delegate { NativeWindows.ShowInactive(launcher); };
            SelectDisplay(state.selectedDisplay, false);
            BuildCards();
            Point? position = state.launcherX.HasValue && state.launcherY.HasValue ? new Point?(new Point(state.launcherX.Value, state.launcherY.Value)) : null;
            launcher.MoveToScreen(position.HasValue ? Screen.FromPoint(position.Value) : display, position);
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "GGD-help V" + Program.Version, Visible = true };
            tray.DoubleClick += delegate { Restore(); };
            tray.ContextMenuStrip = BuildMenu();
            launcherMenu = BuildMenu();
            if (!NativeWindows.RegisterHotKey(overlay.Handle, 1, NativeWindows.MOD_CONTROL | NativeWindows.MOD_SHIFT, Keys.G)) warning = "显示快捷键被占用，请用悬浮开关";
            if (!NativeWindows.RegisterHotKey(overlay.Handle, 2, NativeWindows.MOD_CONTROL | NativeWindows.MOD_SHIFT, Keys.L)) warning = "穿透快捷键被占用，请用悬浮开关";
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            ShowWindows(); initializing = false; restoreTimer.Start();
            if (!String.IsNullOrEmpty(startupMessage)) {
                tray.BalloonTipTitle = "GGD-help 记录"; tray.BalloonTipText = startupMessage; tray.ShowBalloonTip(6000);
                saveStatus.Text = startupMessage;
            }
        }
        private static void Identify(Control control, string id) { control.Name = id; control.AccessibleName = id; }
        private static Color PanelColor { get { return Color.FromArgb(16, 27, 37); } }
        private Button Button(string text, string id, Action action)
        {
            float dpi = NativeWindows.GetDpiScale(display);
            Button button = new Button { Text = text, AutoSize = false, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 55, 67), ForeColor = Color.FromArgb(237, 247, 247), Font = toolbarFont,
                Size = new Size(TextRenderer.MeasureText(text, toolbarFont).Width + (int)(18 * dpi), (int)(32 * dpi)),
                Margin = new Padding((int)(4 * dpi)), UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 99, 110); Identify(button, id);
            button.Click += delegate { action(); }; return button;
        }
        private void BuildToolbar()
        {
            if (toolbar != null) { overlay.Controls.Remove(toolbar); toolbar.Dispose(); }
            if (toolbarFont != null) toolbarFont.Dispose();
            float dpi = NativeWindows.GetDpiScale(display);
            toolbarFont = new Font("Microsoft YaHei UI", 13 * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
            toolbar = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                BackColor = PanelColor, ForeColor = Color.White, Font = toolbarFont, Padding = new Padding((int)(8 * dpi)),
                Location = new Point((int)(16 * dpi), (int)(14 * dpi)), MaximumSize = new Size(Math.Max(300, display.Bounds.Width - (int)(32 * dpi)), 0) };
            Identify(toolbar, "toolbar");
            toolbar.Controls.Add(new Label { Text = "GGD-help  V" + Program.Version, ForeColor = Color.FromArgb(142, 226, 195),
                Size = new Size((int)(152 * dpi), (int)(32 * dpi)), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding((int)(4 * dpi)) });
            toolbar.Controls.Add(Button("−", "round-prev", delegate { ChangeRound(state.session.round - 1); }));
            round = new DarkNumericUpDown { Minimum = 1, Maximum = 99, Value = state.session.round, Width = (int)(70 * dpi),
                Font = toolbarFont, TextAlign = HorizontalAlignment.Center, Margin = new Padding((int)(4 * dpi)) };
            round.ApplyScale(dpi, 32);
            int roundSpace = Math.Max(0, (int)(32 * dpi) - round.Height);
            round.Margin = new Padding((int)(4 * dpi), (int)(4 * dpi) + roundSpace / 2,
                (int)(4 * dpi), (int)(4 * dpi) + roundSpace - roundSpace / 2);
            Identify(round, "round-input");
            round.ValueChanged += delegate { if (!updating) ChangeRound((int)round.Value); };
            toolbar.Controls.Add(round);
            toolbar.Controls.Add(Button("＋", "round-next", delegate { ChangeRound(state.session.round + 1); }));
            summary = new Label { Size = new Size((int)(110 * dpi), (int)(32 * dpi)), TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding((int)(4 * dpi)), ForeColor = Color.FromArgb(172, 189, 199) };
            toolbar.Controls.Add(summary);
            toolbar.Controls.Add(Button("穿透游戏", "lock-button", delegate { SetLocked(true); }));
            toolbar.Controls.Add(Button("位置 / 设置", "settings-toggle", ShowSettings));
            toolbar.Controls.Add(Button("新对局", "new-game", NewMatch));
            toolbar.Controls.Add(Button("收起", "hide-button", delegate { SetVisible(false); }));
            toolbar.Controls.Add(Button("退出", "quit-button", Quit));
            saveStatus = new Label { Size = new Size((int)(180 * dpi), (int)(32 * dpi)), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Margin = new Padding((int)(4 * dpi)),
                ForeColor = Color.FromArgb(142, 226, 195), Text = "自动保存" };
            Identify(saveStatus, "save-status"); toolbar.Controls.Add(saveStatus);
            overlay.Controls.Add(toolbar); toolbar.BringToFront(); UpdateSummary();
        }
        private void SelectDisplay(string name, bool moveLauncher)
        {
            display = Screen.PrimaryScreen;
            foreach (Screen item in Screen.AllScreens) if (item.DeviceName == name) display = item;
            state.selectedDisplay = display.DeviceName;
            float dpi = NativeWindows.GetDpiScale(display);
            string key = "native:" + display.DeviceName + "|" + display.Bounds.Width + "x" + display.Bounds.Height + "|dpi:" + dpi.ToString("0.###", CultureInfo.InvariantCulture);
            if (!state.profiles.TryGetValue(key, out layout)) {
                // Electron's screen bounds were DIP. Preserve a uniquely matching
                // older calibration when its monitor geometry is unambiguous.
                string suffix = "|" + (int)Math.Round(display.Bounds.Width / dpi) + "x" + (int)Math.Round(display.Bounds.Height / dpi) + "|dpi:" + dpi.ToString("0.###", CultureInfo.InvariantCulture);
                Layout legacy = null; int matches = 0;
                foreach (KeyValuePair<string, Layout> item in state.profiles) if (item.Key.StartsWith("display:", StringComparison.Ordinal) && item.Key.EndsWith(suffix, StringComparison.Ordinal)) { legacy = item.Value; matches++; }
                layout = matches == 1 ? Model.NormalizeLayout(legacy) : Model.CreateLayout();
                state.profiles[key] = layout;
            }
            overlay.Bounds = display.Bounds; background.Bounds = display.Bounds;
            BuildToolbar(); RefreshCards();
            if (settings != null) settings.UpdateModels(state, layout, state.selectedDisplay);
            if (moveLauncher) launcher.MoveToScreen(display, null);
            if (!initializing) { ShowWindows(); SaveSoon(); }
        }
        private void BuildCards()
        {
            foreach (PlayerCard card in cards) { overlay.Controls.Remove(card); card.Dispose(); }
            cards.Clear();
            for (int i = 0; i < state.session.players.Count; i++) {
                int index = i;
                PlayerCard card = new PlayerCard(state.session.players[i], delegate { return state.session.round; });
                card.Changed += delegate { UpdateSummary(); SaveSoon(); };
                card.DragRequested += delegate(Point delta) {
                    if (!calibrating || locked) return;
                    CardPosition current = Model.ComputePositions(layout, display.Bounds.Width, display.Bounds.Height)[index];
                    double nicknameGap = 29 * current.scale;
                    double x = Math.Max(0, Math.Min(card.Left + delta.X, display.Bounds.Width - card.Width));
                    double y = Math.Max(0, Math.Min(card.Top - nicknameGap + delta.Y, display.Bounds.Height - card.Height - nicknameGap));
                    Layout moved = Model.MovePlayer(layout, index + 1, x, y, display.Bounds.Width, display.Bounds.Height);
                    // Keep SettingsForm's reference to the active Layout alive.
                    layout.positions = moved.positions;
                    RefreshCards(); SaveSoon();
                };
                cards.Add(card); overlay.Controls.Add(card);
            }
            RefreshCards();
        }
        private void RefreshCards()
        {
            if (layout == null || display == null) return;
            List<CardPosition> points = Model.ComputePositions(layout, display.Bounds.Width, display.Bounds.Height);
            List<Rectangle> rectangles = new List<Rectangle>();
            bool any = state.preferences.features.roles || state.preferences.features.notes || state.preferences.features.death;
            for (int i = 0; i < cards.Count; i++) {
                CardPosition point = points[i]; PlayerCard card = cards[i];
                card.Configure(state.session.players[i], state.preferences, calibrating, point.scale);
                card.SetEditing(!locked);
                // Calibration adds a handle below the controls. Clamp the actual
                // rendered footprint so that handle remains reachable at edges.
                card.Location = new Point(
                    Math.Max(0, Math.Min((int)Math.Round(point.x), display.Bounds.Width - card.Width)),
                    Math.Max(0, Math.Min((int)Math.Round(point.y + 29 * point.scale), display.Bounds.Height - card.Height)));
                bool showCard = i < state.preferences.playerCount && (any || calibrating);
                card.Visible = showCard;
                // Visible also reflects the parent's state, which is false while
                // preparing a hidden overlay before its first nonactive show.
                if (showCard) rectangles.Add(card.Bounds);
            }
            background.UpdateCards(rectangles, state.preferences.opacity);
            if (toolbar != null) toolbar.BringToFront();
            UpdateSummary();
            // Updating layered window attributes can invalidate their cached
            // child surfaces without triggering a WinForms paint for each child.
            if (overlay.Visible) overlay.Refresh();
        }
        private void ChangeRound(int value)
        {
            state.session.round = Math.Max(1, Math.Min(99, value));
            RefreshCards(); SaveSoon();
        }
        private void UpdateSummary()
        {
            if (summary == null || round == null) return;
            updating = true; round.Value = state.session.round; updating = false;
            int alive = 0;
            for (int i = 0; i < state.preferences.playerCount; i++) if (state.session.players[i].death == null) alive++;
            summary.Text = alive + " / " + state.preferences.playerCount + " 人存活";
        }
        private void SetCalibration(bool value)
        {
            calibrating = value && !locked;
            RefreshCards();
            if (settings != null) settings.SetCalibration(calibrating);
        }
        private void ShowSettings()
        {
            if (!visible) SetVisible(true);
            if (locked) SetLocked(false);
            if (settings == null || settings.IsDisposed) {
                settings = new SettingsForm(state, layout, Screen.AllScreens, state.selectedDisplay);
                settings.Changed += delegate { RefreshCards(); SaveSoon(); };
                settings.DisplaySelected += delegate(string name) { SelectDisplay(name, true); };
                settings.CalibrationChanged += SetCalibration;
                settings.ResetLayoutRequested += ResetLayout;
                settings.ImportRequested += Import;
                settings.ExportRequested += Export;
                settings.TopMost = true;
                settings.StartPosition = FormStartPosition.Manual;
                settings.Location = NativeWindows.ClampToWorkingArea(new Point(display.WorkingArea.Right - settings.Width - 20, display.WorkingArea.Top + 90), settings.Size, display);
            }
            settings.UpdateModels(state, layout, state.selectedDisplay);
            settings.Show();
            // Show creates the handle and applies Windows' DPI scaling. Clamp
            // afterwards so the scrollable settings remain reachable on 720p
            // screens and on monitors using a large display scale.
            Rectangle area = Screen.FromControl(settings).WorkingArea;
            settings.Size = new Size(Math.Min(settings.Width, area.Width), Math.Min(settings.Height, area.Height));
            settings.Location = NativeWindows.ClampToWorkingArea(settings.Location, settings.Size, Screen.FromControl(settings));
            settings.Activate();
        }
        private void SetVisible(bool value)
        {
            visible = value;
            if (!value) {
                SetCalibration(false); if (settings != null) settings.Hide();
                NativeWindows.SetClickThrough(overlay, true); overlay.Hide(); background.Hide();
            } else ShowWindows();
            UpdateLauncher(); SaveNow();
        }
        private void SetLocked(bool value)
        {
            locked = value;
            if (locked) { SetCalibration(false); if (settings != null) settings.Hide(); }
            foreach (PlayerCard card in cards) card.SetEditing(!locked);
            toolbar.Enabled = !locked;
            NativeWindows.SetClickThrough(overlay, locked || !visible);
            if (visible) ShowWindows();
            UpdateLauncher();
        }
        private void ShowWindows()
        {
            if (visible) {
                NativeWindows.ShowInactive(background); NativeWindows.SetClickThrough(background, true);
                NativeWindows.ShowInactive(overlay); NativeWindows.SetClickThrough(overlay, locked);
                overlay.Bounds = display.Bounds; background.Bounds = display.Bounds;
            }
            NativeWindows.ShowInactive(launcher); UpdateLauncher();
            // Repaint after the final layered/NOACTIVATE style transitions. In
            // particular, the first show must render untouched toolbar controls.
            if (visible) { background.Refresh(); overlay.Refresh(); }
            launcher.Refresh();
            // The first layered surface may not exist until after ShowWindow
            // returns to the message loop. Repaint once after it is composited.
            repaintTimer.Stop(); repaintTimer.Start();
        }
        private void UpdateLauncher()
        {
            launcher.UpdateState(visible, locked, warning);
        }
        private void Restore()
        {
            SetLocked(false); SetVisible(true); launcher.MoveToScreen(display, null);
        }
        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripItem visibility = menu.Items.Add("", null, MenuAction(delegate { SetVisible(!visible); }));
            ToolStripItem editing = menu.Items.Add("", null, MenuAction(delegate { SetLocked(!locked); }));
            menu.Items.Add("位置 / 设置", null, MenuAction(ShowSettings));
            ToolStripMenuItem monitors = new ToolStripMenuItem("游戏所在显示器");
            menu.Items.Add(monitors); menu.Items.Add("找回开关并恢复编辑", null, MenuAction(Restore));
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("导出记录 JSON", null, MenuAction(Export));
            menu.Items.Add("退出 GGD-help", null, MenuAction(Quit));
            menu.Opening += delegate {
                visibility.Text = visible ? "收起蒙版  Ctrl+Shift+G" : "显示蒙版  Ctrl+Shift+G";
                editing.Text = locked ? "允许编辑  Ctrl+Shift+L" : "穿透游戏  Ctrl+Shift+L";
                // Refresh before opening, never replace or dispose the menu
                // while Windows is still dispatching an item click or close.
                while (monitors.DropDownItems.Count > 0) {
                    ToolStripItem old = monitors.DropDownItems[0];
                    monitors.DropDownItems.Remove(old); old.Dispose();
                }
                foreach (Screen item in Screen.AllScreens) {
                    Screen selected = item;
                    ToolStripMenuItem entry = new ToolStripMenuItem(item.DeviceName + " · " + item.Bounds.Width + "×" + item.Bounds.Height) { Checked = item.DeviceName == state.selectedDisplay };
                    entry.Click += MenuAction(delegate { SelectDisplay(selected.DeviceName, true); });
                    monitors.DropDownItems.Add(entry);
                }
            };
            return menu;
        }
        private EventHandler MenuAction(Action action)
        {
            return delegate {
                if (exiting || overlay.IsDisposed || !overlay.IsHandleCreated) return;
                // ToolStripDropDown reparents its native window after Click and
                // Closed events return. Finish that dispatch before changing
                // windows, opening a dialog or disposing menus during Quit.
                overlay.BeginInvoke((Action)delegate { if (!exiting) action(); });
            };
        }
        private void ShowMenu()
        {
            launcherMenu.Show(launcher, new Point(launcher.Width - 10, launcher.Height));
        }
        private void NewMatch()
        {
            if (MessageBox.Show(overlay, "清空所有玩家的身份、备注和死亡记录，回到第 1 轮？\r\n位置校准和功能开关会保留。", "开始新对局", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            state.session = Model.CreateSession(); BuildCards(); SaveNow();
        }
        private void ResetLayout()
        {
            if (MessageBox.Show(settings, "重置当前屏幕的布局和逐人校准？玩家记录与其他屏幕位置会保留。", "重置位置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Layout fresh = Model.CreateLayout(); layout.offsetX = 0; layout.offsetY = 0; layout.scale = 1; layout.cardScale = 1; layout.positions = fresh.positions;
            RefreshCards(); settings.UpdateModels(state, layout, state.selectedDisplay); SaveNow();
        }
        private void Import()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "GGD-help 记录 (*.json)|*.json", Title = "导入记录" }) {
                if (dialog.ShowDialog(settings) != DialogResult.OK) return;
                try {
                    AppState imported = StateStore.ImportFile(dialog.FileName);
                    if (MessageBox.Show(settings, "导入会替换当前记录和布局，当前记录会留作备份。继续？", "导入记录", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    SaveNow(); state = imported; SelectDisplay(state.selectedDisplay, false); BuildCards();
                    settings.UpdateModels(state, layout, state.selectedDisplay); SaveNow();
                } catch (Exception error) { MessageBox.Show(settings, error.Message, "无法导入", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void Export()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "GGD-help 记录 (*.json)|*.json", FileName = "GGD-help-export.json", Title = "导出记录" }) {
                if (dialog.ShowDialog(settings != null && settings.Visible ? (IWin32Window)settings : launcher) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, StateStore.ExportJson(state), new System.Text.UTF8Encoding(false)); }
                catch (Exception error) { MessageBox.Show(error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void SaveSoon()
        {
            if (initializing || exiting) return;
            saveTimer.Stop(); saveTimer.Start();
        }
        private void SaveNow()
        {
            saveTimer.Stop(); if (initializing) return;
            try { StateStore.Save(directory, state); saveStatus.Text = "已自动保存"; }
            catch (Exception error) { saveStatus.Text = "保存失败，请勿关闭"; warning = "保存失败：" + error.Message; launcher.UpdateState(visible, locked, warning); }
        }
        private void ScheduleDisplayRefresh()
        {
            if (overlay.IsDisposed || !overlay.IsHandleCreated || exiting) return;
            overlay.BeginInvoke((Action)delegate { if (!exiting) { SelectDisplay(state.selectedDisplay, false); launcher.MoveToScreen(Screen.FromPoint(launcher.Location), launcher.Location); } });
        }
        private void OnDisplaySettingsChanged(object sender, EventArgs args) { ScheduleDisplayRefresh(); }
        private void Quit()
        {
            if (exiting) return; exiting = true; SaveNow();
            restoreTimer.Stop(); saveTimer.Stop(); repaintTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            NativeWindows.UnregisterHotKey(overlay.Handle, 1); NativeWindows.UnregisterHotKey(overlay.Handle, 2);
            tray.Visible = false;
            ContextMenuStrip trayMenu = tray.ContextMenuStrip;
            tray.ContextMenuStrip = null; tray.Dispose();
            trayMenu.Dispose(); launcherMenu.Dispose();
            if (settings != null) settings.Dispose(); launcher.Dispose(); background.Dispose(); overlay.Exiting = true; overlay.Dispose();
            saveTimer.Dispose(); restoreTimer.Dispose(); repaintTimer.Dispose(); if (toolbarFont != null) toolbarFont.Dispose(); ExitThread();
        }
        protected override void ExitThreadCore()
        {
            if (!exiting) { Quit(); return; }
            base.ExitThreadCore();
        }
    }
}
