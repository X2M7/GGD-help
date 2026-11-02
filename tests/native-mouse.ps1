param()
$ErrorActionPreference = 'Stop'

# Run in a hidden child process. No keystrokes are injected; only controlled
# mouse actions for the isolated QA windows are accepted through stdin.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class GgdNativeMouse {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public UIntPtr dwExtraInfo;
  }
  [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mouse; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION data; }
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count, INPUT[] input, int size);
  public static void Click(int x, int y) {
    if (!SetCursorPos(x, y)) throw new Exception("SetCursorPos failed.");
    Thread.Sleep(250); // Give native hit-testing its next poll before mouse-down.
    var input = new INPUT[1];
    input[0].data.mouse.dwFlags = 0x0002;
    if (SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Exception("SendInput mouse-down failed.");
    Thread.Sleep(60);
    input[0].data.mouse.dwFlags = 0x0004;
    if (SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Exception("SendInput mouse-up failed.");
    Thread.Sleep(150);
  }
}
'@
[void][GgdNativeMouse]::SetProcessDpiAwarenessContext([IntPtr](-4))
$original = New-Object GgdNativeMouse+POINT
[void][GgdNativeMouse]::GetCursorPos([ref]$original)
[Console]::Out.WriteLine('{"ready":true}')
try {
  while ($null -ne ($line = [Console]::In.ReadLine())) {
    try {
      $request = ConvertFrom-Json $line
      if ($request.action -eq 'close') { break }
      if ($request.action -eq 'capture') {
        Add-Type -AssemblyName System.Drawing
        $bitmap = New-Object System.Drawing.Bitmap ([int]$request.width), ([int]$request.height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
          $graphics.CopyFromScreen([int]$request.x, [int]$request.y, 0, 0, $bitmap.Size)
          $bitmap.Save([string]$request.path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
        [Console]::Out.WriteLine('{"ok":true}')
        continue
      }
      if ($request.action -notin @('move', 'click')) { throw 'Unknown mouse action.' }
      $x = [int]$request.x
      $y = [int]$request.y
      if ($request.action -eq 'click') { [GgdNativeMouse]::Click($x, $y) }
      else {
        if (-not [GgdNativeMouse]::SetCursorPos($x, $y)) { throw 'SetCursorPos failed.' }
        Start-Sleep -Milliseconds 250
      }
      [Console]::Out.WriteLine('{"ok":true}')
    } catch {
      [Console]::Out.WriteLine((@{ error = $_.Exception.Message } | ConvertTo-Json -Compress))
    }
  }
} finally {
  [void][GgdNativeMouse]::SetCursorPos($original.X, $original.Y)
}
