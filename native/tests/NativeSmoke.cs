// Real Windows UI automation. All clicks are guarded against landing on another
// process. A controlled window behind the overlay receives pass-through clicks.
// Chinese Unicode input is exercised; this does not claim to test an actual IME.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

internal sealed class SmokeBackdrop : Form
{
    internal int ClickCount;
    internal int KeyCount;
    internal SmokeBackdrop()
    {
        Text = "GGD-help controlled smoke backdrop";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen.Bounds;
        BackColor = Color.FromArgb(42, 54, 64);
        ShowInTaskbar = false;
        // A normal game window stays below the overlay after activation. Making
        // the fixture topmost would cover the app as soon as a transparent
        // nickname click activates this controlled backdrop.
        TopMost = false;
        KeyPreview = true;
        MouseClick += delegate { Interlocked.Increment(ref ClickCount); };
        KeyPress += delegate { Interlocked.Increment(ref KeyCount); };
    }
}

internal static class NativeSmoke
{
    private static Process running;
    private static SmokeBackdrop backdrop;
    private static string executable, artifactDirectory, profile, copiedExecutable;
    private static IntPtr overlayWindow;
    private static Rectangle displayBounds;
    private static List<Rectangle> lastBlankAreas;
    private static int exitCode = 1;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: NativeSmoke.exe app.exe artifacts-directory"); return 2; }
        executable = Path.GetFullPath(args[0]);
        artifactDirectory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(artifactDirectory);
        try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); }
        catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        backdrop = new SmokeBackdrop();
        displayBounds = backdrop.Bounds;
        backdrop.Shown += delegate {
            Thread worker = new Thread(Run);
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
        };
        Application.Run(backdrop);
        return exitCode;
    }

    private static void Run()
    {
        try
        {
            string workspace = Path.Combine(Path.GetTempPath(), "ggd-help-native-qa-" + Guid.NewGuid().ToString("N"));
            string runDirectory = Path.Combine(workspace, "single-exe");
            profile = Path.Combine(workspace, "profile");
            Directory.CreateDirectory(runDirectory);
            Directory.CreateDirectory(profile);
            copiedExecutable = Path.Combine(runDirectory, Path.GetFileName(executable));
            File.Copy(executable, copiedExecutable);
            Check(Directory.GetFiles(runDirectory).Length == 1, "standalone folder contains only the exe");
            Console.WriteLine("Isolated profile: " + profile);
            StartApp();
            Step("repeated process starts preserve painting and topmost state");
            for (int launch = 0; launch < 2; launch++) { StopApp(); StartApp(); }
            VerifyRoleSelectors();
            VerifySelectorAppearance();
            VerifyLayerOrder();
            VerifyNumericSelectors();
            Step("toolbar, roles and Unicode notes");
            Click("round-next");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 2; }, "round advances to 2");
            Click("round-prev");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 1; }, "round returns to 1");
            Click("round-next");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 2; }, "round advances again before entering records");
            TypeInto("player-1-notes-1", "中文记录一 · native smoke");
            TypeInto("player-1-notes-2", "中文记录二 · 独立备注");
            string[] camps = { "好", "中", "坏" };
            string[] campKeys = { "good", "neutral", "bad" };
            string[] choices = { "清醒梦者", "布谷鸟", "剑客鸭" };
            Check(!Find("player-1-role", true).Current.IsEnabled, "role list waits for a camp choice");
            for (int group = 0; group < camps.Length; group++) {
                SelectComboWithMouse("player-1-camp", camps[group]);
                string campKey = campKeys[group];
                Wait(delegate { return (string)Player()["camp"] == campKey && Find("player-1-role", true).Current.IsEnabled; },
                    "camp selection enables its role list");
                Check(ComboContains("player-1-role", choices[group]), "selected camp includes its expected role");
                Check(!ComboContains("player-1-role", choices[(group + 1) % choices.Length]), "other camps' roles stay out of this list");
                SelectComboWithMouse("player-1-role", choices[group]);
                string choice = choices[group];
                Wait(delegate { return (string)Player()["role"] == choice && (string)Object(Player()["roles"])[campKey] == choice; },
                    "specific role is saved for its camp");
            }
            Wait(delegate {
                IDictionary<string, object> p = Player();
                IDictionary<string, object> roles = Object(p["roles"]);
                return (string)p["notes"] == "中文记录一 · native smoke"
                    && (string)p["notes2"] == "中文记录二 · 独立备注"
                    && (string)roles["good"] != "" && (string)roles["neutral"] != "" && (string)roles["bad"] != "";
            }, "both Unicode notes and all three roles are saved");
            SelectComboWithMouse("player-1-camp", "好");
            Wait(delegate { return (string)Player()["role"] == "清醒梦者"; }, "switching camp restores its previous guess");
            SelectComboWithMouse("player-1-role", "正义使者");
            Wait(delegate { return (string)Player()["role"] == "正义使者"; }, "four-character role is saved");
            string expectedRoles = Json.Serialize(Player()["roles"]);

            Step("death, round and revival");
            Click("player-1-death-knife");
            Wait(delegate { return Player()["death"] != null && Convert.ToInt32(Object(Player()["death"])["round"]) == 2
                && (string)Object(Player()["death"])["cause"] == "刀杀"; }, "death cause and current round are recorded");
            Click("player-1-revive");
            Wait(delegate { return Player()["death"] == null; }, "revival clears only the death record");
            Click("player-1-death-bomb");
            Wait(delegate { return Player()["death"] != null && (string)Object(Player()["death"])["cause"] == "炸死"; }, "second death is saved");

            Step("feature switches preserve notes and roles");
            Click("settings-toggle");
            foreach (string id in new[] { "display-select", "resolution-preset", "aspect-select", "player-count" })
                CheckDarkArrow(id);
            System.Windows.Rect aspectBounds = Find("aspect-select", true).Current.BoundingRectangle;
            System.Windows.Rect countBounds = Find("player-count", true).Current.BoundingRectangle;
            Check(Math.Abs(aspectBounds.Top + aspectBounds.Height / 2 - countBounds.Top - countBounds.Height / 2) <= 2,
                "settings dropdown and number selector share the same visual center line");
            Check(Find("resolution-preset", true).Current.BoundingRectangle.Bottom <=
                Find("预设只调整画面比例，不会修改系统分辨率。", true).Current.BoundingRectangle.Top + 2,
                "resolution selector does not cover its explanatory caption");
            Check(Find("display-select", true).Current.BoundingRectangle.Bottom <=
                FindTextPrefix("此显示器的位置校准会单独保存。").Current.BoundingRectangle.Top + 2,
                "display selector does not cover the monitor information");
            Capture("native-selectors-settings.png");
            foreach (string feature in new[] { "roles", "notes", "death" })
            {
                SetChecked("feature-" + feature, false);
                string control = feature == "roles" ? "player-1-role" : feature == "notes" ? "player-1-notes-1" : "player-1-death-knife";
                Wait(delegate { return !Visible(control); }, feature + " controls disappear");
                if (feature == "roles") Check(!Visible("player-1-camp"), "feature switch hides both linked selectors");
            }
            Check(!Visible("player-1") && !Visible("player-16"), "all feature groups off remove the empty cards");
            CheckToolbarPainted();
            SetChecked("feature-roles", true);
            SetChecked("feature-notes", true);
            SetChecked("feature-death", true);
            Click("settings-close");
            Wait(delegate { return Visible("player-1-notes-1"); }, "notes return after enabling their group");
            Check(Value("player-1-notes-1") == "中文记录一 · native smoke", "notes survive hiding");
            Check(Json.Serialize(Player()["roles"]) == expectedRoles, "role choices survive hiding");
            CheckCardTextPainted("feature groups restored", true);
            VerifyBackplateOpacity();

            Step("transparent nickname strip sends clicks to controlled window");
            AutomationElement card = Find("player-1", true);
            System.Windows.Rect rect = card.Current.BoundingRectangle;
            double scale = rect.Width / 300.0;
            Point namePoint = new Point((int)(rect.Left + rect.Width / 2), (int)(rect.Top - 14 * scale));
            int clicksBefore = backdrop.ClickCount;
            ClickPoint(namePoint, true);
            Wait(delegate { return backdrop.ClickCount == clicksBefore + 1; }, "nickname strip click reaches the backdrop");
            Check((ExtendedStyle(overlayWindow) & 8L) != 0, "overlay stays topmost after activating the game-like backdrop");

            Step("lock releases focus and launcher can restore editing");
            Click("player-1-notes-1");
            EnsureAppForeground();
            MonitorBlankAreas(delegate { Click("lock-button"); }, "locking");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) != 0; }, "locked window uses native pass-through style");
            Wait(delegate { return WindowProcess(GetForegroundWindow()) != running.Id; }, "locking releases keyboard focus");
            CheckCardTextPainted("locked", true);
            AutomationElement notes = Find("player-1-notes-1", true);
            System.Windows.Rect noteRect = notes.Current.BoundingRectangle;
            clicksBefore = backdrop.ClickCount;
            ClickPoint(Center(noteRect), true);
            Wait(delegate { return backdrop.ClickCount == clicksBefore + 1; }, "locked card sends clicks to backdrop");
            Check(WindowProcess(GetForegroundWindow()) == Process.GetCurrentProcess().Id, "controlled backdrop owns keyboard before typing");
            int keysBefore = backdrop.KeyCount;
            SendUnicode("k");
            Wait(delegate { return backdrop.KeyCount > keysBefore; }, "locked overlay does not swallow game-window keyboard input");
            MonitorBlankAreas(delegate { Click("launcher-lock"); }, "unlocking");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) == 0; }, "launcher restores editing");
            CheckCardTextPainted("unlocked", true);

            Step("hide and restore from separate launcher");
            MonitorBlankAreas(delegate { Click("hide-button"); }, "hiding");
            Wait(delegate { return !IsWindowVisible(overlayWindow); }, "overlay hides");
            MonitorBlankAreas(delegate { Click("launcher-toggle"); }, "restoring");
            Wait(delegate { return IsWindowVisible(overlayWindow) && Visible("round-next"); }, "launcher shows overlay again");
            CheckCardTextPainted("shown again", true);

            Step("global show and click-through shortcuts");
            Shortcut(Keys.G);
            Wait(delegate { return !IsWindowVisible(overlayWindow); }, "Ctrl+Shift+G hides overlay");
            Shortcut(Keys.G);
            Wait(delegate { return IsWindowVisible(overlayWindow); }, "Ctrl+Shift+G restores overlay");
            Shortcut(Keys.L);
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) != 0; }, "Ctrl+Shift+L enables pass-through");
            Shortcut(Keys.L);
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) == 0; }, "Ctrl+Shift+L restores editing");
            CheckCardTextPainted("shortcuts restored", true);

            VerifyLauncherMenuLifecycle();

            Step("launcher menu settings, physical card calibration and draggable launcher");
            ChooseLauncherMenuItem("位置 / 设置");
            Wait(delegate { return Visible("calibration-toggle"); }, "settings opens from a real launcher menu click");
            SetChecked("calibration-toggle", true);
            Click("settings-close");
            System.Windows.Rect beforeDrag = Find("player-1", true).Current.BoundingRectangle;
            Drag(Center(Find("player-1-drag", true).Current.BoundingRectangle), new Point(42, 22));
            Wait(delegate { return Math.Abs(Find("player-1", true).Current.BoundingRectangle.Left - beforeDrag.Left - 42) < 3
                && Math.Abs(Find("player-1", true).Current.BoundingRectangle.Top - beforeDrag.Top - 22) < 3; }, "card follows physical mouse drag");
            CheckCardTextPainted("card dragged", true);
            MonitorBlankAreas(delegate { Thread.Sleep(300); }, "after dragging");
            string expectedProfiles = null;
            Wait(delegate { expectedProfiles = Json.Serialize(State()["profiles"]); return expectedProfiles.Contains("positions"); }, "calibrated position is saved");
            ChooseLauncherMenuItem("位置 / 设置");
            SetChecked("calibration-toggle", false);
            Click("settings-close");
            AutomationElement launcherButton = Find("launcher-toggle", true);
            IntPtr launcherHandle = GetAncestor(new IntPtr(launcherButton.Current.NativeWindowHandle), 2);
            RECT launcherRect; Check(GetWindowRect(launcherHandle, out launcherRect), "launcher has screen bounds");
            double launcherScale = (launcherRect.Right - launcherRect.Left) / 280.0;
            Drag(new Point(launcherRect.Left + (int)(18 * launcherScale), launcherRect.Top + (int)(22 * launcherScale)), new Point(-60, 70));
            Wait(delegate { return Convert.ToInt32(State()["launcherX"]) == launcherRect.Left - 60
                && Convert.ToInt32(State()["launcherY"]) == launcherRect.Top + 70; }, "launcher drag persists physical coordinates");
            int expectedLauncherX = Convert.ToInt32(State()["launcherX"]), expectedLauncherY = Convert.ToInt32(State()["launcherY"]);
            SetCursorPos(displayBounds.Left + displayBounds.Width / 2, displayBounds.Bottom - 8);
            Thread.Sleep(350);
            CheckToolbarPainted();
            Capture("native-smoke.png");

            Step("restart persistence from the same standalone exe");
            Click("settings-toggle");
            SetChecked("feature-notes", false);
            Click("settings-close");
            Wait(delegate { return !(bool)Object(Object(State()["preferences"])["features"])["notes"]; }, "hidden feature preference is saved");
            StopApp();
            Check(Directory.GetFiles(Path.GetDirectoryName(copiedExecutable)).Length == 1, "run folder still requires only one exe");
            StartApp();
            Check(!Visible("player-1-notes-1"), "hidden notes stay hidden after restart");
            Check(Convert.ToInt32(Session()["round"]) == 2, "round survives restart");
            Check(Json.Serialize(Player()["roles"]) == expectedRoles, "roles survive restart");
            Check((string)Player()["camp"] == "good" && (string)Player()["role"] == "正义使者",
                "selected camp and specific role survive restart");
            Check((string)Object(Player()["death"])["cause"] == "炸死", "death record survives restart");
            Check(Json.Serialize(State()["profiles"]) == expectedProfiles, "calibrated layout survives restart");
            Check(Convert.ToInt32(State()["launcherX"]) == expectedLauncherX && Convert.ToInt32(State()["launcherY"]) == expectedLauncherY,
                "launcher position survives restart");
            Click("settings-toggle");
            Check(!Checked("feature-notes"), "settings checkbox remains off after restart");
            SetChecked("feature-notes", true);
            Click("settings-close");
            Wait(delegate { return Value("player-1-notes-1") == "中文记录一 · native smoke"
                && Value("player-1-notes-2") == "中文记录二 · 独立备注"; }, "both notes return intact after restart");
            SetCursorPos(displayBounds.Left + displayBounds.Width / 2, displayBounds.Bottom - 8);
            Thread.Sleep(350);
            CheckToolbarPainted();
            CheckCardTextPainted("saved session restored", true);
            Capture("native-smoke-restart.png");
            StopApp(true);
            Console.WriteLine("PASS: standalone native exe; visible card text, owned foreground z-order, translucent backplates, no sampled white flashes, dark native selectors, mouse dropdown choice, numeric arrows and limits, Unicode text, roles, death/revival, feature switches, click-through, focus release, hotkeys, launcher menu actions and exit, card/launcher dragging and restart persistence.");
            Console.WriteLine("Host: " + Environment.OSVersion + "; this result does not claim Windows 10 or a real game was tested.");
            exitCode = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { Capture("native-smoke-failure.png"); DumpElements(); } catch { }
        }
        finally
        {
            if (running != null)
            {
                try { if (!running.HasExited) running.Kill(); } catch { }
                running.Dispose();
            }
            Console.WriteLine("Retained isolated profile for inspection: " + profile);
            backdrop.BeginInvoke(new Action(delegate { backdrop.Close(); }));
        }
    }

    private static void StartApp()
    {
        // Let the fixture finish showing/activating before starting the overlay.
        // Otherwise its initial activation can race the app's no-activate show.
        // A background-launched driver may lack foreground permission. Give
        // real input only to our own fixture, then remove its temporary topmost
        // flag before launching the app so it cannot cover the overlay.
        backdrop.Invoke(new Action(delegate { backdrop.TopMost = true; backdrop.BringToFront(); }));
        ClickPoint(new Point(displayBounds.Left + displayBounds.Width / 2, displayBounds.Top + displayBounds.Height / 2), true);
        backdrop.Invoke(new Action(delegate { backdrop.TopMost = false; backdrop.Activate(); }));
        Wait(delegate { return WindowProcess(GetForegroundWindow()) == Process.GetCurrentProcess().Id; },
            "controlled backdrop is foreground before application startup");
        Thread.Sleep(200);
        ProcessStartInfo info = new ProcessStartInfo(copiedExecutable);
        info.WorkingDirectory = Path.GetDirectoryName(copiedExecutable);
        info.UseShellExecute = false;
        info.EnvironmentVariables["OVERLAY_TEST_USER_DATA"] = profile;
        running = Process.Start(info);
        Wait(delegate { return Find("round-next", false) != null && Find("launcher-toggle", false) != null; }, "both application windows start", 15000);
        AutomationElement next = Find("round-next", true);
        IntPtr handle = new IntPtr(next.Current.NativeWindowHandle);
        overlayWindow = GetAncestor(handle, 2);
        Check(overlayWindow != IntPtr.Zero, "toolbar belongs to an overlay HWND");
        Wait(delegate { return IsWindowVisible(overlayWindow) && (ExtendedStyle(overlayWindow) & 8L) != 0; },
            "visible overlay retains WS_EX_TOPMOST after initial handle creation");
        uint colorKey, alphaFlags; byte alpha;
        bool hasAlpha = GetLayeredWindowAttributes(overlayWindow, out colorKey, out alpha, out alphaFlags);
        Console.WriteLine("Overlay display: visible=" + IsWindowVisible(overlayWindow) + ", alpha=" + (hasAlpha ? alpha.ToString() : "unknown")
            + ", flags=" + alphaFlags + ", style=" + ExtendedStyle(overlayWindow).ToString("X"));
        Check(hasAlpha && (alphaFlags & 1) != 0 && ((alphaFlags & 2) == 0 || alpha == 255),
            "native text layer uses color-key transparency without global alpha attenuation");
        Thread.Sleep(250);
        Capture("native-smoke-start.png");
        CheckToolbarPainted();
        CheckCardTextPainted("startup", false);
        MonitorBlankAreas(delegate { Thread.Sleep(300); }, "startup settling");
    }

    private static void CheckToolbarPainted()
    {
        Wait(delegate {
            AutomationElement toolbar = Find("toolbar", true), brand = null;
            foreach (AutomationElement element in toolbar.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                if (element.Current.Name.StartsWith("GGD-help", StringComparison.Ordinal)) { brand = element; break; }
            return brand != null && HasPaintedColor(brand, Color.FromArgb(142, 226, 195), 8)
                && HasPaintedColor(Find("round-next", true), Color.FromArgb(37, 55, 67), 30);
        }, "toolbar brand text and round button are visibly painted");
    }

    private static bool HasPaintedColor(AutomationElement element, Color expected, int required)
    {
        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return false;
        using (Bitmap pixels = new Bitmap((int)bounds.Width, (int)bounds.Height))
        using (Graphics graphics = Graphics.FromImage(pixels))
        {
            graphics.CopyFromScreen(new Point((int)bounds.Left, (int)bounds.Top), Point.Empty, pixels.Size);
            int matching = 0;
            for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                    if (CloseColor(pixels.GetPixel(x, y), expected, 3) && ++matching >= required) return true;
        }
        return false;
    }

    private static void CheckCardTextPainted(string phase, bool includeNotes)
    {
        // UIA can report valid text and controls even when the composed window
        // is only an empty mask. Inspect the physical glyph pixels as well.
        foreach (int player in new[] { 1, 16 }) {
            foreach (string suffix in new[] { "camp", "role", "death-status" }) {
                string id = "player-" + player + "-" + suffix;
                Wait(delegate { return HasGlyphPixels(Find(id, true), suffix != "death-status"); },
                    phase + ": visible glyphs inside " + id);
            }
        }
        if (includeNotes) foreach (int note in new[] { 1, 2 }) {
            string id = "player-1-notes-" + note;
            Wait(delegate { return HasGlyphPixels(Find(id, true), false); }, phase + ": visible Unicode note " + note);
        }
    }

    private static bool HasGlyphPixels(AutomationElement element, bool selector)
    {
        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 10 || bounds.Height < 7) return false;
        int left = (int)Math.Ceiling(bounds.Left) + 3, top = (int)Math.Ceiling(bounds.Top) + 3;
        int right = (int)Math.Floor(bounds.Right) - 3, bottom = (int)Math.Floor(bounds.Bottom) - 3;
        if (selector) right -= (int)Math.Max(10, bounds.Height * .75);
        Rectangle area = Rectangle.FromLTRB(left, top, right, bottom);
        if (area.Width <= 0 || area.Height <= 0 || !displayBounds.Contains(area)) return false;
        using (Bitmap pixels = new Bitmap(area.Width, area.Height))
        using (Graphics graphics = Graphics.FromImage(pixels)) {
            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            int ink = 0;
            HashSet<int> columns = new HashSet<int>(), rows = new HashSet<int>();
            for (int y = 0; y < pixels.Height; y++) for (int x = 0; x < pixels.Width; x++) {
                Color color = pixels.GetPixel(x, y);
                // Disabled text remains gray. Borders and dark surfaces are
                // excluded by the inset and intensity threshold; requiring
                // several columns also prevents a blinking caret from passing.
                if (color.R > 105 && color.G > 105 && color.B > 105) {
                    ink++; columns.Add(x); rows.Add(y);
                }
            }
            return ink >= 4 && columns.Count >= 3 && rows.Count >= 2;
        }
    }

    private static void VerifyLayerOrder()
    {
        Step("owned foreground stays above the translucent backplate when it is raised");
        IntPtr background = new IntPtr(Find("card-background", true).Current.NativeWindowHandle);
        Check(background != IntPtr.Zero && WindowProcess(background) == running.Id, "backplate belongs to this test application");
        uint colorKey, flags; byte alpha;
        Check(GetLayeredWindowAttributes(background, out colorKey, out alpha, out flags)
            && flags == 2 && alpha > 0 && alpha < 255, "backplate uses alpha only, with no color-key raster surface");
        Check(GetWindow(overlayWindow, 4) == background, "foreground has the backplate as its native owner");
        Check(IsAbove(overlayWindow, background), "foreground starts above the backplate");
        // Simulate a compositor/display reordering event directly. Separate
        // unrelated topmost windows can otherwise cover the text after startup.
        Check(SetWindowPos(background, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010),
            "controlled backplate can be raised without activation");
        Wait(delegate { return IsAbove(overlayWindow, background); }, "ownership preserves foreground z-order after the backplate is raised");
        CheckCardTextPainted("backplate raised", false);
        CheckToolbarPainted();
    }

    private static void VerifyBackplateOpacity()
    {
        Step("foreground text remains readable at minimum and maximum backplate opacity");
        int original = Convert.ToInt32(Object(State()["preferences"])["opacity"]);
        foreach (int value in new[] { 15, 95, original }) {
            Click("settings-toggle");
            AutomationElement slider = Find("panel-opacity", true);
            // The MSAA RangeValue provider can change the native track position
            // without sending the WinForms notification that updates the model.
            // Focus our own slider and use the same keys a user would press.
            slider.SetFocus();
            EnsureAppForeground();
            Wait(delegate { return Find("panel-opacity", true).Current.HasKeyboardFocus; }, "opacity slider has keyboard focus");
            PressKey(value <= 55 ? Keys.Home : Keys.End);
            int steps = value <= 55 ? value - 15 : 95 - value;
            for (int step = 0; step < steps; step++) PressKey(value <= 55 ? Keys.Right : Keys.Left);
            int selected = value;
            Wait(delegate { return Convert.ToInt32(Object(State()["preferences"])["opacity"]) == selected; },
                "backplate opacity saved at " + value);
            // Focusing the slider may scroll this small settings window down.
            // Bring the existing Close button back into view before clicking.
            Find("settings-close", true).SetFocus();
            Click("settings-close");
            IntPtr background = new IntPtr(Find("card-background", true).Current.NativeWindowHandle);
            uint key, flags; byte alpha;
            Check(GetLayeredWindowAttributes(background, out key, out alpha, out flags) && flags == 2
                && Math.Abs(alpha - 255 * value / 100.0) <= 1, "compositor applies requested backplate alpha " + value);
            CheckCardTextPainted("backplate opacity " + value, true);
            CheckToolbarPainted();
        }
    }

    private static bool IsAbove(IntPtr foreground, IntPtr background)
    {
        IntPtr current = background;
        for (int limit = 0; limit < 1000; limit++) {
            current = GetWindow(current, 3); // GW_HWNDPREV follows the Z order.
            if (current == foreground) return true;
            if (current == IntPtr.Zero) return false;
        }
        return false;
    }

    private static void MonitorBlankAreas(Action transition, string phase)
    {
        // These narrow strips have no controls, text or cursor: white pixels
        // here are paint artifacts, not legitimate glyphs or popup scrollbars.
        if (IsWindowVisible(overlayWindow)) {
            List<Rectangle> areas = new List<Rectangle>();
            foreach (int player in new[] { 1, 16 }) {
                System.Windows.Rect card = Find("player-" + player, true).Current.BoundingRectangle;
                double scale = card.Width / 300.0;
                areas.Add(new Rectangle((int)card.Left + 2, (int)card.Top + 4, 2, Math.Max(1, (int)card.Height - 8)));
                areas.Add(new Rectangle((int)(card.Left + card.Width / 2), (int)(card.Top - 14 * scale), 3, 3));
            }
            foreach (Rectangle area in areas) Check(displayBounds.Contains(area), "artifact sample stays inside controlled display");
            lastBlankAreas = areas;
        }
        Check(lastBlankAreas != null, "blank sample bounds are available before hiding");
        List<Rectangle> sampleAreas = new List<Rectangle>(lastBlankAreas);
        Exception samplingError = null;
        int frames = 0, whitePixels = 0;
        using (ManualResetEvent stop = new ManualResetEvent(false))
        using (ManualResetEvent ready = new ManualResetEvent(false)) {
            Thread sampler = new Thread(delegate() {
                try {
                    while (!stop.WaitOne(0)) {
                        foreach (Rectangle area in sampleAreas)
                            using (Bitmap pixels = new Bitmap(area.Width, area.Height))
                            using (Graphics graphics = Graphics.FromImage(pixels)) {
                                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
                                for (int y = 0; y < pixels.Height; y++) for (int x = 0; x < pixels.Width; x++) {
                                    Color color = pixels.GetPixel(x, y);
                                    if (color.R > 130 && color.G > 130 && color.B > 130) whitePixels++;
                                }
                            }
                        frames++;
                        ready.Set();
                        stop.WaitOne(8);
                    }
                } catch (Exception error) { samplingError = error; ready.Set(); }
            });
            sampler.IsBackground = true;
            sampler.Start();
            try {
                Check(ready.WaitOne(3000), "artifact sampler starts before " + phase);
                transition();
                Thread.Sleep(80);
            } finally {
                stop.Set();
                Check(sampler.Join(5000), "artifact sampler finishes after " + phase);
            }
        }
        if (samplingError != null) throw new InvalidOperationException("Screen sampling failed during " + phase, samplingError);
        Check(frames >= 3 && whitePixels == 0, phase + ": no white flashes in empty card/nickname strips ("
            + frames + " frames, " + whitePixels + " bright pixels)");
    }

    private static Color Blend(Color foreground, Color background, double opacity)
    {
        return Color.FromArgb((int)Math.Round(foreground.R * opacity + background.R * (1 - opacity)),
            (int)Math.Round(foreground.G * opacity + background.G * (1 - opacity)),
            (int)Math.Round(foreground.B * opacity + background.B * (1 - opacity)));
    }

    private static bool CloseColor(Color first, Color second, int tolerance)
    {
        return Math.Abs(first.R - second.R) <= tolerance && Math.Abs(first.G - second.G) <= tolerance
            && Math.Abs(first.B - second.B) <= tolerance;
    }

    private static void VerifySelectorAppearance()
    {
        Step("opaque readable native controls above a live translucent backplate");
        foreach (string id in new[] { "round-input", "player-1-camp", "player-1-role", "player-1-death-round" })
            CheckDarkArrow(id);
        System.Windows.Rect bounds = Find("player-1-camp", true).Current.BoundingRectangle;
        // Near the lower left corner, away from text, arrows, borders and focus cues.
        Point sample = new Point((int)(bounds.Left + bounds.Width * .25), (int)(bounds.Bottom - Math.Max(3, bounds.Height * .18)));
        Color before = Pixel(sample);
        System.Windows.Rect card = Find("player-1", true).Current.BoundingRectangle;
        Point plateSample = new Point((int)card.Left + 3, (int)(card.Top + card.Height / 2));
        Color plateBefore = Pixel(plateSample);
        backdrop.Invoke(new Action(delegate { backdrop.BackColor = Color.FromArgb(230, 184, 140); backdrop.Refresh(); }));
        Thread.Sleep(350);
        Color after = Pixel(sample);
        Color plateAfter = Pixel(plateSample);
        Check(CloseColor(before, after, 2), "native selector contrast stays stable when the scene changes");
        Check(plateAfter.R - plateBefore.R >= 25 && plateAfter.G - plateBefore.G >= 20,
            "card backplate shows the live controlled backdrop through alpha (" + plateBefore + " -> " + plateAfter + ")");
        Capture("native-selectors-transparency.png");
        backdrop.Invoke(new Action(delegate { backdrop.BackColor = Color.FromArgb(42, 54, 64); backdrop.Refresh(); }));
        Thread.Sleep(350);
        CheckToolbarPainted();
    }

    private static void VerifyRoleSelectors()
    {
        Step("two linked role selectors fit their row");
        foreach (int player in new[] { 1, 16 }) {
            string prefix = "player-" + player;
            System.Windows.Rect card = Find(prefix, true).Current.BoundingRectangle;
            System.Windows.Rect camp = Find(prefix + "-camp", true).Current.BoundingRectangle;
            System.Windows.Rect role = Find(prefix + "-role", true).Current.BoundingRectangle;
            Check(!camp.IsEmpty && camp.Width > 0 && camp.Right + 1 <= role.Left && role.Right <= card.Right,
                "camp and role have separate non-overlapping dropdowns");
            Check(Math.Abs(camp.Top - role.Top) <= 1 && Math.Abs(camp.Height - role.Height) <= 1,
                "linked selectors share their baseline");
            Check(role.Width > camp.Width, "specific role has space for its full name");
            Check(!Find(prefix + "-role", true).Current.IsEnabled, "no specific role until a camp is selected");
        }
        Capture("native-selectors-linked-roles.png");
    }

    private static Color Pixel(Point point)
    {
        using (Bitmap image = new Bitmap(1, 1))
        using (Graphics graphics = Graphics.FromImage(image))
        { graphics.CopyFromScreen(point, Point.Empty, image.Size); return image.GetPixel(0, 0); }
    }

    private static void CheckDarkArrow(string id)
    {
        System.Windows.Rect bounds = Find(id, true).Current.BoundingRectangle;
        Rectangle area = new Rectangle((int)(bounds.Right - Math.Max(10, bounds.Height * .60)),
            (int)bounds.Top + 2, (int)Math.Max(7, bounds.Height * .60) - 2, Math.Max(1, (int)bounds.Height - 4));
        using (Bitmap pixels = new Bitmap(area.Width, area.Height))
        using (Graphics graphics = Graphics.FromImage(pixels))
        {
            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            int white = 0;
            for (int y = 0; y < pixels.Height; y++) for (int x = 0; x < pixels.Width; x++) {
                Color color = pixels.GetPixel(x, y);
                if (color.R > 210 && color.G > 210 && color.B > 210) white++;
            }
            Check(white < pixels.Width * pixels.Height / 5, id + " has no system-white arrow block");
        }
    }

    private static void NumericArrow(string id, bool up)
    {
        System.Windows.Rect bounds = Find(id, true).Current.BoundingRectangle;
        ClickPoint(new Point((int)(bounds.Right - Math.Max(4, bounds.Height * .25)),
            (int)(bounds.Top + bounds.Height * (up ? .25 : .75))), false);
        Thread.Sleep(SystemInformation.DoubleClickTime + 30);
    }

    private static void TypeNumber(string id, string value)
    {
        System.Windows.Rect bounds = Find(id, true).Current.BoundingRectangle;
        ClickPoint(new Point((int)(bounds.Left + bounds.Width * .3), (int)(bounds.Top + bounds.Height / 2)), false);
        Thread.Sleep(SystemInformation.DoubleClickTime + 30);
        EnsureAppForeground();
        KeyEvent(Keys.ControlKey, false); PressKey(Keys.A); KeyEvent(Keys.ControlKey, true);
        SendUnicode(value); PressKey(Keys.Tab);
    }

    private static void VerifyNumericSelectors()
    {
        Step("numeric arrows, direct entry and the 1/99 limits");
        NumericArrow("round-input", true);
        Wait(delegate { return Convert.ToInt32(Session()["round"]) == 2; }, "toolbar upper arrow increments");
        NumericArrow("round-input", false);
        Wait(delegate { return Convert.ToInt32(Session()["round"]) == 1; }, "toolbar lower arrow decrements");
        NumericArrow("round-input", false);
        Check(Convert.ToInt32(Session()["round"]) == 1, "toolbar lower bound is one");
        TypeNumber("round-input", "99");
        Wait(delegate { return Convert.ToInt32(Session()["round"]) == 99; }, "toolbar accepts direct numeric entry");
        NumericArrow("round-input", true);
        Check(Convert.ToInt32(Session()["round"]) == 99, "toolbar upper bound is 99");
        TypeNumber("round-input", "1");
        Wait(delegate { return Convert.ToInt32(Session()["round"]) == 1; }, "toolbar numeric entry restores round one");

        TypeNumber("player-1-death-round", "99");
        NumericArrow("player-1-death-round", true);
        Click("player-1-death-knife");
        Wait(delegate { return Player()["death"] != null && Convert.ToInt32(Object(Player()["death"])["round"]) == 99; },
            "card numeric entry and upper limit are used when recording death");
        NumericArrow("player-1-death-round", false);
        Wait(delegate { return Convert.ToInt32(Object(Player()["death"])["round"]) == 98; }, "card lower arrow decrements");
        TypeNumber("player-1-death-round", "1");
        Wait(delegate { return Convert.ToInt32(Object(Player()["death"])["round"]) == 1; }, "card accepts direct numeric entry");
        NumericArrow("player-1-death-round", false);
        Check(Convert.ToInt32(Object(Player()["death"])["round"]) == 1, "card lower bound is one");
        Click("player-1-revive");
        Wait(delegate { return Player()["death"] == null; }, "numeric smoke leaves player alive");
    }

    private static bool ComboContains(string id, string text)
    {
        IntPtr handle = new IntPtr(Find(id, true).Current.NativeWindowHandle);
        return SendMessageText(handle, 0x0158, new IntPtr(-1), text).ToInt64() >= 0;
    }

    private static void SelectComboWithMouse(string id, string expected)
    {
        Step("actual mouse choice " + id + " = " + expected);
        AutomationElement element = Find(id, true);
        IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);
        Check(handle != IntPtr.Zero, "role selector retains a native HWND");
        int index = (int)SendMessageText(handle, 0x0158, new IntPtr(-1), expected).ToInt64();
        Check(index >= 0, "requested option exists in the filtered list: " + expected);
        COMBOBOXINFO info = new COMBOBOXINFO { size = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
        Check(GetComboBoxInfo(handle, ref info), "native dropdown metadata is available");
        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        ClickPoint(new Point((int)(bounds.Right - bounds.Height * .3), (int)(bounds.Top + bounds.Height / 2)), false);
        Thread.Sleep(SystemInformation.DoubleClickTime + 30);
        Wait(delegate { return IsWindowVisible(info.list); }, "dropdown opens with the mouse arrow");
        SendMessageValue(info.list, 0x0197, new IntPtr(index), IntPtr.Zero);
        RECT listBounds, itemBounds = new RECT();
        Check(GetWindowRect(info.list, out listBounds), "dropdown has screen bounds");
        Check(SendMessage(info.list, 0x0198, new IntPtr(index), ref itemBounds).ToInt64() != -1, "dropdown item bounds are available");
        Capture("native-selectors-dropdown.png");
        ClickPoint(new Point(listBounds.Left + (itemBounds.Left + itemBounds.Right) / 2,
            listBounds.Top + (itemBounds.Top + itemBounds.Bottom) / 2), false);
        Thread.Sleep(SystemInformation.DoubleClickTime + 30);
        Wait(delegate { return SendMessageValue(handle, 0x0147, IntPtr.Zero, IntPtr.Zero).ToInt64() == index
            && !IsWindowVisible(info.list); }, "mouse selects the option and closes the list");
        CheckDarkArrow(id);
    }

    private static AutomationElement FindMenuItem(string caption)
    {
        if (running == null || running.HasExited) return null;
        Condition match = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.NameProperty, caption));
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, running.Id)))
            foreach (AutomationElement item in window.FindAll(TreeScope.Element | TreeScope.Descendants, match))
                if (!item.Current.IsOffscreen && !item.Current.BoundingRectangle.IsEmpty) return item;
        return null;
    }

    private static void ChooseLauncherMenuItem(string caption)
    {
        // On adjacent monitors Windows may place a right-edge popup on the
        // next screen. Keep this fixture and all physical input on its backdrop.
        AutomationElement button = Find("launcher-menu", true);
        IntPtr launcherHandle = GetAncestor(new IntPtr(button.Current.NativeWindowHandle), 2);
        RECT launcherRect;
        Check(GetWindowRect(launcherHandle, out launcherRect), "launcher bounds before popup");
        int reserve = Math.Min(500, displayBounds.Width / 2);
        int moveLeft = Math.Max(0, launcherRect.Right - (displayBounds.Right - reserve));
        if (moveLeft > 0) {
            double scale = (launcherRect.Right - launcherRect.Left) / 280.0;
            Drag(new Point(launcherRect.Left + (int)(18 * scale), launcherRect.Top + (int)(22 * scale)),
                new Point(-moveLeft, 0));
        }
        Click("launcher-menu");
        AutomationElement item = null;
        Wait(delegate { item = FindMenuItem(caption); return item != null && item.Current.IsEnabled; },
            "launcher popup item is visible: " + caption);
        ClickElement(item);
        ThrowIfApplicationError();
    }

    private static void VerifyLauncherMenuLifecycle()
    {
        Step("repeated real launcher popup actions close without disposed-menu exceptions");
        for (int repeat = 0; repeat < 2; repeat++)
        {
            ChooseLauncherMenuItem("收起蒙版  Ctrl+Shift+G");
            Wait(delegate { return !IsWindowVisible(overlayWindow); }, "popup hides the overlay");
            ChooseLauncherMenuItem("显示蒙版  Ctrl+Shift+G");
            Wait(delegate { return IsWindowVisible(overlayWindow) && Visible("round-next"); }, "popup shows the overlay");
            ChooseLauncherMenuItem("穿透游戏  Ctrl+Shift+L");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) != 0; }, "popup enables click-through");
            ChooseLauncherMenuItem("允许编辑  Ctrl+Shift+L");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) == 0; }, "popup restores editing");
        }
        ChooseLauncherMenuItem("找回开关并恢复编辑");
        Wait(delegate { return IsWindowVisible(overlayWindow) && (ExtendedStyle(overlayWindow) & 0x20L) == 0
            && Visible("launcher-toggle"); }, "popup recovery keeps both windows usable");
    }

    private static void StopApp(bool forceMenu = false)
    {
        if (!forceMenu && Visible("quit-button"))
        {
            Click("quit-button");
            Wait(delegate { return running.HasExited; }, "application quits through the toolbar", 8000);
            Check(running.ExitCode == 0, "application exits cleanly");
            running.Dispose();
            running = null;
            return;
        }
        ChooseLauncherMenuItem("退出 GGD-help");
        Wait(delegate { return running.HasExited; }, "application quits through its popup menu", 8000);
        Check(running.ExitCode == 0, "application exits cleanly");
        running.Dispose();
        running = null;
    }

    private static AutomationElement Find(string id, bool required)
    {
        if (running == null || running.HasExited) { if (required) throw new Exception("App is not running: " + id); return null; }
        Condition identity = new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, id),
            new PropertyCondition(AutomationElement.NameProperty, id));
        AutomationElementCollection windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, running.Id));
        foreach (AutomationElement window in windows)
        {
            AutomationElement result = window.FindFirst(TreeScope.Element | TreeScope.Descendants, identity);
            if (result != null) return result;
        }
        if (required) throw new Exception("Missing automation control: " + id);
        return null;
    }

    private static bool Visible(string id)
    {
        AutomationElement element = Find(id, false);
        return element != null && !element.Current.IsOffscreen && !element.Current.BoundingRectangle.IsEmpty;
    }

    private static AutomationElement FindTextPrefix(string prefix)
    {
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, running.Id)))
            foreach (AutomationElement element in window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                if (element.Current.Name.StartsWith(prefix, StringComparison.Ordinal)) return element;
        throw new Exception("Missing caption: " + prefix);
    }

    private static void Click(string id)
    {
        AutomationElement target = null;
        Wait(delegate { target = Find(id, false); return target != null && !target.Current.IsOffscreen && target.Current.IsEnabled; }, "click target ready: " + id);
        ClickElement(target);
    }

    private static void ClickElement(AutomationElement element)
    {
        object pattern;
        if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pattern))
        {
            try { ((ScrollItemPattern)pattern).ScrollIntoView(); } catch (InvalidOperationException) { }
        }
        ClickPoint(Center(element.Current.BoundingRectangle), false);
        // Avoid interpreting rapid repeated real clicks as a Windows double-click.
        Thread.Sleep(Math.Max(150, SystemInformation.DoubleClickTime + 30));
    }

    private static Point Center(System.Windows.Rect rect)
    {
        Check(!rect.IsEmpty && rect.Width > 0 && rect.Height > 0, "mouse target has physical bounds");
        return new Point((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
    }

    private static void ClickPoint(Point point, bool allowBackdrop)
    {
        int targetProcess = WindowProcess(WindowFromPoint(point));
        bool safe = running != null && targetProcess == running.Id;
        if (allowBackdrop) safe = safe || targetProcess == Process.GetCurrentProcess().Id;
        Check(safe, "refusing click outside app or controlled backdrop at " + point + ", process=" + targetProcess);
        Check(displayBounds.Contains(point), "click stays on the controlled display");
        if (!SetCursorPos(point.X, point.Y)) throw new Exception("SetCursorPos failed.");
        Thread.Sleep(60);
        targetProcess = WindowProcess(WindowFromPoint(point));
        Check((running != null && targetProcess == running.Id) || (allowBackdrop && targetProcess == Process.GetCurrentProcess().Id), "target remains safe before mouse-down");
        Send(new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0002 } } },
            new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0004 } } });
    }

    private static void TypeInto(string id, string value)
    {
        Click(id);
        EnsureAppForeground();
        KeyEvent(Keys.ControlKey, false); PressKey(Keys.A); KeyEvent(Keys.ControlKey, true);
        SendUnicode(value);
        PressKey(Keys.Tab);
        Wait(delegate { return Value(id) == value; }, "text input: " + id);
    }

    private static void Drag(Point origin, Point delta)
    {
        Check(running != null && WindowProcess(WindowFromPoint(origin)) == running.Id, "drag starts on the application");
        Point destination = new Point(origin.X + delta.X, origin.Y + delta.Y);
        Check(displayBounds.Contains(origin) && displayBounds.Contains(destination), "drag remains on the controlled display");
        SetCursorPos(origin.X, origin.Y);
        Thread.Sleep(100);
        Send(new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0002 } } });
        try
        {
            Thread.Sleep(100);
            for (int step = 1; step <= 10; step++)
            {
                SetCursorPos(origin.X + delta.X * step / 10, origin.Y + delta.Y * step / 10);
                Thread.Sleep(40);
            }
        }
        finally { Send(new INPUT { type = 0, data = new InputUnion { mouse = new MOUSEINPUT { flags = 0x0004 } } }); }
        Thread.Sleep(SystemInformation.DoubleClickTime + 50);
    }

    private static void Shortcut(Keys key)
    {
        int foreground = WindowProcess(GetForegroundWindow());
        Check((running != null && foreground == running.Id) || foreground == Process.GetCurrentProcess().Id, "shortcut targets the app or controlled backdrop");
        KeyEvent(Keys.ControlKey, false); KeyEvent(Keys.ShiftKey, false);
        try { PressKey(key); }
        finally { KeyEvent(Keys.ShiftKey, true); KeyEvent(Keys.ControlKey, true); }
        Thread.Sleep(300);
    }

    private static string Value(string id)
    {
        AutomationElement element = Find(id, true);
        object pattern;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) return ((ValuePattern)pattern).Current.Value;
        return element.Current.Name;
    }

    private static bool Checked(string id)
    {
        return ((TogglePattern)Find(id, true).GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On;
    }

    private static void SetChecked(string id, bool value)
    {
        if (Checked(id) != value) Click(id);
        Wait(delegate { return Checked(id) == value; }, "checkbox state: " + id);
    }

    private static void EnsureAppForeground()
    {
        Wait(delegate { return running != null && WindowProcess(GetForegroundWindow()) == running.Id; }, "app owns keyboard before injecting text");
    }

    private static IDictionary<string, object> Object(object value) { return (IDictionary<string, object>)value; }
    private static IDictionary<string, object> State() { return Object(Json.DeserializeObject(File.ReadAllText(Path.Combine(profile, "native-state.json"), Encoding.UTF8))); }
    private static IDictionary<string, object> Session() { return Object(State()["session"]); }
    private static IDictionary<string, object> Player() { return Object(((object[])Session()["players"])[0]); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Assertion failed: " + message); }
    private static void Step(string message) { Console.WriteLine("Checking " + message + "..."); }

    private static void ThrowIfApplicationError()
    {
        if (running == null || running.HasExited) return;
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, running.Id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))))
        {
            try
            {
                if (window.Current.IsOffscreen) continue;
                string title = window.Current.Name;
                if (title.IndexOf(".NET Framework", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("Application displayed a .NET Framework exception dialog: " + title);
                object pattern;
                bool modal = window.TryGetCurrentPattern(WindowPattern.Pattern, out pattern)
                    && ((WindowPattern)pattern).Current.IsModal;
                if (!modal && title != "GGD-help" && window.Current.ClassName != "#32770") continue;
                foreach (AutomationElement text in window.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                {
                    string message = text.Current.Name;
                    if (message.IndexOf("Unhandled exception", StringComparison.OrdinalIgnoreCase) >= 0
                        || message.Contains("未经处理的异常") || message.Contains("未处理的异常")
                        || message.Contains("ObjectDisposedException") || message.Contains("GGD-help 无法继续运行"))
                        throw new Exception("Application displayed an error dialog: " + message);
                }
            }
            catch (ElementNotAvailableException) { }
        }
    }

    private static void Wait(Func<bool> check, string message, int timeout = 6000)
    {
        Stopwatch timer = Stopwatch.StartNew();
        Exception last = null;
        while (timer.ElapsedMilliseconds < timeout)
        {
            try
            {
                ThrowIfApplicationError();
                if (check()) { ThrowIfApplicationError(); return; }
            }
            catch (IOException error) { last = error; }
            catch (ElementNotAvailableException error) { last = error; }
            catch (InvalidOperationException error) { last = error; }
            Thread.Sleep(60);
        }
        throw new Exception("Timed out: " + message, last);
    }

    private static void Capture(string name)
    {
        using (Bitmap image = new Bitmap(displayBounds.Width, displayBounds.Height))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(displayBounds.Location, Point.Empty, displayBounds.Size);
            image.Save(Path.Combine(artifactDirectory, name), ImageFormat.Png);
        }
    }

    private static void DumpElements()
    {
        if (running == null || running.HasExited) return;
        StringBuilder dump = new StringBuilder();
        AutomationElementCollection windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, running.Id));
        foreach (AutomationElement window in windows)
        {
            foreach (AutomationElement element in window.FindAll(TreeScope.Element | TreeScope.Descendants, Condition.TrueCondition))
            {
                try { dump.AppendLine(element.Current.ControlType.ProgrammaticName + " | " + element.Current.AutomationId + " | "
                    + element.Current.Name + " | " + element.Current.BoundingRectangle); } catch (ElementNotAvailableException) { }
            }
        }
        File.WriteAllText(Path.Combine(artifactDirectory, "native-smoke-uia.txt"), dump.ToString(), Encoding.UTF8);
    }

    private static void SendUnicode(string value)
    {
        foreach (char ch in value)
        {
            Send(new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { scan = ch, flags = 0x0004 } } },
                new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { scan = ch, flags = 0x0004 | 0x0002 } } });
        }
    }

    private static void KeyEvent(Keys key, bool up)
    { Send(new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { key = (ushort)key, flags = up ? 2u : 0u } } }); }
    private static void PressKey(Keys key) { KeyEvent(key, false); KeyEvent(key, true); Thread.Sleep(30); }
    private static void Send(params INPUT[] inputs)
    { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length) throw new Exception("SendInput failed: " + Marshal.GetLastWin32Error()); }
    private static int WindowProcess(IntPtr window) { uint id; GetWindowThreadProcessId(window, out id); return (int)id; }
    private static long ExtendedStyle(IntPtr window) { return GetWindowLongPtr(window, -20).ToInt64(); }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort key, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct COMBOBOXINFO { public int size; public RECT item, button; public uint state; public IntPtr combo, edit, list; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr window, ref COMBOBOXINFO info);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref RECT rectangle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageText(IntPtr window, int message, IntPtr parameter, string text);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageValue(IntPtr window, int message, IntPtr parameter, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
}
