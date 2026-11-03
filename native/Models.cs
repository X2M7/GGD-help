using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace GgdHelp
{
    public sealed class AppState
    {
        public int version = 2;
        public Session session;
        public Dictionary<string, Layout> profiles = new Dictionary<string, Layout>();
        public Preferences preferences;
        public string selectedDisplay = "";
        public int? launcherX;
        public int? launcherY;
    }
    public sealed class Session { public int round = 1; public List<Player> players = new List<Player>(); }
    public sealed class Player
    {
        public int id;
        public string name = "", role = "", notes = "", notes2 = "";
        public Roles roles;
        public Death death;
        [ScriptIgnore] public bool hasId = true;
    }
    public sealed class Roles { public string good = "", neutral = "", bad = ""; }
    public sealed class Death { public int round = 1; public string cause = "不明"; }
    public sealed class Preferences
    {
        public int playerCount = 16;
        public double opacity = 68;
        public Features features = new Features();
    }
    public sealed class Features { public bool roles = true, notes = true, death = true; }
    public sealed class Layout
    {
        public string aspect = "16:9";
        public double offsetX, offsetY, scale = 1, cardScale = 1;
        public List<Position> positions;
    }
    public sealed class Position
    {
        public double x, y;
        public Position() { }
        public Position(double xValue, double yValue) { x = xValue; y = yValue; }
    }
    public sealed class CardPosition { public int id; public double x, y, scale; }
    public sealed class Viewport { public double x, y, width, height; }
    public sealed class ResolutionPreset
    {
        public int width, height;
        public string aspect;
        public ResolutionPreset(int w, int h, string a) { width = w; height = h; aspect = a; }
        public override string ToString() { return width + " × " + height; }
    }

    public static class Model
    {
        public const int PlayerCount = 16, CardWidth = 300, CardHeight = 160;
        public static readonly string[] Aspects = { "16:9", "16:10", "21:9", "43:18", "64:27", "stretch" };
        public static readonly string[] DeathCauses = { "刀杀", "投票出局", "刺杀", "炸死", "吞食", "连带", "不明" };
        public static readonly string[] RoleKeys = { "good", "neutral", "bad" };
        public static readonly string[] RoleLabels = { "好", "中", "坏" };
        public static readonly string[][] RoleGroups = {
            new[] { "好人", "肉汁", "正义使者", "警长", "加拿大", "殡仪员", "网红", "模仿", "侦探", "工程师", "复仇", "观鸟", "保镖", "政治家", "锁匠", "冒险家", "星界行者", "流浪儿童", "追踪者", "跟踪者" },
            new[] { "中立", "呆呆", "秃鹫", "鹈鹕", "鸽子", "猎鹰", "决斗呆呆" },
            new[] { "坏比", "丧葬者", "隐形", "食鸭鸟", "变形者", "静语者", "专业杀手", "间谍", "刺客", "雇佣杀手", "告密者", "派对狂", "爆炸王", "身份窃贼", "忍者", "连环杀手", "术士", "超能力" }
        };
        public static readonly ResolutionPreset[] ResolutionPresets = {
            new ResolutionPreset(1280,720,"16:9"), new ResolutionPreset(1366,768,"16:9"),
            new ResolutionPreset(1600,900,"16:9"), new ResolutionPreset(1920,1080,"16:9"),
            new ResolutionPreset(2560,1440,"16:9"), new ResolutionPreset(3840,2160,"16:9"),
            new ResolutionPreset(1920,1200,"16:10"), new ResolutionPreset(2560,1600,"16:10"),
            new ResolutionPreset(2560,1080,"64:27"), new ResolutionPreset(3440,1440,"43:18")
        };

        public static double Clamp(double value, double min, double max) { return Math.Min(max, Math.Max(min, value)); }
        private static double Number(double value, double fallback, double min, double max)
        { return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Clamp(value, min, max); }
        private static int Round(double value, int fallback)
        { return (int)Math.Floor(Number(value, fallback, 1, 99) + 0.5); }
        private static string Clean(string value, int max)
        {
            if (value == null) return "";
            StringBuilder text = new StringBuilder();
            foreach (char ch in value)
            {
                if (ch <= 8 || ch == 11 || ch == 12 || (ch >= 14 && ch <= 31) || ch == 127) continue;
                text.Append(ch);
                if (text.Length >= max) break;
            }
            return text.ToString();
        }
        public static AppState CreateState()
        { return new AppState { session = CreateSession(), preferences = new Preferences() }; }
        public static Session CreateSession()
        {
            Session session = new Session();
            for (int i = 1; i <= PlayerCount; i++) session.players.Add(new Player { id = i, roles = new Roles() });
            return session;
        }
        public static Session NormalizeSession(Session raw)
        {
            Session result = CreateSession();
            if (raw == null) return result;
            result.round = Round(raw.round, 1);
            List<Player> source = raw.players ?? new List<Player>();
            for (int i = 0; i < PlayerCount; i++)
            {
                Player item = null;
                for (int j = 0; j < Math.Min(PlayerCount, source.Count); j++)
                    if (source[j] != null && source[j].hasId && source[j].id == i + 1) { item = source[j]; break; }
                if (item == null && i < source.Count && source[i] != null && !source[i].hasId) item = source[i];
                if (item == null) continue;
                Player target = result.players[i];
                target.name = Clean(item.name, 32); target.role = Clean(item.role, 32);
                target.notes = Clean(item.notes, 240); target.notes2 = Clean(item.notes2, 240);
                if (item.roles != null)
                    target.roles = new Roles { good = Clean(item.roles.good, 32), neutral = Clean(item.roles.neutral, 32), bad = Clean(item.roles.bad, 32) };
                else
                    target.roles = new Roles {
                        good = Array.IndexOf(RoleGroups[0], target.role) >= 0 ? target.role : "",
                        neutral = Array.IndexOf(RoleGroups[1], target.role) >= 0 ? target.role : "",
                        bad = Array.IndexOf(RoleGroups[2], target.role) >= 0 ? target.role : ""
                    };
                if (item.death != null) target.death = new Death {
                    round = Round(item.death.round, 1), cause = Array.IndexOf(DeathCauses, item.death.cause) >= 0 ? item.death.cause : "不明"
                };
            }
            return result;
        }
        public static Session SetRound(Session raw, double round)
        { Session result = NormalizeSession(raw); result.round = Round(round, result.round); return result; }
        public static Session SetDeath(Session raw, int id, double round, string cause)
        {
            Session result = NormalizeSession(raw);
            if (id >= 1 && id <= PlayerCount) result.players[id - 1].death = new Death {
                round = Round(round, result.round), cause = Array.IndexOf(DeathCauses, cause) >= 0 ? cause : "不明"
            };
            return result;
        }
        public static Session RevivePlayer(Session raw, int id)
        { Session result = NormalizeSession(raw); if (id >= 1 && id <= PlayerCount) result.players[id - 1].death = null; return result; }
        public static Preferences NormalizePreferences(Preferences raw)
        {
            if (raw == null) return new Preferences();
            Features f = raw.features ?? new Features();
            return new Preferences {
                playerCount = (int)Clamp(raw.playerCount, 1, PlayerCount), opacity = Number(raw.opacity, 68, 15, 95),
                features = new Features { roles = f.roles, notes = f.notes, death = f.death }
            };
        }
        public static AppState Normalize(AppState raw)
        {
            if (raw == null) return CreateState();
            AppState result = new AppState {
                session = NormalizeSession(raw.session), preferences = NormalizePreferences(raw.preferences),
                selectedDisplay = Clean(raw.selectedDisplay, 200), launcherX = raw.launcherX, launcherY = raw.launcherY
            };
            if (raw.profiles != null)
                foreach (KeyValuePair<string, Layout> item in raw.profiles)
                    if (item.Key != null && item.Key.Length < 200 && result.profiles.Count < 100) result.profiles[item.Key] = NormalizeLayout(item.Value);
            return result;
        }
        public static Layout CreateLayout()
        {
            Layout layout = new Layout { positions = new List<Position>() };
            for (int i = 0; i < PlayerCount; i++) layout.positions.Add(new Position((198 + i % 4 * 386) / 1920.0, (273 + i / 4 * 178) / 1080.0));
            return layout;
        }
        public static Layout NormalizeLayout(Layout raw)
        {
            Layout result = CreateLayout();
            if (raw == null) return result;
            result.aspect = Array.IndexOf(Aspects, raw.aspect) >= 0 ? raw.aspect : "16:9";
            result.offsetX = Number(raw.offsetX, 0, -1, 1); result.offsetY = Number(raw.offsetY, 0, -1, 1);
            result.scale = Number(raw.scale, 1, .35, 2); result.cardScale = Number(raw.cardScale, 1, .6, 1.8);
            if (raw.positions != null)
                for (int i = 0; i < Math.Min(PlayerCount, raw.positions.Count); i++)
                    if (raw.positions[i] != null) result.positions[i] = new Position(
                        Number(raw.positions[i].x, result.positions[i].x, -8, 9), Number(raw.positions[i].y, result.positions[i].y, -8, 9));
            return result;
        }
        public static Viewport ComputeViewport(int width, int height, string aspect)
        {
            double w = Number(width, 1920, 1, 32768), h = Number(height, 1080, 1, 32768);
            if (aspect == "stretch") return new Viewport { width = w, height = h };
            string selected = Array.IndexOf(Aspects, aspect) >= 0 ? aspect : "16:9";
            string[] parts = selected.Split(':');
            double ratio = Double.Parse(parts[0], CultureInfo.InvariantCulture) / Double.Parse(parts[1], CultureInfo.InvariantCulture);
            double vw = Math.Min(w, h * ratio), vh = Math.Min(h, w / ratio);
            return new Viewport { x = (w - vw) / 2, y = (h - vh) / 2, width = vw, height = vh };
        }
        private static double Scale(Layout layout, int width, int height, Viewport viewport)
        { return Math.Min(Math.Min(viewport.width / 1920, viewport.height / 1080) * layout.cardScale, Math.Min(width / (double)CardWidth, height / (double)CardHeight)); }
        public static List<CardPosition> ComputePositions(Layout raw, int width, int height)
        {
            width = (int)Clamp(width, 1, 32768); height = (int)Clamp(height, 1, 32768);
            Layout layout = NormalizeLayout(raw); Viewport viewport = ComputeViewport(width, height, layout.aspect);
            double scale = Scale(layout, width, height, viewport);
            List<CardPosition> result = new List<CardPosition>();
            for (int i = 0; i < PlayerCount; i++) result.Add(new CardPosition {
                id = i + 1, scale = scale,
                x = Clamp(viewport.x + ((layout.positions[i].x - .5) * layout.scale + .5 + layout.offsetX) * viewport.width, 0, width - CardWidth * scale),
                y = Clamp(viewport.y + ((layout.positions[i].y - .5) * layout.scale + .5 + layout.offsetY) * viewport.height, 0, height - CardHeight * scale)
            });
            return result;
        }
        public static Layout MovePlayer(Layout raw, int id, double screenX, double screenY, int width, int height)
        {
            Layout layout = NormalizeLayout(raw);
            if (id < 1 || id > PlayerCount || Double.IsNaN(screenX) || Double.IsInfinity(screenX) || Double.IsNaN(screenY) || Double.IsInfinity(screenY)) return layout;
            width = (int)Clamp(width, 1, 32768); height = (int)Clamp(height, 1, 32768);
            Viewport viewport = ComputeViewport(width, height, layout.aspect); double scale = Scale(layout, width, height, viewport);
            double x = Clamp(screenX, 0, width - CardWidth * scale), y = Clamp(screenY, 0, height - CardHeight * scale);
            layout.positions[id - 1] = new Position(((x - viewport.x) / viewport.width - .5 - layout.offsetX) / layout.scale + .5,
                ((y - viewport.y) / viewport.height - .5 - layout.offsetY) / layout.scale + .5);
            return NormalizeLayout(layout);
        }
        public static string ProfileKey(string id, int width, int height, double scaleFactor)
        { return "display:" + Clean(id ?? "primary", 120) + "|" + (int)Clamp(width, 1, 32768) + "x" + (int)Clamp(height, 1, 32768) + "|dpi:" + Number(scaleFactor, 1, .25, 8).ToString("0.########", CultureInfo.InvariantCulture); }

        // Read untyped JSON so null, strings and booleans are not silently
        // coerced into coordinates by JavaScriptSerializer's typed converter.
        internal static IDictionary<string, object> Object(object raw)
        { return raw as IDictionary<string, object> ?? new Dictionary<string, object>(); }
        internal static object Get(IDictionary<string, object> obj, string key)
        { object value; return obj.TryGetValue(key, out value) ? value : null; }
        private static double RawNumber(object value, double fallback, double min, double max)
        {
            if (!(value is int || value is long || value is double || value is decimal || value is float || value is short || value is byte)) return fallback;
            return Number(Convert.ToDouble(value, CultureInfo.InvariantCulture), fallback, min, max);
        }
        private static string RawText(IDictionary<string, object> obj, string key, int limit) { return Clean(Get(obj, key) as string, limit); }
        public static AppState FromObject(object raw)
        {
            IDictionary<string, object> source = Object(raw), s = Object(Get(source, "session"));
            AppState state = CreateState();
            state.session.round = (int)Math.Floor(RawNumber(Get(s, "round"), 1, 1, 99) + .5);
            IList players = Get(s, "players") as IList;
            if (players != null) {
                state.session.players.Clear();
                for (int i = 0; i < Math.Min(PlayerCount, players.Count); i++) {
                    IDictionary<string, object> item = Object(players[i]);
                    double rawId = RawNumber(Get(item, "id"), 0, -1000, 1000);
                    Player p = new Player { id = Math.Floor(rawId) == rawId ? (int)rawId : 0, hasId = item.ContainsKey("id"),
                        name = RawText(item,"name",32), role = RawText(item,"role",32), notes = RawText(item,"notes",240), notes2 = RawText(item,"notes2",240) };
                    IDictionary<string, object> roles = Get(item,"roles") as IDictionary<string, object>;
                    if (roles != null) p.roles = new Roles { good = RawText(roles,"good",32), neutral = RawText(roles,"neutral",32), bad = RawText(roles,"bad",32) };
                    IDictionary<string, object> death = Get(item,"death") as IDictionary<string, object>;
                    if (death != null) p.death = new Death { round = (int)Math.Floor(RawNumber(Get(death,"round"),1,1,99)+.5), cause = RawText(death,"cause",32) };
                    state.session.players.Add(p);
                }
            }
            IDictionary<string, object> prefs = Object(Get(source,"preferences")), features = Object(Get(prefs,"features"));
            state.preferences = new Preferences {
                playerCount = (int)Math.Floor(RawNumber(Get(prefs,"playerCount"),16,1,16)+.5), opacity = RawNumber(Get(prefs,"opacity"),68,15,95),
                features = new Features { roles = !System.Object.Equals(Get(features,"roles"), false), notes = !System.Object.Equals(Get(features,"notes"), false), death = !System.Object.Equals(Get(features,"death"), false) }
            };
            foreach (KeyValuePair<string, object> profile in Object(Get(source,"profiles")))
                if (profile.Key.Length < 200 && state.profiles.Count < 100) state.profiles[profile.Key] = LayoutFromObject(profile.Value);
            state.selectedDisplay = RawText(source,"selectedDisplay",200);
            object lx = Get(source,"launcherX"), ly = Get(source,"launcherY");
            if (lx != null) { double x = RawNumber(lx, Double.NaN, -100000,100000); if (!Double.IsNaN(x)) state.launcherX = (int)Math.Floor(x+.5); }
            if (ly != null) { double y = RawNumber(ly, Double.NaN, -100000,100000); if (!Double.IsNaN(y)) state.launcherY = (int)Math.Floor(y+.5); }
            return Normalize(state);
        }
        private static Layout LayoutFromObject(object raw)
        {
            IDictionary<string,object> source = Object(raw); Layout layout = CreateLayout();
            layout.aspect = RawText(source,"aspect",20);
            layout.offsetX = RawNumber(Get(source,"offsetX"),0,-1,1); layout.offsetY = RawNumber(Get(source,"offsetY"),0,-1,1);
            layout.scale = RawNumber(Get(source,"scale"),1,.35,2); layout.cardScale = RawNumber(Get(source,"cardScale"),1,.6,1.8);
            IList positions = Get(source,"positions") as IList;
            if (positions != null) for (int i=0;i<Math.Min(PlayerCount,positions.Count);i++) {
                IDictionary<string,object> position = Object(positions[i]);
                layout.positions[i] = new Position(RawNumber(Get(position,"x"),layout.positions[i].x,-8,9), RawNumber(Get(position,"y"),layout.positions[i].y,-8,9));
            }
            return NormalizeLayout(layout);
        }
    }
}
