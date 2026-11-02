using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GgdHelp
{
    public static class ModelTests
    {
        private static int assertions;
        private static void Check(bool value, string description)
        { assertions++; if (!value) throw new Exception("Native model test failed: " + description); }
        private static void Near(double value, double expected, string description)
        { Check(Math.Abs(value - expected) < 0.0000001, description + ": " + value + " != " + expected); }
        public static void Run()
        {
            assertions = 0;
            AppState state = Model.CreateState();
            Check(state.session.players.Count == 16 && state.session.round == 1, "new session");
            state.session.players[0].roles.good = "警长"; state.session.players[0].notes = "中文与 emoji 🦆";
            state.session.players[0].notes2 = "第二备注";
            Check(state.session.players[1].roles.good == "", "independent role objects");
            Session killed = Model.SetDeath(state.session, 1, 3, "刀杀");
            Session later = Model.SetRound(killed, 8);
            Check(later.players[0].death.round == 3 && later.players[0].death.cause == "刀杀", "death round preserved");
            Session revived = Model.RevivePlayer(later, 1);
            Check(revived.players[0].death == null && revived.players[0].notes2 == "第二备注", "revival retains notes");
            Check(killed.players[0].death != null && state.session.players[0].death == null, "mutations copy session");
            Check(Model.SetRound(state.session, 4.5).round == 5 && Model.SetRound(state.session, Double.NaN).round == 1, "JavaScript rounding and invalid values");
            Check(Model.SetDeath(state.session, 2, 1000, "bogus").players[1].death.cause == "不明", "unknown cause");

            AppState migrated = StateStore.ImportJson("{\"session\":{\"round\":-1,\"players\":[{\"id\":2,\"role\":\"鹈鹕\"},{\"id\":1,\"role\":\"警长\",\"notes\":\"旧备注\"},{\"id\":3,\"role\":\"刺客\",\"roles\":{}}]}}");
            Check(migrated.session.round == 1, "import round lower bound");
            Check(migrated.session.players[0].roles.good == "警长" && migrated.session.players[0].notes == "旧备注", "legacy identity and reordered IDs");
            Check(migrated.session.players[1].roles.neutral == "鹈鹕", "legacy neutral identity");
            Check(migrated.session.players[2].roles.bad == "", "explicit cleared grouped role overrides legacy");
            Check(migrated.preferences.features.roles && migrated.preferences.features.notes && migrated.preferences.features.death, "old preferences default visible");
            AppState malformed = StateStore.ImportJson("{\"session\":{\"players\":[{\"id\":1,\"notes\":\"\\u0000hello\",\"death\":{\"round\":300,\"cause\":\"?\"}}]},\"preferences\":{\"opacity\":null,\"playerCount\":\"1\",\"features\":{\"roles\":\"false\",\"notes\":false}}}");
            Check(malformed.session.players[0].notes == "hello" && malformed.session.players[0].death.round == 99, "control characters and bounds");
            Check(malformed.preferences.playerCount == 16 && malformed.preferences.opacity == 68 && malformed.preferences.features.roles && !malformed.preferences.features.notes, "strict JSON types");
            for (int mask = 0; mask < 8; mask++) {
                state.preferences.features = new Features { roles = (mask & 1) != 0, notes = (mask & 2) != 0, death = (mask & 4) != 0 };
                AppState read = StateStore.ImportJson(StateStore.ExportJson(state));
                Check(read.preferences.features.roles == state.preferences.features.roles && read.preferences.features.notes == state.preferences.features.notes && read.preferences.features.death == state.preferences.features.death, "feature combination " + mask);
                Check(read.session.players[0].notes == state.session.players[0].notes, "Unicode JSON round trip");
            }
            Check(Model.CreateState().preferences.features.roles, "feature defaults not shared");

            Layout layout = Model.CreateLayout();
            List<CardPosition> points = Model.ComputePositions(layout, 1920, 1080);
            Near(points[0].x, 198, "original x"); Near(points[0].y, 273, "original y");
            Near(points[1].x - points[0].x, 386, "original columns"); Near(points[4].y - points[0].y, 178, "original rows");
            foreach (ResolutionPreset preset in Model.ResolutionPresets) {
                layout = Model.CreateLayout(); layout.aspect = preset.aspect;
                foreach (CardPosition point in Model.ComputePositions(layout, preset.width, preset.height))
                    Check(point.x >= 0 && point.y >= 0 && point.x + Model.CardWidth * point.scale <= preset.width + .000001 && point.y + Model.CardHeight * point.scale <= preset.height + .000001, "preset screen bounds");
            }
            Viewport viewport = Model.ComputeViewport(3440, 1440, "16:9"); Near(viewport.x, 440, "ultrawide letterbox");
            Near(Model.ComputeViewport(1920, 1200, "16:9").y, 60, "16:10 letterbox");
            Near(Model.ComputeViewport(3440, 1440, "43:18").width, 3440, "exact ultrawide ratio");
            Near(Model.ComputeViewport(2560, 1080, "64:27").width, 2560, "exact 2560 ratio");
            layout = Model.CreateLayout(); layout.scale = 1.2; layout.offsetX = .07; layout.offsetY = -.04; layout.cardScale = 1.3;
            Layout moved = Model.MovePlayer(layout, 6, 800, 470, 3440, 1440); points = Model.ComputePositions(moved, 3440, 1440);
            Near(points[5].x, 800, "drag coordinate inverse x"); Near(points[5].y, 470, "drag coordinate inverse y");
            Check(moved.positions[5] != layout.positions[5], "drag does not mutate old layout");
            moved = Model.MovePlayer(Model.CreateLayout(), 1, -900, 99999, 3440, 1440); CardPosition edge = Model.ComputePositions(moved, 3440, 1440)[0];
            Near(edge.x, 0, "left screen clamp"); Near(edge.y + Model.CardHeight * edge.scale, 1440, "bottom screen clamp");
            foreach (int width in new[] { 1, 50, 800, 2160 }) foreach (int height in new[] { 1, 20, 600, 3840 })
                foreach (CardPosition point in Model.ComputePositions(layout, width, height))
                    Check(point.x >= 0 && point.y >= 0 && point.x + Model.CardWidth * point.scale <= width + .000001 && point.y + Model.CardHeight * point.scale <= height + .000001, "tiny/portrait screen bounds");
            Check(Model.ProfileKey("100",1920,1080,1) == "display:100|1920x1080|dpi:1", "profile key compatibility");
            Check(Model.ProfileKey("100",1920,1080,1) != Model.ProfileKey("100",1920,1080,1.5), "DPI-specific profile");
            TestSnappy();
            TestStorage();
            TestLoadFailureFlag();
            Console.WriteLine("Native model/storage checks passed: " + assertions);
        }
        private static void TestSnappy()
        {
            // Literal 'abc', followed by an overlapping two-byte-offset copy.
            byte[] decoded = LegacyStorage.DecodeSnappy(new byte[] { 9, 8, 97, 98, 99, 22, 3, 0 });
            Check(Encoding.ASCII.GetString(decoded) == "abcabcabc", "Snappy overlap copy");
            // Copy variants with one- and four-byte offsets are accepted too.
            Check(Encoding.ASCII.GetString(LegacyStorage.DecodeSnappy(new byte[] { 8, 12, 97,98,99,100,1,4 })) == "abcdabcd", "Snappy one-byte copy");
            Check(Encoding.ASCII.GetString(LegacyStorage.DecodeSnappy(new byte[] { 8, 12, 97,98,99,100,15,4,0,0,0 })) == "abcdabcd", "Snappy four-byte copy");
            bool rejected = false;
            try { LegacyStorage.DecodeSnappy(new byte[] { 8, 1, 0 }); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid Snappy offset rejected");
        }
        private static void TestStorage()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ggd-model-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                AppState state = Model.CreateState(); state.session.players[0].notes = "first";
                StateStore.Save(directory, state);
                state.session.players[0].notes = "second"; state.session.round = 5; state.profiles["screen"] = Model.CreateLayout();
                StateStore.Save(directory, state);
                AppState loaded = StateStore.Load(directory);
                Check(loaded.session.players[0].notes == "second" && loaded.session.round == 5 && loaded.profiles.ContainsKey("screen"), "atomic save round trip");
                Check(StateStore.ImportFile(Path.Combine(directory, StateStore.FileName + ".bak")).session.players[0].notes == "first", "previous state backup");
                File.WriteAllText(Path.Combine(directory, StateStore.FileName), "{corrupt");
                loaded = StateStore.Load(directory);
                Check(loaded.session.players[0].notes == "first" && StateStore.LastMessage.Length > 0, "corrupt primary restores backup and reports");
                StateStore.Save(directory, loaded);
                Check(Directory.GetFiles(directory, "*.corrupt-*").Length == 1, "corrupt original archived");
                Check(StateStore.ImportFile(Path.Combine(directory, StateStore.FileName + ".bak")).session.players[0].notes == "first", "good backup not replaced by corrupt original");
                Check(Directory.GetFiles(directory, "*.tmp-*").Length == 0, "atomic write temporary cleaned");
            } finally {
                string full = Path.GetFullPath(directory), root = Path.GetFullPath(Path.GetTempPath());
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("ggd-model-test-", StringComparison.Ordinal)) Directory.Delete(full, true);
            }
        }
        private static string Fingerprint(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories); Array.Sort(files, StringComparer.Ordinal);
            StringBuilder result = new StringBuilder();
            using (SHA256 hash = SHA256.Create()) foreach (string file in files) {
                result.Append(file).Append(':'); using (FileStream stream = File.OpenRead(file)) result.Append(Convert.ToBase64String(hash.ComputeHash(stream)));
            }
            return result.ToString();
        }
        public static void TestLoadFailureFlag()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ggd-model-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                StateStore.Load(directory); Check(!StateStore.LoadFailed, "no saved data is not a load failure");
                string primary = Path.Combine(directory, StateStore.FileName);
                File.WriteAllText(primary, "broken");
                StateStore.Load(directory); Check(StateStore.LoadFailed && File.ReadAllText(primary) == "broken", "unreadable primary is reported and preserved");
                File.WriteAllText(primary + ".bak", StateStore.ExportJson(Model.CreateState()));
                StateStore.Load(directory); Check(!StateStore.LoadFailed, "successful backup recovery is not failure");
                string exportDirectory = Path.Combine(directory, "export"); Directory.CreateDirectory(exportDirectory);
                string export = Path.Combine(exportDirectory, "GGD-help-export.json"); File.WriteAllText(export, "broken");
                StateStore.Load(exportDirectory); Check(StateStore.LoadFailed, "bad legacy export is a failure");
                File.WriteAllText(export, StateStore.ExportJson(Model.CreateState()));
                StateStore.Load(exportDirectory); Check(!StateStore.LoadFailed, "successful export import resets failure flag");
                string brokenDatabase = Path.Combine(directory, "db"); Directory.CreateDirectory(Path.Combine(brokenDatabase, "Local Storage", "leveldb"));
                StateStore.Load(brokenDatabase); Check(StateStore.LoadFailed, "unreadable existing LevelDB is a failure");
                StateStore.Load(Path.Combine(directory,"absent")); Check(!StateStore.LoadFailed, "next clean load clears old failure flag");
            } finally {
                string full = Path.GetFullPath(directory), root = Path.GetFullPath(Path.GetTempPath());
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("ggd-model-test-", StringComparison.Ordinal)) Directory.Delete(full, true);
            }
            Console.WriteLine("LoadFailed status checks passed.");
        }
        public static void TestLegacyFixture(string directory)
        {
            string before = Fingerprint(directory), expected = File.ReadAllText(Path.Combine(directory,"expected.json"), Encoding.UTF8);
            string json, message; bool found = LegacyStorage.TryRead(directory, out json, out message);
            if (expected.Trim() == "null") {
                Check(!found && String.IsNullOrEmpty(message), "latest tombstone prevents stale migration");
                StateStore.Load(directory); Check(!StateStore.LoadFailed, "deleted target is not a migration failure");
            }
            else {
                Check(found, "real Electron fixture read: " + LegacyStorage.LastFailureDetail);
                Check(StateStore.ExportJson(StateStore.ImportJson(json)) == StateStore.ExportJson(StateStore.ImportJson(expected)), "real Electron JSON exactly matches expected state");
                StateStore.Load(directory); Check(!StateStore.LoadFailed, "successful LevelDB import is not a load failure");
            }
            Check(before == Fingerprint(directory), "legacy files are unchanged");
            Console.WriteLine("Legacy fixture passed: " + directory);
        }
#if MODEL_TEST_MAIN
        public static int Main(string[] args)
        {
            try { Run(); foreach (string directory in args) TestLegacyFixture(directory); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
#endif
    }
}
