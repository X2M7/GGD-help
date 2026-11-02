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
            Step("toolbar, roles and Unicode notes");
            Click("round-next");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 2; }, "round advances to 2");
            Click("round-prev");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 1; }, "round returns to 1");
            Click("round-next");
            Wait(delegate { return Convert.ToInt32(Session()["round"]) == 2; }, "round advances again before entering records");
            TypeInto("player-1-notes-1", "中文记录一 · native smoke");
            TypeInto("player-1-notes-2", "中文记录二 · 独立备注");
            foreach (string group in new[] { "good", "neutral", "bad" })
            {
                Click("player-1-role-" + group);
                EnsureAppForeground();
                PressKey(Keys.Home); PressKey(Keys.Down); PressKey(Keys.Enter);
                Thread.Sleep(100);
            }
            Wait(delegate {
                IDictionary<string, object> p = Player();
                IDictionary<string, object> roles = Object(p["roles"]);
                return (string)p["notes"] == "中文记录一 · native smoke"
                    && (string)p["notes2"] == "中文记录二 · 独立备注"
                    && (string)roles["good"] != "" && (string)roles["neutral"] != "" && (string)roles["bad"] != "";
            }, "both Unicode notes and all three roles are saved");
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
            foreach (string feature in new[] { "roles", "notes", "death" })
            {
                SetChecked("feature-" + feature, false);
                string control = feature == "roles" ? "player-1-role-good" : feature == "notes" ? "player-1-notes-1" : "player-1-death-knife";
                Wait(delegate { return !Visible(control); }, feature + " controls disappear");
            }
            SetChecked("feature-roles", true);
            SetChecked("feature-notes", true);
            SetChecked("feature-death", true);
            Click("settings-close");
            Wait(delegate { return Visible("player-1-notes-1"); }, "notes return after enabling their group");
            Check(Value("player-1-notes-1") == "中文记录一 · native smoke", "notes survive hiding");
            Check(Json.Serialize(Player()["roles"]) == expectedRoles, "role choices survive hiding");

            Step("transparent nickname strip sends clicks to controlled window");
            AutomationElement card = Find("player-1", true);
            System.Windows.Rect rect = card.Current.BoundingRectangle;
            double scale = rect.Width / 250.0;
            Point namePoint = new Point((int)(rect.Left + rect.Width / 2), (int)(rect.Top - 14 * scale));
            int clicksBefore = backdrop.ClickCount;
            ClickPoint(namePoint, true);
            Wait(delegate { return backdrop.ClickCount == clicksBefore + 1; }, "nickname strip click reaches the backdrop");

            Step("lock releases focus and launcher can restore editing");
            Click("player-1-notes-1");
            EnsureAppForeground();
            Click("lock-button");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) != 0; }, "locked window uses native pass-through style");
            Wait(delegate { return WindowProcess(GetForegroundWindow()) != running.Id; }, "locking releases keyboard focus");
            AutomationElement notes = Find("player-1-notes-1", true);
            System.Windows.Rect noteRect = notes.Current.BoundingRectangle;
            clicksBefore = backdrop.ClickCount;
            ClickPoint(Center(noteRect), true);
            Wait(delegate { return backdrop.ClickCount == clicksBefore + 1; }, "locked card sends clicks to backdrop");
            Check(WindowProcess(GetForegroundWindow()) == Process.GetCurrentProcess().Id, "controlled backdrop owns keyboard before typing");
            int keysBefore = backdrop.KeyCount;
            SendUnicode("k");
            Wait(delegate { return backdrop.KeyCount > keysBefore; }, "locked overlay does not swallow game-window keyboard input");
            Click("launcher-lock");
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) == 0; }, "launcher restores editing");

            Step("hide and restore from separate launcher");
            Click("hide-button");
            Wait(delegate { return !IsWindowVisible(overlayWindow); }, "overlay hides");
            Click("launcher-toggle");
            Wait(delegate { return IsWindowVisible(overlayWindow) && Visible("round-next"); }, "launcher shows overlay again");

            Step("global show and click-through shortcuts");
            Shortcut(Keys.G);
            Wait(delegate { return !IsWindowVisible(overlayWindow); }, "Ctrl+Shift+G hides overlay");
            Shortcut(Keys.G);
            Wait(delegate { return IsWindowVisible(overlayWindow); }, "Ctrl+Shift+G restores overlay");
            Shortcut(Keys.L);
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) != 0; }, "Ctrl+Shift+L enables pass-through");
            Shortcut(Keys.L);
            Wait(delegate { return (ExtendedStyle(overlayWindow) & 0x20L) == 0; }, "Ctrl+Shift+L restores editing");

            Step("physical card calibration and draggable launcher");
            Click("settings-toggle");
            SetChecked("calibration-toggle", true);
            Click("settings-close");
            System.Windows.Rect beforeDrag = Find("player-1", true).Current.BoundingRectangle;
            Drag(Center(Find("player-1-drag", true).Current.BoundingRectangle), new Point(42, 22));
            Wait(delegate { return Math.Abs(Find("player-1", true).Current.BoundingRectangle.Left - beforeDrag.Left - 42) < 3
                && Math.Abs(Find("player-1", true).Current.BoundingRectangle.Top - beforeDrag.Top - 22) < 3; }, "card follows physical mouse drag");
            string expectedProfiles = null;
            Wait(delegate { expectedProfiles = Json.Serialize(State()["profiles"]); return expectedProfiles.Contains("positions"); }, "calibrated position is saved");
            Click("settings-toggle");
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
            Capture("native-smoke-restart.png");
            StopApp();
            Console.WriteLine("PASS: standalone native exe; real mouse controls, Unicode text, roles, death/revival, feature switches, click-through, focus release, hotkeys, card/launcher dragging and restart persistence.");
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
        Thread.Sleep(250);
        Capture("native-smoke-start.png");
        CheckToolbarPainted();
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
                    if (pixels.GetPixel(x, y).ToArgb() == expected.ToArgb() && ++matching >= required) return true;
        }
        return false;
    }

    private static void StopApp()
    {
        if (Visible("quit-button"))
        {
            Click("quit-button");
            if (!running.WaitForExit(8000)) throw new Exception("Application did not quit through the toolbar.");
            Check(running.ExitCode == 0, "application exits cleanly");
            running.Dispose();
            running = null;
            return;
        }
        Click("launcher-menu");
        AutomationElement quit = null;
        Wait(delegate { quit = Find("quit-app", false) ?? Find("退出 GGD-help", false) ?? Find("quit-button", false); return quit != null; }, "quit menu opens");
        ClickElement(quit);
        if (!running.WaitForExit(8000)) throw new Exception("Application did not quit through its menu.");
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

    private static void Wait(Func<bool> check, string message, int timeout = 6000)
    {
        Stopwatch timer = Stopwatch.StartNew();
        Exception last = null;
        while (timer.ElapsedMilliseconds < timeout)
        {
            try { if (check()) return; }
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
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
}
