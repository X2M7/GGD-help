using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GgdHelp
{
    internal static class NativeWindows
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;
        private static IntPtr lastExternalForeground;
        private static bool dpiInitialized;

        // Must run before creating any Control or asking WinForms for Screen bounds.
        public static void InitializeDpi()
        {
            if (dpiInitialized) return;
            dpiInitialized = true;
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
                if (Marshal.GetLastWin32Error() == 5) return; // Already set by manifest.
                if (SetProcessDpiAwarenessContext(new IntPtr(-3))) return;
                if (Marshal.GetLastWin32Error() == 5) return;
            }
            catch (EntryPointNotFoundException) { }
            try
            {
                int result = SetProcessDpiAwareness(2);
                if (result == 0 || result == unchecked((int)0x80070005)) return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        public static float GetDpiScale(Screen target)
        {
            if (target == null) target = Screen.PrimaryScreen;
            // Prefer the modern HWND-aware API once a window exists on this
            // display; before the first HWND, query its monitor directly.
            try
            {
                foreach (Form window in Application.OpenForms)
                {
                    if (!window.IsHandleCreated || window.IsDisposed
                        || Screen.FromHandle(window.Handle).DeviceName != target.DeviceName) continue;
                    uint dpi = GetDpiForWindow(window.Handle);
                    if (dpi > 0) return ClampScale(dpi / 96f);
                }
            }
            catch (EntryPointNotFoundException) { }
            Rectangle bounds = target.Bounds;
            Point middle = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            IntPtr monitor = MonitorFromPoint(middle, 2);
            try
            {
                uint x, y;
                if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0)
                    return ClampScale(x / 96f);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi > 0) return ClampScale(dpi / 96f);
            }
            catch (EntryPointNotFoundException) { }
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 1f;
            try { return ClampScale(GetDeviceCaps(dc, 88) / 96f); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        private static float ClampScale(float scale)
        {
            return Math.Max(0.75f, Math.Min(8f, scale));
        }

        public static void SetClickThrough(Form form, bool enabled)
        {
            if (form == null || form.IsDisposed) return;
            IntPtr handle = form.Handle;
            bool wasVisible = form.Visible;
            bool hadFocus = GetForegroundWindow() == handle || form.ContainsFocus;
            RememberExternalForeground();
            // NOACTIVATE alone does not revoke an existing keyboard focus. Hiding
            // the focused HWND first lets Windows return focus to the game.
            if (enabled && hadFocus) form.Hide();
            long style = GetExtendedStyle(handle) | WS_EX_LAYERED;
            if (enabled) style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            else style &= ~(long)(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            SetExtendedStyle(handle, style);
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            if (enabled && hadFocus)
            {
                if (wasVisible) ShowInactive(form);
                RestoreExternalForegroundIfNeeded(handle);
            }
        }

        // Overlay and launcher forms should additionally override
        // ShowWithoutActivation, so WinForms itself also uses a nonactive show.
        public static void ShowInactive(Form form)
        {
            if (form == null || form.IsDisposed) return;
            RememberExternalForeground();
            IntPtr previousForeground = GetForegroundWindow();
            IntPtr handle = form.Handle;
            long originalStyle = GetExtendedStyle(handle);
            SetExtendedStyle(handle, originalStyle | WS_EX_NOACTIVATE);
            try
            {
                if (!form.Visible) form.Show();
                ShowWindow(handle, SW_SHOWNOACTIVATE);
                SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            finally { SetExtendedStyle(handle, originalStyle); }
            // Showing the launcher while editing must also preserve the overlay's
            // input focus; only repair focus accidentally taken by this show.
            if (previousForeground != handle && previousForeground != IntPtr.Zero
                && GetForegroundWindow() == handle && IsWindow(previousForeground))
                SetForegroundWindow(previousForeground);
        }

        private static void RememberExternalForeground()
        {
            IntPtr current = GetForegroundWindow();
            if (current != IntPtr.Zero && !IsOurWindow(current)) lastExternalForeground = current;
        }

        private static bool IsOurWindow(IntPtr handle)
        {
            uint processId;
            GetWindowThreadProcessId(handle, out processId);
            return processId == CurrentProcessId;
        }

        private static void RestoreExternalForegroundIfNeeded(IntPtr shownWindow)
        {
            IntPtr current = GetForegroundWindow();
            if ((current == shownWindow || current == IntPtr.Zero)
                && lastExternalForeground != IntPtr.Zero
                && IsWindow(lastExternalForeground) && IsWindowVisible(lastExternalForeground))
                SetForegroundWindow(lastExternalForeground);
        }

        public static bool RegisterHotKey(IntPtr window, int id, uint modifiers, Keys key)
        {
            return RegisterHotKeyNative(window, id, modifiers | MOD_NOREPEAT, (uint)key);
        }

        public static bool UnregisterHotKey(IntPtr window, int id)
        {
            return UnregisterHotKeyNative(window, id);
        }

        public static void BeginMove(Form form)
        {
            if (form == null || form.IsDisposed) return;
            ReleaseCapture();
            SendMessage(form.Handle, 0x00A1, new IntPtr(2), IntPtr.Zero); // HTCAPTION
        }

        public static Point ClampToWorkingArea(Point requested, Size size, Screen target)
        {
            Rectangle area = (target ?? Screen.FromPoint(requested)).WorkingArea;
            return new Point(
                Math.Max(area.Left, Math.Min(requested.X, area.Right - Math.Min(size.Width, area.Width))),
                Math.Max(area.Top, Math.Min(requested.Y, area.Bottom - Math.Min(size.Height, area.Height))));
        }

        private static long GetExtendedStyle(IntPtr handle)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() : GetWindowLong(handle, GWL_EXSTYLE);
        }

        private static void SetExtendedStyle(IntPtr handle, long style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
            else SetWindowLong(handle, GWL_EXSTYLE, unchecked((int)style));
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)] private static extern bool RegisterHotKeyNative(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)] private static extern bool UnregisterHotKeyNative(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
