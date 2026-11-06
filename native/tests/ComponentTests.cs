using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GgdHelp
{
    internal static class ComponentTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFonts(Control control, Graphics graphics)
        {
            // GDI+ throws if a Font setter retained an equal, subsequently
            // disposed instance. This is the small-scale resize regression.
            Check(control.Font.GetHeight(graphics) > 0, "Valid font for " + control.Name);
            foreach (Control child in control.Controls) CheckFonts(child, graphics);
        }

        private static Control Find(Control parent, string name)
        {
            Control[] matches = parent.Controls.Find(name, true);
            Check(matches.Length == 1, "Exactly one control named " + name);
            return matches[0];
        }

        private static void CheckInside(Control control, Control parent)
        {
            Check(control.Left >= 0 && control.Top >= 0 && control.Right <= parent.ClientSize.Width
                && control.Bottom <= parent.ClientSize.Height, "Control stays inside its parent: " + control.Name);
        }

        private static void CheckSelectors(PlayerCard card, double scale)
        {
            Control previous = null;
            Control notes = Find(card, "player-1-notes-1");
            foreach (string suffix in new[] { "camp", "role" }) {
                Control select = Find(card, "player-1-" + suffix);
                Check(select is DarkComboBox, "Role selector keeps the themed native ComboBox");
                CheckInside(select, card);
                Check(select.Height >= select.Font.Height + 2, "Role selector does not clip its font at " + scale);
                string caption = suffix == "camp" ? "阵营待定" : "正义使者";
                Size glyph;
                using (Graphics measure = select.CreateGraphics()) glyph = TextRenderer.MeasureText(measure, caption, select.Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                double actualScale = Math.Max(.55, scale);
                int textWidth = select.Width - (int)Math.Round(18 * actualScale) - Math.Max(2, (int)Math.Round(4 * actualScale)) - 2;
                Check(textWidth >= glyph.Width, "Selector caption fits before its arrow at " + scale + ": " + suffix);
                CheckArrowPalette(select, new Rectangle(Math.Max(1, select.Width - Math.Max(14, (int)Math.Round(20 * scale))),
                    2, Math.Min(select.Width - 2, Math.Max(12, (int)Math.Round(20 * scale)) - 2), Math.Max(1, select.Height - 4)));
                Check(select.Bottom <= notes.Top, "Role selector stays above notes at " + scale);
                if (previous != null) {
                    Check(previous.Right < select.Left, "Role selectors do not overlap at " + scale);
                    Check(previous.Top == select.Top && previous.Height == select.Height, "Role selectors share a baseline at " + scale);
                }
                previous = select;
            }
            Control round = Find(card, "player-1-death-round"), revive = Find(card, "player-1-revive");
            Control status = Find(card, "player-1-death-status");
            Check(round is DarkNumericUpDown, "Death round keeps the themed native NumericUpDown");
            CheckInside(round, card);
            Check(round.Height >= round.Font.Height + 2, "Round editor does not clip its font at " + scale);
            Check(status.Right <= round.Left && round.Right < revive.Left, "Round editor fits between status and revival at " + scale);
            Check(Math.Abs((round.Top + round.Height / 2.0) - (revive.Top + revive.Height / 2.0)) <= 1.5,
                "Round editor and revival are vertically centered at " + scale);
            Control arrow = null;
            foreach (Control child in round.Controls) {
                CheckInside(child, round);
                if (!(child is TextBox)) arrow = child;
                else {
                    int digits = TextRenderer.MeasureText("99", child.Font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    Check(child.Width >= digits, "Two-digit rounds fit the editable text area at " + scale
                        + ": edit " + child.Width + "px, digits " + digits + "px, font " + child.Font.Size);
                }
            }
            Check(arrow != null && arrow.Width >= 10 && arrow.Height >= 12, "Both numeric arrows have a usable area at " + scale
                + ": " + (arrow == null ? "missing" : arrow.Size.ToString()) + ", round " + round.Size);
            CheckArrowPalette(round, Rectangle.Inflate(arrow.Bounds, -1, -1));
        }

        private static void CheckLinkedRoles(PlayerCard card, Player player, Preferences preferences)
        {
            DarkComboBox camp = (DarkComboBox)Find(card, "player-1-camp");
            DarkComboBox role = (DarkComboBox)Find(card, "player-1-role");
            int dropdowns = 0;
            foreach (Control control in card.Controls) if (control is ComboBox) dropdowns++;
            Check(dropdowns == 2, "Identity has exactly one camp and one specific-role selector");
            Check(camp.Items.Count == 4 && (string)camp.Items[0] == "阵营待定", "Camp list has a placeholder and three camps");
            int changes = 0;
            Action changed = delegate { changes++; };
            card.Changed += changed;
            try {
                Model.SelectCamp(player, "");
                card.Configure(player, preferences, false, 1);
                Check(camp.SelectedIndex == 0 && !role.Enabled && role.Items.Count == 1,
                    "Unknown camp disables specific-role selection without leaking another camp's choices");
                for (int group = 0; group < Model.RoleKeys.Length; group++) {
                    int before = changes;
                    camp.SelectedIndex = group + 1;
                    Check(changes == before + 1, "Selecting a camp saves once");
                    Check(Model.GetCamp(player) == Model.RoleKeys[group] && role.Enabled, "Camp choice enables the filtered role list");
                    Check(role.Items.Count == Model.RoleGroups[group].Length + 1, "Role list contains exactly its camp's catalogue");
                    for (int index = 0; index < Model.RoleGroups[group].Length; index++)
                        Check((string)role.Items[index + 1] == Model.RoleGroups[group][index], "Role list follows the selected camp");
                    string selected = Model.RoleGroups[group][Model.RoleGroups[group].Length - 1];
                    before = changes;
                    role.SelectedItem = selected;
                    Check(changes == before + 1 && Model.GetRole(player) == selected, "Choosing a specific role saves once");
                    string previous = selected;
                    camp.SelectedIndex = 0;
                    Check(!role.Enabled && Model.GetRole(player) == "", "Clearing camp shows the undetermined state");
                    camp.SelectedIndex = group + 1;
                    Check((string)role.SelectedItem == previous, "Switching camps preserves each previous guess");
                }
                camp.SelectedIndex = 1;
                role.SelectedIndex = 0;
                Check(Model.GetRole(player) == "" && role.SelectedIndex == 0, "Specific role can be cleared independently of camp");
                Model.SelectRole(player, "旧版自定义身份");
                card.Configure(player, preferences, false, 1);
                Check((string)role.SelectedItem == "旧版自定义身份", "Unknown saved role stays selectable in its original camp");
                camp.SelectedIndex = 2;
                Check(!role.Items.Contains("旧版自定义身份"), "Unknown saved role is not copied into another camp");
                camp.SelectedIndex = 1;
                Check((string)role.SelectedItem == "旧版自定义身份", "Returning restores the unknown role");
                int unchanged = changes;
                card.Configure(player, preferences, false, .667);
                Check(changes == unchanged, "Layout refresh never overwrites or resaves identity");
                card.SetEditing(false);
                string originalCamp = Model.GetCamp(player), originalRole = Model.GetRole(player);
                camp.SelectedIndex = 3;
                role.SelectedIndex = 0;
                Check(changes == unchanged && Model.GetCamp(player) == originalCamp && Model.GetRole(player) == originalRole,
                    "Locked selectors cannot change the model even through their event handlers");
                card.SetEditing(true);
                Player replacement = new Player { id = player.id, camp = "good", roles = new Roles { good = "警长" } };
                card.Configure(replacement, preferences, false, 1);
                Check((string)role.SelectedItem == "警长" && !role.Items.Contains("旧版自定义身份"),
                    "Configuring a different player in the same camp discards the old player's custom option");
                Model.SelectCamp(player, "good");
                Model.SelectRole(player, "警长");
                card.Configure(player, preferences, false, 1);
                Check(camp.SelectedIndex == 1 && (string)role.SelectedItem == "警长", "Normal selection restores after editing is enabled");
            } finally { card.Changed -= changed; }
        }

        private static void CheckArrowPalette(Control selector, Rectangle arrow)
        {
            EnsureHandles(selector);
            using (Bitmap image = new Bitmap(selector.Width, selector.Height)) {
                selector.DrawToBitmap(image, new Rectangle(Point.Empty, selector.Size));
                Rectangle sample = Rectangle.Intersect(new Rectangle(Point.Empty, selector.Size), arrow);
                int bright = 0, mint = 0;
                for (int y = sample.Top; y < sample.Bottom; y++) for (int x = sample.Left; x < sample.Right; x++) {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.R > 200 && pixel.G > 200 && pixel.B > 200) bright++;
                    if (pixel.R < 190 && pixel.G > 170 && pixel.B > 140) mint++;
                }
                Check(sample.Width > 0 && sample.Height > 0 && bright <= sample.Width * sample.Height / 4,
                    "System white arrow background must not reappear: " + selector.Name);
                Check(mint > 0, "The arrow itself is painted, not an empty dark rectangle: " + selector.Name);
            }
        }

        private static void CheckPaintMessages(Control control, bool textExpected)
        {
            EnsureHandles(control);
            Color untouched = Color.Magenta;
            foreach (bool enabled in new[] { true, false }) {
                control.Enabled = enabled;
                using (Bitmap image = new Bitmap(control.Width, control.Height))
                using (Graphics graphics = Graphics.FromImage(image)) {
                    graphics.Clear(untouched);
                    IntPtr dc = graphics.GetHdc();
                    try {
                        Check(SendMessage(control.Handle, 0x0014, dc, IntPtr.Zero) == new IntPtr(1),
                            "Erasing a themed surface defers painting until its complete frame: " + control.Name);
                    } finally { graphics.ReleaseHdc(dc); }
                    Check(image.GetPixel(control.Width / 2, control.Height / 2).ToArgb() == untouched.ToArgb(),
                        "Erase must not publish an intermediate background: " + control.Name);

                    // A hidden HWND must honor PRF_CHECKVISIBLE rather than
                    // painting into an export/screenshot that explicitly excludes it.
                    dc = graphics.GetHdc();
                    try { SendMessage(control.Handle, 0x0317, dc, new IntPtr(1 | 4 | 8)); }
                    finally { graphics.ReleaseHdc(dc); }
                    Check(image.GetPixel(control.Width / 2, control.Height / 2).ToArgb() == untouched.ToArgb(),
                        "Printing respects hidden controls: " + control.Name);

                    foreach (int message in new[] { 0x0317, 0x0318 }) {
                        graphics.Clear(untouched);
                        dc = graphics.GetHdc();
                        try { SendMessage(control.Handle, message, dc, new IntPtr(4 | 8)); }
                        finally { graphics.ReleaseHdc(dc); }
                        int unpainted = 0, glyphs = 0;
                        int arrowWidth = Math.Max(8, (int)Math.Round(18 * control.Width / 190.0));
                        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) {
                            Color pixel = image.GetPixel(x, y);
                            if (pixel.ToArgb() == untouched.ToArgb()) unpainted++;
                            if (x > 3 && x < control.Width - arrowWidth - 3 && y > 2 && y < control.Height - 3
                                && pixel.G > 70 && pixel.B > 60) glyphs++;
                        }
                        Check(unpainted == 0, "Print paints the entire surface without stale pixels: " + control.Name);
                        Check(image.GetPixel(1, 1).R < 190, "Print keeps the dark surface when enabled or disabled: " + control.Name);
                        if (textExpected) {
                            if (glyphs <= 3) image.Save(Path.Combine(Path.GetTempPath(), "ggd-paint-glyph-failure.png"), ImageFormat.Png);
                            Check(glyphs > 3, "Print retains readable identity glyphs when enabled or disabled: " + control.Name
                                + " enabled=" + enabled + " message=" + message + " size=" + control.Size + " glyphs=" + glyphs);
                        }
                    }
                }
                // Validate native invalidation is consumed inside WM_PAINT.
                // Keeping a dirty rectangle here causes a continuous paint loop.
                for (int repeat = 0; repeat < 12; repeat++) {
                    InvalidateRect(control.Handle, IntPtr.Zero, true);
                    SendMessage(control.Handle, 0x000F, IntPtr.Zero, IntPtr.Zero);
                }
                Check(!GetUpdateRect(control.Handle, IntPtr.Zero, false), "Native paint clears its update region: " + control.Name);
            }
            control.Enabled = true;
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
        [DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr window, IntPtr rect, bool erase);

        private static void CheckNumericPadding(DarkNumericUpDown control)
        {
            // UpDownBase marks its surface Opaque, so WinForms skips
            // OnPaintBackground. Seed the reusable buffer with another frame's
            // pixels and verify OnPaint initializes all gaps around its children.
            foreach (bool enabled in new[] { true, false }) {
                control.Enabled = enabled;
                using (Bitmap image = new Bitmap(control.Width, control.Height))
                using (Graphics graphics = Graphics.FromImage(image)) {
                    graphics.Clear(Color.Magenta);
                    MethodInfo paint = typeof(DarkNumericUpDown).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    paint.Invoke(control, new object[] { new PaintEventArgs(graphics, control.ClientRectangle) });
                    int gaps = 0, stale = 0;
                    for (int y = 1; y < image.Height - 1; y++) for (int x = 1; x < image.Width - 1; x++) {
                        bool insideChild = false;
                        foreach (Control child in control.Controls)
                            if (child.Bounds.Contains(x, y)) insideChild = true;
                        if (insideChild) continue;
                        gaps++;
                        if (image.GetPixel(x, y).ToArgb() != control.BackColor.ToArgb()) stale++;
                    }
                    Check(gaps > 0 && stale == 0, "Numeric padding must not reuse stale buffer pixels: " + control.Name
                        + " enabled=" + enabled + " size=" + control.Size + " stale=" + stale + "/" + gaps);
                }
            }
            control.Enabled = true;
        }

        private static void CheckLegacyIdentity()
        {
            foreach (string oldRole in new[] { "警长", "旧版未分类身份" }) {
                Player legacy = new Player { id = 1, role = oldRole };
                using (PlayerCard card = new PlayerCard(legacy, delegate { return 1; })) {
                    ComboBox camp = (ComboBox)Find(card, "player-1-camp");
                    ComboBox role = (ComboBox)Find(card, "player-1-role");
                    Check((string)role.SelectedItem == oldRole, "Legacy singular role remains visible without erasing absent grouped data");
                    if (oldRole == "警长") {
                        Check(camp.SelectedIndex == 1 && role.Enabled, "Known legacy identity infers its camp");
                    } else {
                        Check(camp.SelectedIndex == 0 && !role.Enabled, "Unknown legacy identity stays visible but unclassified");
                        camp.SelectedIndex = 3;
                        Check(Model.GetCamp(legacy) == "bad" && (string)role.SelectedItem == oldRole && role.Enabled,
                            "User can classify an unknown legacy identity without losing its text");
                    }
                }
            }
        }

        private static void SaveSelectorPreview(PlayerCard card, Player player, Preferences preferences, string directory)
        {
            Directory.CreateDirectory(directory);
            double[] scales = { 2.0 / 3.0, 1.0, 4.0 / 3.0, 2.0 };
            using (Bitmap sheet = new Bitmap(760, 1100))
            using (Graphics graphics = Graphics.FromImage(sheet))
            using (Font caption = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel)) {
                graphics.Clear(Color.FromArgb(42, 54, 64));
                int y = 16;
                foreach (double scale in scales) {
                    card.Configure(player, preferences, false, scale);
                    TextRenderer.DrawText(graphics, "Control scale " + scale.ToString("0.###"), caption,
                        new Point(16, y), Color.White);
                    y += 24;
                    using (Bitmap frame = new Bitmap(card.Width, card.Height))
                    using (Graphics frameGraphics = Graphics.FromImage(frame)) {
                        card.DrawToBitmap(frame, new Rectangle(Point.Empty, card.Size));
                        // Native child HWNDs may be omitted when their Form is
                        // hidden. Print each one explicitly for this contact sheet.
                        foreach (Control child in card.Controls) {
                            if (child.Name.EndsWith("-drag", StringComparison.Ordinal)) continue;
                            EnsureHandles(child);
                            using (Bitmap controlImage = new Bitmap(child.Width, child.Height)) {
                                child.DrawToBitmap(controlImage, new Rectangle(Point.Empty, child.Size));
                                frameGraphics.DrawImageUnscaled(controlImage, child.Location);
                            }
                        }
                        graphics.DrawImageUnscaled(frame, 16, y);
                    }
                    y += card.Height + 24;
                }
                sheet.Save(Path.Combine(directory, "native-selector-components.png"), ImageFormat.Png);
            }
        }

        private static void EnsureHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) EnsureHandles(child);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                NativeWindows.InitializeDpi();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CheckLegacyIdentity();
                AppState state = Model.CreateState();
                Player player = state.session.players[0];
                player.notes = "中文备注";
                player.notes2 = "第二备注";
                player.roles.good = "警长";
                Model.SelectCamp(player, "good");
                player.death = new Death { round = 2, cause = "刀杀" };
                using (Form host = new Form())
                using (PlayerCard card = new PlayerCard(player, delegate { return state.session.round; }))
                using (Bitmap bitmap = new Bitmap(900, 900))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    host.Controls.Add(card);
                    CheckLinkedRoles(card, player, state.preferences);
                    // Changing scale below the minimum font size must reuse
                    // live fonts even when several adjacent scales look equal.
                    double[] scales = { .4, .45, .5, .6, .5, .45, .4, .4, .7, 1, 1.333, 2, 1, .4 };
                    foreach (double scale in scales)
                    {
                        card.Configure(player, state.preferences, false, scale);
                        CheckFonts(card, graphics);
                        card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, card.Size));
                        Check(!host.Visible, "Component test must never show its host");
                    }
                    if (args.Length > 0) SaveSelectorPreview(card, player, state.preferences, args[0]);
                    foreach (double scale in new[] { .4, .55, 2.0 / 3.0, 1.0, 1.25, 4.0 / 3.0, 1.5, 2.0 }) {
                        card.Configure(player, state.preferences, false, scale);
                        CheckSelectors(card, scale);
                        card.SetEditing(false);
                        Check(!Find(card, "player-1-camp").Enabled && !Find(card, "player-1-role").Enabled && !Find(card, "player-1-death-round").Enabled,
                            "Lock disables native selectors at " + scale);
                        card.SetEditing(true);
                    }
                    foreach (double scale in new[] { 2.0 / 3.0, 1.0, 2.0 }) {
                        card.Configure(player, state.preferences, false, scale);
                        CheckPaintMessages(Find(card, "player-1-role"), true);
                        DarkNumericUpDown numeric = (DarkNumericUpDown)Find(card, "player-1-death-round");
                        CheckNumericPadding(numeric);
                        foreach (Control child in numeric.Controls)
                            if (!(child is TextBox)) CheckPaintMessages(child, false);
                    }
                    card.Configure(player, state.preferences, false, .4);
                    Size minimumCard = card.Size;
                    card.Configure(player, state.preferences, false, .55);
                    Check(card.Size == minimumCard, "Very small requested scales retain the usable minimum card size");
                    Check(Find(card, "player-1-death-round").Font.Size >= 10,
                        "Small-screen round numbers retain a readable minimum font");
                    card.Configure(player, state.preferences, false, .4);
                    int originalHeight = card.Height;
                    card.Configure(player, state.preferences, true, .4);
                    Check(card.Height > originalHeight, "Calibration handle adds a separate row");
                    state.preferences.features = new Features { roles = false, notes = false, death = false };
                    card.Configure(player, state.preferences, false, 1);
                    Check(!card.HasVisibleContent, "All disabled features hide the record card");
                    card.Configure(player, state.preferences, true, 1);
                    Check(card.HasVisibleContent && card.Width == 38, "Calibration remains reachable with all features disabled");
                    Check(player.notes == "中文备注" && player.notes2 == "第二备注" && player.roles.good == "警长"
                        && player.death.round == 2, "Hiding features retains all player data");
                }

                AppState settingsState = Model.CreateState();
                using (SettingsForm settings = new SettingsForm(settingsState, Model.CreateLayout(), Screen.AllScreens, Screen.PrimaryScreen.DeviceName))
                {
                    foreach (string id in new[] { "display-select", "resolution-preset", "aspect-select" }) {
                        Control selector = Find(settings, id);
                        Check(selector is DarkComboBox, "Settings selector uses the same dark theme: " + id);
                        CheckInside(selector, selector.Parent);
                        CheckArrowPalette(selector, new Rectangle(Math.Max(1, selector.Width - 20), 2, 18, Math.Max(1, selector.Height - 4)));
                    }
                    Control count = Find(settings, "player-count"), aspect = Find(settings, "aspect-select");
                    Check(count is DarkNumericUpDown, "Settings player count uses the same dark theme");
                    CheckInside(count, count.Parent);
                    Check(aspect.Right < count.Left, "Settings aspect and player-count inputs do not overlap");
                    Check(!settings.Visible, "Settings component test never shows a window");
                }

                using (LauncherForm launcher = new LauncherForm())
                using (Bitmap bitmap = new Bitmap(100, 100))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    for (int repeat = 0; repeat < 4; repeat++)
                    {
                        launcher.MoveToScreen(Screen.PrimaryScreen, null);
                        CheckFonts(launcher, graphics);
                        Check(!launcher.Visible, "Launcher component test must not show a window");
                    }
                }
                Console.WriteLine("Native component checks passed: " + checks + " (no visible windows)");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
