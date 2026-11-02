using System;
using System.Drawing;
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

        [STAThread]
        private static int Main()
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
