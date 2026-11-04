using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GgdHelp
{
    // Exercise real ToolStrip dispatch on a private desktop. No mouse movement,
    // visible windows, existing game, or user's saved profile is involved.
    internal static class MenuTests
    {
        private static int checks;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0) return RunOnPrivateDesktop();
            using (StreamWriter output = new StreamWriter(args[0])) {
                output.AutoFlush = true;
                Console.SetOut(output); Console.SetError(output);
                try { Run(); Console.WriteLine("Native menu checks passed: " + checks + " (private desktop)"); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static T Field<T>(OverlayContext context, string name)
        { return (T)typeof(OverlayContext).GetField(name, Private).GetValue(context); }

        private static void Call(OverlayContext context, string name)
        {
            try { typeof(OverlayContext).GetMethod(name, Private).Invoke(context, null); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static void PumpUntil(Func<bool> complete, string description)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!complete() && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            Check(complete(), description);
        }

        private static ContextMenuStrip Open(OverlayContext context, bool tray)
        {
            ContextMenuStrip menu = null;
            if (tray) {
                menu = Field<NotifyIcon>(context, "tray").ContextMenuStrip;
                menu.Show(Field<LauncherForm>(context, "launcher"), new Point(10, 10));
            } else {
                Call(context, "ShowMenu");
                // Looking up the real HWND also lets this test reproduce the
                // old one-use launcher menu, which had no persistent field.
                EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr parameter) {
                    ContextMenuStrip candidate = Control.FromHandle(window) as ContextMenuStrip;
                    if (candidate != null && candidate.Visible) menu = candidate;
                    return true;
                }, IntPtr.Zero);
            }
            Check(menu != null && menu.Visible && !menu.IsDisposed, "The requested menu opens");
            return menu;
        }

        private static ToolStripItem Item(ContextMenuStrip menu, string prefix)
        {
            foreach (ToolStripItem item in menu.Items)
                if (item.Text.StartsWith(prefix, StringComparison.Ordinal)) return item;
            throw new InvalidOperationException("Missing menu item: " + prefix);
        }

        private static void Choose(ContextMenuStrip menu, ToolStripItem item, OverlayContext context, Action duringDispatch)
        {
            bool clicked = false;
            EventHandler verify = delegate {
                clicked = true;
                Check(!menu.IsDisposed && !Field<OverlayForm>(context, "overlay").IsDisposed,
                    "Menu and owner stay alive throughout item-click dispatch");
                if (duringDispatch != null) duringDispatch();
            };
            item.Click += verify;
            try {
                // PerformClick follows ToolStripItem.HandleClick and
                // ToolStripDropDown.OnItemClicked, including native reparenting.
                // Disposing from Closed used to throw before Click was reached.
                item.PerformClick();
            } finally { item.Click -= verify; }
            Check(clicked && !menu.Visible && !menu.IsDisposed,
                "Selection finishes closing without disposing the active menu");
        }

        private static void Run()
        {
            NativeWindows.InitializeDpi();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string profile = Path.Combine(Path.GetTempPath(), "ggd-help-menu-qa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            Console.WriteLine("Isolated profile: " + profile);
            using (EventWaitHandle restore = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (OverlayContext context = new OverlayContext(profile, restore)) {
                try {
                    foreach (bool tray in new[] { false, true }) {
                        ContextMenuStrip original = null;
                        for (int repeat = 0; repeat < 3; repeat++) {
                            ContextMenuStrip menu = Open(context, tray);
                            if (original == null) original = menu;
                            Check(Object.ReferenceEquals(original, menu), "Reopening reuses the owned menu");
                            SettingsForm previous = Field<SettingsForm>(context, "settings");
                            Choose(menu, Item(menu, "位置 / 设置"), context, delegate {
                                Check(previous == null ? Field<SettingsForm>(context, "settings") == null : !previous.Visible,
                                    "Settings opens after the menu dispatch returns");
                            });
                            PumpUntil(delegate { SettingsForm settings = Field<SettingsForm>(context, "settings");
                                return settings != null && settings.Visible; }, "Menu action opens settings");
                            Field<SettingsForm>(context, "settings").Hide();

                            menu = Open(context, tray);
                            Choose(menu, Item(menu, "收起蒙版"), context, delegate {
                                Check(Field<bool>(context, "visible"), "Hide is deferred until menu dispatch ends");
                            });
                            PumpUntil(delegate { return !Field<bool>(context, "visible"); }, "Hide action runs");
                            menu = Open(context, tray);
                            Choose(menu, Item(menu, "显示蒙版"), context, null);
                            PumpUntil(delegate { return Field<bool>(context, "visible"); }, "Show action runs");

                            menu = Open(context, tray);
                            Choose(menu, Item(menu, "穿透游戏"), context, delegate {
                                Check(!Field<bool>(context, "locked"), "Lock is deferred until menu dispatch ends");
                            });
                            PumpUntil(delegate { return Field<bool>(context, "locked"); }, "Lock action runs");
                            menu = Open(context, tray);
                            Choose(menu, Item(menu, "允许编辑"), context, null);
                            PumpUntil(delegate { return !Field<bool>(context, "locked"); }, "Unlock action runs");

                            menu = Open(context, tray);
                            ToolStripMenuItem monitors = (ToolStripMenuItem)Item(menu, "游戏所在显示器");
                            Check(monitors.DropDownItems.Count == Screen.AllScreens.Length, "Monitor menu matches connected screens");
                            monitors.ShowDropDown();
                            Choose(menu, monitors.DropDownItems[0], context, null);
                            Application.DoEvents();
                            Check(!monitors.DropDown.Visible, "Selecting a monitor closes the submenu hierarchy");

                            menu = Open(context, tray);
                            Choose(menu, Item(menu, "找回开关"), context, null);
                            Application.DoEvents();
                            Check(Field<bool>(context, "visible") && !Field<bool>(context, "locked"), "Restore keeps editing available");
                            menu = Open(context, tray);
                            menu.Close(ToolStripDropDownCloseReason.Keyboard);
                            Check(!menu.Visible && !menu.IsDisposed, "Dismissal preserves the reusable menu");
                        }
                    }
                    ContextMenuStrip launcherMenu = Open(context, false);
                    launcherMenu.Close();
                    ContextMenuStrip trayMenu = Open(context, true);
                    Choose(trayMenu, Item(trayMenu, "退出 GGD-help"), context, delegate {
                        Check(!Field<bool>(context, "exiting"), "Quit waits until the menu callback returns");
                    });
                    PumpUntil(delegate { return Field<bool>(context, "exiting"); }, "Deferred quit runs");
                    Check(launcherMenu.IsDisposed && trayMenu.IsDisposed, "Quit disposes both owned menus after dispatch");
                } finally {
                    if (!Field<bool>(context, "exiting")) Call(context, "Quit");
                }
            }
        }

        private static int RunOnPrivateDesktop()
        {
            string name = "GGD-help-menu-qa-" + Guid.NewGuid().ToString("N");
            string log = Path.Combine(Path.GetTempPath(), name + ".log");
            IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create isolated test desktop");
            ProcessInformation process = new ProcessInformation();
            try {
                StartupInfo startup = new StartupInfo();
                startup.Size = Marshal.SizeOf(startup); startup.Desktop = name;
                string executable = typeof(MenuTests).Assembly.Location;
                if (!CreateProcess(executable, "\"" + executable + "\" \"" + log + "\"", IntPtr.Zero, IntPtr.Zero,
                    false, 0x08000000, IntPtr.Zero, null, ref startup, out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot launch isolated menu tests");
                if (WaitForSingleObject(process.Process, 60000) != 0) {
                    TerminateProcess(process.Process, 2);
                    WaitForSingleObject(process.Process, 5000);
                    if (File.Exists(log)) Console.Error.WriteLine(File.ReadAllText(log));
                    throw new TimeoutException("Menu test child did not finish within 60 seconds");
                }
                int result;
                if (!GetExitCodeProcess(process.Process, out result)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (File.Exists(log)) Console.Write(File.ReadAllText(log));
                return result;
            } finally {
                if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
                CloseDesktop(desktop);
            }
        }

        private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string application, string command, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out int code);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
