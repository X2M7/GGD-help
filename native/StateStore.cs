using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GgdHelp
{
    public static class StateStore
    {
        public const string FileName = "native-state.json";
        public static string LastMessage { get; private set; }
        public static bool LoadFailed { get; private set; }
        private static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 80 }; }

        public static AppState ImportJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("记录 JSON 为空或超过大小限制。");
            object data = Serializer().DeserializeObject(json);
            // A direct localStorage export and a keyed export are both accepted.
            IDictionary<string, object> root = data as IDictionary<string, object>;
            if (root == null) throw new InvalidDataException("记录 JSON 必须是对象。");
            object wrapped;
            if (root.TryGetValue("goose-duck-overlay:v2", out wrapped) && wrapped is string)
                return ImportJson((string)wrapped);
            object session;
            if (!root.TryGetValue("session", out session) || !(session is IDictionary<string, object>))
                throw new InvalidDataException("文件中没有 GGD-help 的 session 记录。");
            return Model.FromObject(root);
        }
        public static AppState ImportFile(string file)
        {
            FileInfo info = new FileInfo(file);
            if (info.Length > 8 * 1024 * 1024) throw new InvalidDataException("记录文件超过大小限制。");
            return ImportJson(File.ReadAllText(file, Encoding.UTF8));
        }
        public static string ExportJson(AppState state) { return Serializer().Serialize(Model.Normalize(state)); }

        public static AppState Load(string directory)
        {
            LastMessage = ""; LoadFailed = false;
            string target = Path.Combine(directory, FileName), backup = target + ".bak";
            if (File.Exists(target)) {
                try { return ImportFile(target); }
                catch (Exception error) {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                    if (File.Exists(backup)) {
                        try {
                            AppState recovered = ImportFile(backup);
                            LastMessage = "记录文件无法读取，已从上次备份恢复；原文件仍保留。";
                            return recovered;
                        } catch (Exception backupError) {
                            if (!(backupError is IOException || backupError is UnauthorizedAccessException || backupError is ArgumentException || backupError is InvalidOperationException)) throw;
                        }
                    }
                    LastMessage = "原生版记录无法读取，原文件仍保留。请通过设置导入已导出的 JSON 记录。";
                    LoadFailed = true;
                    return Model.CreateState();
                }
            }
            foreach (string name in new[] { "GGD-help-export.json", "electron-state.json", "goose-duck-overlay-v2.json", "overlay-export.json" }) {
                string file = Path.Combine(directory, name);
                if (!File.Exists(file)) continue;
                try {
                    AppState imported = ImportFile(file);
                    LastMessage = "已导入旧版 JSON 记录；旧版文件保持不变。";
                    return imported;
                } catch (Exception error) {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                    LastMessage = "找到旧版 JSON 文件，但无法读取：" + name + "。请在设置中重新导入。";
                    LoadFailed = true;
                    return Model.CreateState();
                }
            }
            string json, message;
            if (LegacyStorage.TryRead(directory, out json, out message)) {
                try {
                    AppState migrated = ImportJson(json);
                    LastMessage = "已读取旧版 Electron 记录；旧版本的文件保持不变。";
                    ReadLegacyWindow(directory, migrated);
                    return migrated;
                } catch (Exception error) {
                    if (!(error is IOException || error is ArgumentException || error is InvalidOperationException)) throw;
                    LastMessage = "找到旧版记录，但格式无法解析。原文件保持不变，请通过设置导入 JSON。";
                    LoadFailed = true;
                    return Model.CreateState();
                }
            }
            LastMessage = message;
            LoadFailed = !String.IsNullOrEmpty(message);
            return Model.CreateState();
        }
        private static void ReadLegacyWindow(string directory, AppState state)
        {
            string path = Path.Combine(directory, "overlay-window.json");
            if (!File.Exists(path)) return;
            try {
                IDictionary<string, object> obj = Model.Object(Serializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)));
                IDictionary<string, object> position = Model.Object(Model.Get(obj, "launcherPosition"));
                object x = Model.Get(position, "x"), y = Model.Get(position, "y");
                if (x is int && y is int) { state.launcherX = (int)x; state.launcherY = (int)y; }
            } catch (Exception error) {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                // Optional launcher geometry must not discard successfully recovered notes.
            }
        }
        public static void Save(string directory, AppState state)
        {
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, FileName), backup = target + ".bak";
            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            byte[] content = new UTF8Encoding(false).GetBytes(ExportJson(state));
            try {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                    stream.Write(content, 0, content.Length); stream.Flush(true);
                }
                if (File.Exists(target)) {
                    bool previousValid = true;
                    try { ImportFile(target); }
                    catch (Exception error) {
                        if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)) throw;
                        previousValid = false;
                    }
                    if (!previousValid) {
                        // Never replace a good backup with the corrupt file that Load rejected.
                        File.Copy(target, target + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"), false);
                    }
                    File.Replace(temporary, target, previousValid ? backup : null);
                } else File.Move(temporary, target);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
