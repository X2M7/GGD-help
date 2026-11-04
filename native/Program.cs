using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("GGD-help")]
[assembly: AssemblyDescription("GGD-help native meeting overlay")]
[assembly: AssemblyCompany("X2M7")]
[assembly: AssemblyProduct("GGD-help")]
[assembly: AssemblyCopyright("Based on snapea/goose-duck-help")]
[assembly: AssemblyVersion("0.2.3.0")]
[assembly: AssemblyFileVersion("0.2.3.0")]

namespace GgdHelp
{
    internal static class Program
    {
        public const string Version = "0.2.3";
        [STAThread]
        private static void Main(string[] args)
        {
#if INCLUDE_MODEL_TESTS
            if (args.Length > 0 && args[0] == "--self-test") {
                string result = "PASS";
                try { ModelTests.Run(); }
                catch (Exception error) { result = error.ToString(); Environment.ExitCode = 1; }
                if (args.Length > 1) File.WriteAllText(args[1], result, new UTF8Encoding(false));
                return;
            }
#endif
            NativeWindows.InitializeDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string directory = Environment.GetEnvironmentVariable("OVERLAY_TEST_USER_DATA");
            if (String.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GGD-help");
            directory = Path.GetFullPath(directory);
            string identity;
            using (SHA256 hash = SHA256.Create()) identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
            bool first;
            using (Mutex instance = new Mutex(true, "Local\\GGD-help-native-" + identity, out first))
            using (EventWaitHandle restore = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\GGD-help-native-restore-" + identity)) {
                if (!first) { restore.Set(); return; }
                try {
                    Directory.CreateDirectory(directory);
                    Application.Run(new OverlayContext(directory, restore));
                } catch (OperationCanceledException) { }
                catch (Exception error) {
                    MessageBox.Show("GGD-help 无法继续运行：\r\n" + error.Message + "\r\n\r\n已有记录保留在：" + directory, "GGD-help", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.ExitCode = 1;
                } finally { instance.ReleaseMutex(); }
            }
        }
    }
}
