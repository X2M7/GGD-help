using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
            foreach (string group in Model.RoleKeys) {
                Control select = Find(card, "player-1-role-" + group);
                Control camp = Find(card, "player-1-camp-" + group);
                CheckInside(camp, card);
                Size glyph;
                using (Graphics measure = camp.CreateGraphics()) glyph = TextRenderer.MeasureText(measure, camp.Text, camp.Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                Check(camp.Right < select.Left && camp.Width >= glyph.Width,
                    "Camp character has its own unclipped column before the selector at " + scale + ": " + group
                    + " label=" + camp.Bounds + " select=" + select.Bounds + " glyph=" + glyph + " font=" + camp.Font.Size);
                Check(camp.Top == select.Top && camp.Height == select.Height,
                    "Camp character stays centered alongside its selector at " + scale);
                Check(select is DarkComboBox, "Role selector keeps the themed native ComboBox");
                CheckInside(select, card);
                Check(select.Height >= select.Font.Height + 2, "Role selector does not clip its font at " + scale);
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
                AppState state = Model.CreateState();
                Player player = state.session.players[0];
                player.notes = "中文备注";
                player.notes2 = "第二备注";
                player.roles.good = "警长";
                player.death = new Death { round = 2, cause = "刀杀" };
                using (Form host = new Form())
                using (PlayerCard card = new PlayerCard(player, delegate { return state.session.round; }))
                using (Bitmap bitmap = new Bitmap(900, 900))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    host.Controls.Add(card);
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
                        Check(!Find(card, "player-1-role-good").Enabled && !Find(card, "player-1-death-round").Enabled,
                            "Lock disables native selectors at " + scale);
                        card.SetEditing(true);
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
