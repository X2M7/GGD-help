using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GgdHelp
{
    public sealed class SettingsForm : Form
    {
        private AppState state;
        private Layout layout;
        private Screen[] screens;
        private string selectedDisplay;
        private bool updating;
        private readonly CheckBox roles;
        private readonly CheckBox notes;
        private readonly CheckBox death;
        private readonly CheckBox calibration;
        private readonly DarkComboBox display;
        private readonly DarkComboBox aspect;
        private readonly DarkComboBox preset;
        private readonly DarkNumericUpDown playerCount;
        private readonly Label displayInfo;
        private readonly TrackBar[] sliders = new TrackBar[5];
        private readonly Label[] sliderValues = new Label[5];
        private readonly Panel content;

        public event Action Changed;
        public event Action<string> DisplaySelected;
        public event Action<bool> CalibrationChanged;
        public event Action ResetLayoutRequested;
        public event Action ImportRequested;
        public event Action ExportRequested;

        public Layout LayoutModel
        {
            get { return layout; }
            set { UpdateModels(state, value, selectedDisplay); }
        }

        public SettingsForm(AppState model, Layout currentLayout, Screen[] availableScreens, string displayName)
        {
            state = model;
            layout = currentLayout;
            screens = availableScreens ?? Screen.AllScreens;
            selectedDisplay = displayName;
            Text = "GGD-help · 位置 / 设置";
            Name = "SettingsForm";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 760);
            BackColor = Color.FromArgb(16, 27, 37);
            ForeColor = CardPalette.Ink;
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Regular, GraphicsUnit.Point);

            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = BackColor;
            Controls.Add(scroll);
            content = new Panel();
            content.Location = new Point(18, 16);
            content.Size = new Size(376, 1060);
            content.BackColor = BackColor;
            scroll.Controls.Add(content);

            Label title = LabelAt("对齐你的会议画面", 0, 0, 282, 34);
            title.Font = new Font(Font.FontFamily, 15, FontStyle.Bold, GraphicsUnit.Point);
            Button close = ButtonAt("关闭", 306, 0, 70, 32);
            Identify(close, "settings-close");
            close.Click += delegate { Hide(); };
            Label featureHeading = LabelAt("显示功能", 0, 44, 376, 25);
            featureHeading.ForeColor = CardPalette.Mint;
            roles = CheckAt("人物身份判定", 0, 76);
            notes = CheckAt("备注 1 和 2", 0, 105);
            death = CheckAt("死亡方式与死亡轮次", 0, 134);
            Identify(roles, "feature-roles");
            Identify(notes, "feature-notes");
            Identify(death, "feature-death");
            LabelAt("关闭只隐藏内容，已有记录会保留。", 0, 166, 376, 24).ForeColor = CardPalette.Muted;
            roles.CheckedChanged += delegate { if (!updating) { state.preferences.features.roles = roles.Checked; RaiseChanged(); } };
            notes.CheckedChanged += delegate { if (!updating) { state.preferences.features.notes = notes.Checked; RaiseChanged(); } };
            death.CheckedChanged += delegate { if (!updating) { state.preferences.features.death = death.Checked; RaiseChanged(); } };

            LabelAt("游戏所在显示器", 0, 204, 376, 24);
            display = ComboAt(0, 232, 376);
            Identify(display, "display-select");
            displayInfo = LabelAt("", 0, 266, 376, 44);
            displayInfo.ForeColor = CardPalette.Muted;
            display.SelectedIndexChanged += delegate
            {
                if (updating) return;
                Choice choice = display.SelectedItem as Choice;
                if (choice == null) return;
                selectedDisplay = choice.Value;
                Action<string> callback = DisplaySelected;
                if (callback != null) callback(selectedDisplay);
            };
            LabelAt("参考分辨率", 0, 312, 376, 24);
            preset = ComboAt(0, 340, 376);
            Identify(preset, "resolution-preset");
            preset.Items.Add("选择参考分辨率");
            foreach (ResolutionPreset item in Model.ResolutionPresets) preset.Items.Add(item);
            preset.SelectedIndex = 0;
            preset.SelectedIndexChanged += delegate
            {
                if (updating) return;
                ResolutionPreset chosen = preset.SelectedItem as ResolutionPreset;
                if (chosen == null) return;
                layout.aspect = chosen.aspect;
                SetAspectSelection();
                RaiseChanged();
            };
            LabelAt("预设只调整画面比例，不会修改系统分辨率。", 0, 373, 376, 24).ForeColor = CardPalette.Muted;
            LabelAt("游戏画面比例", 0, 411, 215, 24);
            aspect = ComboAt(0, 439, 215);
            Identify(aspect, "aspect-select");
            string[] aspectNames = { "16:9（居中）", "16:10（居中）", "21:9（居中）", "3440:1440（超宽）", "2560:1080（超宽）", "铺满屏幕" };
            for (int index = 0; index < Model.Aspects.Length; index++) aspect.Items.Add(new Choice(Model.Aspects[index], aspectNames[index]));
            aspect.SelectedIndexChanged += delegate
            {
                if (updating) return;
                Choice chosen = aspect.SelectedItem as Choice;
                if (chosen == null) return;
                layout.aspect = chosen.Value;
                updating = true;
                preset.SelectedIndex = 0;
                updating = false;
                RaiseChanged();
            };
            LabelAt("显示人数", 239, 411, 137, 24);
            playerCount = new DarkNumericUpDown();
            playerCount.SetBounds(239, 439, 137, 30);
            playerCount.ApplyScale(1, 30);
            playerCount.TextAlign = HorizontalAlignment.Center;
            playerCount.Minimum = 1;
            playerCount.Maximum = 16;
            playerCount.BackColor = CardPalette.Input;
            playerCount.ForeColor = CardPalette.Ink;
            Identify(playerCount, "player-count");
            content.Controls.Add(playerCount);
            playerCount.ValueChanged += delegate { if (!updating) { state.preferences.playerCount = (int)playerCount.Value; RaiseChanged(); } };
            calibration = CheckAt("拖动校准角色位置", 0, 487);
            Identify(calibration, "calibration-toggle");
            calibration.ForeColor = CardPalette.Mint;
            calibration.CheckedChanged += delegate
            {
                if (updating) return;
                Action<bool> callback = CalibrationChanged;
                if (callback != null) callback(calibration.Checked);
            };
            LabelAt("在游戏发言轮，拖动记录卡的校准柄对齐人物。", 0, 521, 376, 24).ForeColor = CardPalette.Muted;

            AddSlider(0, "整体左右", 559, -70, 70);
            AddSlider(1, "整体上下", 627, -70, 70);
            AddSlider(2, "位置间距", 695, 60, 145);
            AddSlider(3, "记录卡大小", 763, 70, 150);
            AddSlider(4, "记录卡底色", 831, 15, 95);
            Button reset = ButtonAt("重置当前屏幕位置", 0, 907, 188, 34);
            Identify(reset, "reset-layout");
            reset.Click += delegate { Action callback = ResetLayoutRequested; if (callback != null) callback(); };
            LabelAt("设置会自动保存", 208, 912, 168, 26).ForeColor = CardPalette.Muted;
            Button import = ButtonAt("导入记录", 0, 955, 180, 34);
            Identify(import, "import-records");
            import.Click += delegate { Action callback = ImportRequested; if (callback != null) callback(); };
            Button export = ButtonAt("导出记录", 196, 955, 180, 34);
            Identify(export, "export-records");
            export.Click += delegate { Action callback = ExportRequested; if (callback != null) callback(); };
            LabelAt("Ctrl + Shift + G：显示 / 隐藏\r\nCtrl + Shift + L：编辑 / 穿透", 0, 1004, 376, 50).ForeColor = CardPalette.Muted;
            UpdateModels(model, currentLayout, displayName);
        }

        private Label LabelAt(string text, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(x, y, width, height);
            label.ForeColor = CardPalette.Ink;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(label);
            return label;
        }

        private static void Identify(Control control, string name)
        { control.Name = name; control.AccessibleName = name; }

        private CheckBox CheckAt(string text, int x, int y)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.SetBounds(x, y, 376, 28);
            check.ForeColor = CardPalette.Ink;
            check.BackColor = BackColor;
            check.UseVisualStyleBackColor = false;
            content.Controls.Add(check);
            return check;
        }

        private DarkComboBox ComboAt(int x, int y, int width)
        {
            DarkComboBox combo = new DarkComboBox();
            combo.SetBounds(x, y, width, 30);
            combo.ApplyScale(1, 30);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = CardPalette.Input;
            combo.ForeColor = CardPalette.Ink;
            content.Controls.Add(combo);
            return combo;
        }

        private Button ButtonAt(string text, int x, int y, int width, int height)
        {
            Button button = CardPalette.Button(text);
            button.SetBounds(x, y, width, height);
            content.Controls.Add(button);
            return button;
        }

        private void AddSlider(int index, string title, int y, int min, int max)
        {
            LabelAt(title, 0, y, 250, 24);
            Label value = LabelAt("", 260, y, 116, 24);
            value.ForeColor = CardPalette.Mint;
            value.TextAlign = ContentAlignment.MiddleRight;
            sliderValues[index] = value;
            TrackBar track = new TrackBar();
            track.SetBounds(0, y + 27, 376, 34);
            track.Minimum = min;
            track.Maximum = max;
            track.SmallChange = 1;
            track.LargeChange = 5;
            track.TickStyle = TickStyle.None;
            track.AutoSize = false;
            string[] names = { "offset-x", "offset-y", "layout-scale", "card-scale", "panel-opacity" };
            Identify(track, names[index]);
            content.Controls.Add(track);
            sliders[index] = track;
            track.ValueChanged += delegate
            {
                UpdateSliderCaption(index);
                if (updating) return;
                if (index == 0) layout.offsetX = track.Value / 200.0;
                else if (index == 1) layout.offsetY = track.Value / 200.0;
                else if (index == 2) layout.scale = track.Value / 100.0;
                else if (index == 3) layout.cardScale = track.Value / 100.0;
                else state.preferences.opacity = track.Value;
                RaiseChanged();
            };
        }

        private void UpdateSliderCaption(int index)
        {
            double value = index < 2 ? sliders[index].Value / 2.0 : sliders[index].Value;
            sliderValues[index].Text = value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private void SetAspectSelection()
        {
            bool previous = updating;
            updating = true;
            for (int index = 0; index < aspect.Items.Count; index++)
            {
                Choice choice = aspect.Items[index] as Choice;
                if (choice != null && choice.Value == layout.aspect) { aspect.SelectedIndex = index; break; }
            }
            updating = previous;
        }

        public void UpdateModels(AppState model, Layout currentLayout, string displayName)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (currentLayout == null) throw new ArgumentNullException("currentLayout");
            state = model;
            layout = currentLayout;
            selectedDisplay = displayName;
            updating = true;
            try
            {
                roles.Checked = state.preferences.features.roles;
                notes.Checked = state.preferences.features.notes;
                death.Checked = state.preferences.features.death;
                playerCount.Value = Math.Max(1, Math.Min(16, state.preferences.playerCount));
                screens = Screen.AllScreens;
                display.Items.Clear();
                Screen chosen = null;
                for (int index = 0; index < screens.Length; index++)
                {
                    Screen screen = screens[index];
                    display.Items.Add(new Choice(screen.DeviceName, "显示器 " + (index + 1) + " · " + screen.Bounds.Width + " × " + screen.Bounds.Height + (screen.Primary ? "（主屏）" : "")));
                    if (screen.DeviceName == selectedDisplay) { display.SelectedIndex = index; chosen = screen; }
                }
                if (chosen == null && screens.Length > 0) { display.SelectedIndex = 0; chosen = screens[0]; }
                displayInfo.Text = chosen == null ? "未找到可用显示器" : "此显示器的位置校准会单独保存。\r\n当前画面：" + chosen.Bounds.Width + " × " + chosen.Bounds.Height + " 像素";
                SetAspectSelection();
                ResolutionPreset selectedPreset = preset.SelectedItem as ResolutionPreset;
                if (selectedPreset != null && selectedPreset.aspect != layout.aspect) preset.SelectedIndex = 0;
                double[] values = { layout.offsetX * 200, layout.offsetY * 200, layout.scale * 100, layout.cardScale * 100, state.preferences.opacity };
                for (int index = 0; index < sliders.Length; index++)
                {
                    sliders[index].Value = Math.Max(sliders[index].Minimum, Math.Min(sliders[index].Maximum, (int)Math.Round(values[index])));
                    UpdateSliderCaption(index);
                }
            }
            finally { updating = false; }
        }

        public void SetCalibration(bool value)
        {
            bool previous = updating;
            updating = true;
            calibration.Checked = value;
            updating = previous;
        }

        private void RaiseChanged()
        {
            Action callback = Changed;
            if (callback != null) callback();
        }

        private void ApplySelectorMetrics()
        {
            // The Framework runtime may keep this dialog at its design bounds
            // even on a high-DPI monitor. Follow the actual content layout, not
            // the monitor DPI, so selectors cannot grow over the next label.
            float scale = content.Width / 376f;
            foreach (DarkComboBox combo in new[] { display, preset, aspect }) combo.ApplyScale(scale, 30);
            playerCount.ApplyScale(scale, 30);
            playerCount.Top = aspect.Top + Math.Max(0, (aspect.Height - playerCount.Height) / 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplySelectorMetrics();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == NativeWindows.WM_DPICHANGED && IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)delegate { if (!IsDisposed) ApplySelectorMetrics(); });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }

        private sealed class Choice
        {
            public readonly string Value;
            private readonly string label;
            public Choice(string value, string text) { Value = value; label = text; }
            public override string ToString() { return label; }
        }
    }
}
