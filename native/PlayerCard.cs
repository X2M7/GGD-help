using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GgdHelp
{
    internal static class CardPalette
    {
        // A dark color key avoids bright fringes on antialiased transparent labels.
        internal static readonly Color TransparentKey = Color.FromArgb(1, 2, 3);
        internal static readonly Color Ink = Color.FromArgb(237, 247, 247);
        internal static readonly Color Muted = Color.FromArgb(172, 189, 199);
        internal static readonly Color Mint = Color.FromArgb(142, 226, 195);
        internal static readonly Color Dark = Color.FromArgb(13, 25, 34);
        internal static readonly Color Input = Color.FromArgb(17, 32, 43);
        internal static readonly Color Border = Color.FromArgb(74, 99, 109);
        internal static readonly Color Dead = Color.FromArgb(241, 151, 143);

        internal static Button Button(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 80, 93);
            button.BackColor = Color.FromArgb(37, 55, 67);
            button.ForeColor = Ink;
            button.UseVisualStyleBackColor = false;
            button.Margin = Padding.Empty;
            button.Padding = Padding.Empty;
            button.Cursor = Cursors.Hand;
            return button;
        }
    }

    internal sealed class CardTextBox : TextBox
    {
        private readonly string cue;
        internal CardTextBox(string placeholder) { cue = placeholder; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string text);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, 0x1501, new IntPtr(1), cue);
        }
    }

    public sealed class PlayerCard : Panel
    {
        private Player player;
        private Preferences preferences;
        private readonly Func<int> currentRound;
        private readonly DarkComboBox camp;
        private readonly DarkComboBox role;
        private string displayedCamp;
        private Player displayedPlayer;
        private readonly TextBox[] notes = new TextBox[2];
        private readonly Button[] deaths = new Button[7];
        private readonly Label deathStatus;
        private readonly Label roundPrefix;
        private readonly Label roundSuffix;
        private readonly DarkNumericUpDown deathRound;
        private readonly Button revive;
        private readonly Button dragHandle;
        private readonly ToolTip tips;
        private Font normalFont;
        private Font smallFont;
        private Font numberFont;
        private double currentScale = -1;
        private bool updating;
        private bool editing = true;
        private bool calibrating;
        private bool dragging;
        private Point lastDragPoint;
        private int lastRound;
        private int? draftDeathRound;

        public event Action Changed;
        // Each value is a physical-screen pixel delta since the previous event.
        // The owner applies that delta to the card's calibrated screen position.
        public event Action<Point> DragRequested;
        public bool HasVisibleContent { get; private set; }

        public PlayerCard(Player value, Func<int> roundProvider)
        {
            if (value == null) throw new ArgumentNullException("value");
            player = value;
            currentRound = roundProvider ?? delegate { return 1; };
            Name = "player-" + value.id;
            AccessibleName = Name;
            BackColor = CardPalette.TransparentKey;
            ForeColor = CardPalette.Ink;
            Padding = Padding.Empty;
            Margin = Padding.Empty;
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            tips = new ToolTip();
            camp = NewRoleSelector("camp");
            camp.Items.Add("阵营待定");
            camp.Items.AddRange(Model.RoleLabels);
            camp.SelectedIndexChanged += delegate
            {
                if (updating || !editing || camp.SelectedIndex < 0) return;
                Model.SelectCamp(player, camp.SelectedIndex == 0 ? "" : Model.RoleKeys[camp.SelectedIndex - 1]);
                UpdateRoleSelectors();
                RaiseChanged();
            };
            role = NewRoleSelector("role");
            role.SelectedIndexChanged += delegate
            {
                if (updating || !editing || !role.Enabled || role.SelectedIndex < 0) return;
                Model.SelectRole(player, role.SelectedIndex == 0 ? "" : role.SelectedItem.ToString());
                UpdateRoleTips();
                RaiseChanged();
            };
            for (int index = 0; index < 2; index++)
            {
                int field = index;
                CardTextBox input = new CardTextBox("备注 " + (index + 1));
                input.AutoSize = false;
                input.BorderStyle = BorderStyle.FixedSingle;
                input.BackColor = CardPalette.Input;
                input.ForeColor = CardPalette.Ink;
                input.MaxLength = 240;
                input.ImeMode = ImeMode.NoControl;
                input.Name = Name + "-notes-" + (index + 1);
                input.AccessibleName = input.Name;
                tips.SetToolTip(input, "备注 " + (index + 1) + " · 自动保存");
                input.TextChanged += delegate
                {
                    if (updating || !editing) return;
                    if (field == 0) player.notes = input.Text;
                    else player.notes2 = input.Text;
                    RaiseChanged();
                };
                notes[index] = input;
                Controls.Add(input);
            }
            string[] deathLabels = { "刀", "票", "刺", "炸", "吞", "连", "？" };
            string[] deathIds = { "knife", "vote", "assassin", "bomb", "eaten", "linked", "unknown" };
            for (int index = 0; index < deathLabels.Length; index++)
            {
                string cause = Model.DeathCauses[index];
                Button button = CardPalette.Button(deathLabels[index]);
                button.Name = Name + "-death-" + deathIds[index];
                button.AccessibleName = button.Name;
                tips.SetToolTip(button, "标记死亡：" + cause);
                button.Click += delegate
                {
                    if (!editing) return;
                    player.death = new Death { round = (int)deathRound.Value, cause = cause };
                    UpdateDeath();
                    RaiseChanged();
                };
                deaths[index] = button;
                Controls.Add(button);
            }
            deathStatus = NewLabel("");
            deathStatus.Name = Name + "-death-status";
            deathStatus.AccessibleName = deathStatus.Name;
            deathStatus.AutoEllipsis = true;
            roundPrefix = NewLabel("第");
            roundSuffix = NewLabel("轮");
            deathRound = new DarkNumericUpDown();
            deathRound.Minimum = 1;
            deathRound.Maximum = 99;
            deathRound.BackColor = CardPalette.Input;
            deathRound.ForeColor = CardPalette.Ink;
            deathRound.TextAlign = HorizontalAlignment.Center;
            deathRound.Name = Name + "-death-round";
            deathRound.AccessibleName = deathRound.Name;
            deathRound.ValueChanged += delegate
            {
                if (updating || !editing) return;
                if (player.death == null) draftDeathRound = (int)deathRound.Value;
                else player.death.round = (int)deathRound.Value;
                UpdateDeath();
                RaiseChanged();
            };
            Controls.Add(deathRound);
            revive = CardPalette.Button("存活");
            revive.Name = Name + "-revive";
            revive.AccessibleName = revive.Name;
            tips.SetToolTip(revive, "清除死亡标记，保留身份和备注");
            revive.Click += delegate
            {
                if (!editing) return;
                player.death = null;
                draftDeathRound = null;
                UpdateDeath();
                RaiseChanged();
            };
            Controls.Add(revive);
            dragHandle = CardPalette.Button("⠿");
            dragHandle.ForeColor = CardPalette.Mint;
            dragHandle.Cursor = Cursors.SizeAll;
            dragHandle.Name = Name + "-drag";
            dragHandle.AccessibleName = dragHandle.Name;
            tips.SetToolTip(dragHandle, "按住拖动，对齐这位玩家的位置");
            dragHandle.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (!editing || !calibrating || e.Button != MouseButtons.Left) return;
                dragging = true;
                lastDragPoint = Cursor.Position;
                dragHandle.Capture = true;
            };
            dragHandle.MouseMove += delegate
            {
                if (!dragging) return;
                Point next = Cursor.Position;
                Point delta = new Point(next.X - lastDragPoint.X, next.Y - lastDragPoint.Y);
                lastDragPoint = next;
                if (delta.X == 0 && delta.Y == 0) return;
                Action<Point> callback = DragRequested;
                if (callback != null) callback(delta);
            };
            dragHandle.MouseUp += delegate { EndDrag(); };
            dragHandle.MouseCaptureChanged += delegate { if (!dragHandle.Capture) dragging = false; };
            Controls.Add(dragHandle);
            Configure(value, new Preferences(), false, 1);
        }

        private Label NewLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.BackColor = Color.Transparent;
            label.ForeColor = CardPalette.Muted;
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);
            return label;
        }

        private DarkComboBox NewRoleSelector(string suffix)
        {
            DarkComboBox select = new DarkComboBox();
            select.BackColor = CardPalette.Input;
            select.ForeColor = CardPalette.Ink;
            select.Name = select.AccessibleName = Name + "-" + suffix;
            Controls.Add(select);
            return select;
        }

        private void UpdateRoleSelectors()
        {
            bool previous = updating;
            updating = true;
            try {
                string selectedCamp = Model.GetCamp(player);
                int group = Array.IndexOf(Model.RoleKeys, selectedCamp);
                camp.SelectedIndex = group + 1;
                if (!Object.ReferenceEquals(displayedPlayer, player) || displayedCamp != selectedCamp || role.Items.Count == 0) {
                    role.BeginUpdate();
                    try {
                        role.Items.Clear();
                        role.Items.Add("身份待定");
                        if (group >= 0) role.Items.AddRange(Model.RoleGroups[group]);
                        displayedCamp = selectedCamp;
                        displayedPlayer = player;
                    } finally { role.EndUpdate(); }
                }
                string choice = Model.GetRole(player);
                // Retain imported guesses even when a role was renamed or is
                // absent from the current catalogue; never put it in other camps.
                if (choice.Length != 0 && !role.Items.Contains(choice)) role.Items.Add(choice);
                role.SelectedIndex = choice.Length == 0 ? 0 : role.Items.IndexOf(choice);
                role.Enabled = editing && group >= 0;
                camp.ForeColor = group == 0 ? CardPalette.Mint : group == 1 ? Color.FromArgb(230, 205, 136)
                    : group == 2 ? CardPalette.Dead : CardPalette.Muted;
                UpdateRoleTips();
            } finally { updating = previous; }
        }

        private void UpdateRoleTips()
        {
            int group = Array.IndexOf(Model.RoleKeys, Model.GetCamp(player));
            string choice = Model.GetRole(player);
            tips.SetToolTip(camp, "先选择阵营：好 / 中 / 坏");
            tips.SetToolTip(role, group < 0 ? (choice.Length == 0 ? "先选择左侧阵营，再选择具体身份" : "旧身份：" + choice + " · 先选择阵营")
                : Model.RoleLabels[group] + " · " + (choice.Length == 0 ? "身份待定" : choice));
        }

        private void RaiseChanged()
        {
            Action callback = Changed;
            if (callback != null) callback();
        }

        private void EndDrag()
        {
            bool wasDragging = dragging;
            dragging = false;
            if (dragHandle.Capture) dragHandle.Capture = false;
            if (wasDragging) RaiseChanged();
        }

        public void Configure(Player value, Preferences options, bool isCalibrating, double scale)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (options == null) throw new ArgumentNullException("options");
            if (options.features == null) options.features = new Features();
            if (player.id != value.id || lastRound != currentRound()) draftDeathRound = null;
            lastRound = currentRound();
            player = value;
            preferences = options;
            calibrating = isCalibrating;
            if (!calibrating && dragging) EndDrag();
            updating = true;
            SuspendLayout();
            try
            {
                ApplyScale(Math.Max(0.55, Math.Min(3.5, scale)));
                UpdateRoleSelectors();
                if (notes[0].Text != value.notes) notes[0].Text = value.notes ?? "";
                if (notes[1].Text != value.notes2) notes[1].Text = value.notes2 ?? "";
                UpdateDeath();
                ArrangeControls();
                SetEditing(editing);
            }
            finally { ResumeLayout(false); updating = false; }
            Invalidate();
        }

        private void ApplyScale(double scale)
        {
            if (Math.Abs(currentScale - scale) < 0.001) return;
            currentScale = scale;
            Font oldNormal = normalFont;
            Font oldSmall = smallFont;
            Font oldNumber = numberFont;
            float normalSize = (float)Math.Max(6.0, 11.0 * scale);
            float smallSize = (float)Math.Max(5.5, 10.0 * scale);
            float numberSize = (float)Math.Max(10.0, 13.0 * scale);
            if (normalFont == null || Math.Abs(normalFont.Size - normalSize) > 0.001f)
                normalFont = new Font("Microsoft YaHei UI", normalSize, FontStyle.Regular, GraphicsUnit.Pixel);
            if (smallFont == null || Math.Abs(smallFont.Size - smallSize) > 0.001f)
                smallFont = new Font("Microsoft YaHei UI", smallSize, FontStyle.Regular, GraphicsUnit.Pixel);
            if (numberFont == null || Math.Abs(numberFont.Size - numberSize) > 0.001f)
                numberFont = new Font("Microsoft YaHei UI", numberSize, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = normalFont;
            foreach (Control control in Controls) control.Font = normalFont;
            deathStatus.Font = smallFont;
            roundPrefix.Font = smallFont;
            roundSuffix.Font = smallFont;
            revive.Font = smallFont;
            deathRound.Font = numberFont;
            foreach (DarkComboBox select in new[] { camp, role })
            {
                select.ApplyScale(scale, 26);
                select.DropDownHeight = Px(240);
            }
            camp.DropDownWidth = Px(88);
            role.DropDownWidth = Px(210);
            deathRound.ApplyScale(scale, 26);
            if (oldNormal != null && !Object.ReferenceEquals(oldNormal, normalFont)) oldNormal.Dispose();
            if (oldSmall != null && !Object.ReferenceEquals(oldSmall, smallFont)) oldSmall.Dispose();
            if (oldNumber != null && !Object.ReferenceEquals(oldNumber, numberFont)) oldNumber.Dispose();
        }

        private int Px(double value) { return Math.Max(1, (int)Math.Round(value * currentScale)); }
        private void PlaceInRow(Control control, double x, int y, double width, int rowHeight)
        {
            control.SetBounds(Px(x), y, Px(width), rowHeight);
            // Native selectors may enforce a font-dependent minimum height.
            // Center their final bounds instead of assuming SetBounds won.
            control.Top = y + Math.Max(0, (rowHeight - control.Height) / 2);
        }

        private void ArrangeControls()
        {
            Features feature = preferences.features;
            bool any = feature.roles || feature.notes || feature.death;
            HasVisibleContent = any || calibrating;
            Visible = HasVisibleContent && player.id <= preferences.playerCount;
            int y = Px(7), gap = Px(5);
            int roleHeight = Px(26);
            roleHeight = Math.Max(roleHeight, Math.Max(camp.Height, role.Height));
            camp.Visible = role.Visible = feature.roles;
            if (feature.roles) {
                PlaceInRow(camp, 8, y, 88, roleHeight);
                PlaceInRow(role, 102, y, 190, roleHeight);
            }
            if (feature.roles) y += roleHeight + gap;
            int noteHeight = Math.Max(Px(26), normalFont.Height + Px(4));
            for (int index = 0; index < 2; index++)
            {
                notes[index].Visible = feature.notes;
                if (feature.notes) PlaceInRow(notes[index], 8 + index * 146, y, 138, noteHeight);
            }
            if (feature.notes) y += noteHeight + gap;
            foreach (Button button in deaths) button.Visible = feature.death;
            deathStatus.Visible = roundPrefix.Visible = roundSuffix.Visible = deathRound.Visible = revive.Visible = feature.death;
            if (feature.death)
            {
                int buttonHeight = Math.Max(Px(24), normalFont.Height + Px(4));
                for (int index = 0; index < deaths.Length; index++) PlaceInRow(deaths[index], 8 + index * (288.0 / 7), y, 260.0 / 7, buttonHeight);
                y += buttonHeight + gap;
                int recordHeight = Math.Max(Px(26), Math.Max(deathRound.Height, smallFont.Height + Px(4)));
                PlaceInRow(deathStatus, 8, y, 132, recordHeight);
                PlaceInRow(roundPrefix, 146, y, 12, recordHeight);
                PlaceInRow(deathRound, 162, y, 66, recordHeight);
                PlaceInRow(roundSuffix, 232, y, 12, recordHeight);
                PlaceInRow(revive, 248, y, 44, recordHeight);
                y += recordHeight + gap;
            }
            dragHandle.Visible = calibrating;
            if (calibrating)
            {
                int handleHeight = Math.Max(Px(23), normalFont.Height + Px(4));
                PlaceInRow(dragHandle, 8, any ? y : Px(7), 22, handleHeight);
                y = (any ? y : Px(7)) + handleHeight + gap;
            }
            Size = new Size(Px(any ? Model.CardWidth : 38), Math.Max(1, y + Px(2)));
        }

        private void UpdateDeath()
        {
            bool previous = updating;
            updating = true;
            try
            {
                Death record = player.death;
                int round = record == null ? (draftDeathRound ?? currentRound()) : record.round;
                deathRound.Value = Math.Max(1, Math.Min(99, round));
                deathStatus.Text = record == null ? "存活 · 点击方式标记" : "第 " + record.round + " 轮 · " + record.cause;
                deathStatus.ForeColor = record == null ? CardPalette.Mint : Color.FromArgb(255, 208, 196);
                tips.SetToolTip(deathStatus, deathStatus.Text);
                revive.Text = record == null ? "存活" : "复活";
                revive.Enabled = editing && record != null;
                for (int index = 0; index < deaths.Length; index++)
                {
                    bool chosen = record != null && record.cause == Model.DeathCauses[index];
                    deaths[index].BackColor = chosen ? Color.FromArgb(134, 80, 86) : Color.FromArgb(37, 55, 67);
                    deaths[index].FlatAppearance.BorderColor = chosen ? CardPalette.Dead : CardPalette.Border;
                }
            }
            finally { updating = previous; }
            Invalidate();
        }

        public void SetEditing(bool value)
        {
            editing = value;
            camp.Enabled = value;
            role.Enabled = value && Array.IndexOf(Model.RoleKeys, Model.GetCamp(player)) >= 0;
            foreach (TextBox input in notes) { input.ReadOnly = !value; input.TabStop = value; }
            foreach (Button button in deaths) button.Enabled = value;
            deathRound.Enabled = value;
            revive.Enabled = value && player.death != null;
            dragHandle.Enabled = value && calibrating;
            if (!value && dragging) EndDrag();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!HasVisibleContent || Width < 2 || Height < 2) return;
            Color border = calibrating ? CardPalette.Mint : player.death == null ? CardPalette.Border : CardPalette.Dead;
            using (Pen pen = new Pen(border))
            {
                if (calibrating) pen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                if (normalFont != null) normalFont.Dispose();
                if (smallFont != null) smallFont.Dispose();
                if (numberFont != null) numberFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
