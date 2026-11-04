using System;
using System.Collections.Generic;

namespace GgdHelp
{
    // Steam / international PC roles, checked against official announcements
    // through 2026-11-04. Map and Corruption restrictions: docs/roles.md.
    public static class RoleCatalog
    {
        // The order matches Model.RoleKeys: good, neutral, bad.
        // Keep familiar roles first; aliases belong below, never in the menus.
        public static readonly string[][] Groups = {
            new[] {
                "鹅", "肉汁", "正义使者", "警长", "加拿大鹅", "殡仪员", "网红",
                "模仿者", "侦探", "工程师", "复仇者", "观鸟者", "保镖", "政治家",
                "锁匠", "冒险家", "星界行者", "流浪儿童", "追踪者", "跟踪者",
                "通灵者", "恋人（鹅）", "求生者", "说客", "预言家", "科学家",
                "探测员", "验尸官", "士兵", "清醒梦者",
                "审判官", "恶魔猎手", "圣徒", "女裁缝"
            },
            new[] {
                "呆呆鸟", "秃鹫", "鹈鹕", "鸽子", "猎鹰", "决斗呆呆鸟",
                "老鹰", "渡鸦", "布谷鸟", "喜鹊", "乌鸦"
            },
            new[] {
                "鸭子", "丧葬者", "隐形者", "食鸟鸭", "变形者", "静语者",
                "专业杀手", "间谍", "刺客", "雇佣杀手", "告密者", "派对狂",
                "爆炸王", "身份窃贼", "忍者", "连环杀手", "术士", "超能力者",
                "恋人（鸭）", "传教士", "丘比特", "异形", "哑剧演员", "小丑鸭",
                "掠夺者", "狙击手", "巫医", "走失小鸭", "宿主", "寄生者", "剑客鸭",
                "大祭司", "新信徒", "食罪者"
            }
        };

        private static readonly string[] CampKeys = { "good", "neutral", "bad" };
        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "好人", "鹅" }, { "大白鹅", "鹅" }, { "白鹅", "鹅" },
                { "坏比", "鸭子" }, { "坏人", "鸭子" }, { "鸭", "鸭子" },
                // The former neutral placeholder is a camp, not a role.
                { "中立", "" },
                { "加拿大", "加拿大鹅" }, { "模仿", "模仿者" },
                { "复仇", "复仇者" }, { "观鸟", "观鸟者" },
                { "隐形", "隐形者" }, { "食鸭鸟", "食鸟鸭" },
                { "超能力", "超能力者" }, { "呆呆", "呆呆鸟" },
                { "决斗呆呆", "决斗呆呆鸟" }, { "决斗呆", "决斗呆呆鸟" },
                { "恋人鹅", "恋人（鹅）" }, { "恋人(鹅)", "恋人（鹅）" },
                { "恋人鸭", "恋人（鸭）" }, { "恋人(鸭)", "恋人（鸭）" },
                { "生存家", "求生者" }, { "传教士鸭", "传教士" },
                { "丘比特鸭", "丘比特" }, { "异形鸭", "异形" },
                { "Drone", "异形" },
                { "默剧演员", "哑剧演员" }, { "哑剧鸭", "哑剧演员" },
                { "小丑", "小丑鸭" }, { "迷途小鸭", "走失小鸭" },
                { "新教徒", "新信徒" }, { "食罪者鸭", "食罪者" }
            };

        public static string NormalizeName(string role)
        {
            if (String.IsNullOrWhiteSpace(role)) return "";
            string name = role.Trim();
            string canonical;
            return Aliases.TryGetValue(name, out canonical) ? canonical : name;
        }

        public static string InferCamp(string role)
        {
            // Infer this legacy placeholder before NormalizeName removes it.
            if (role != null && role.Trim() == "中立") return "neutral";
            string name = NormalizeName(role);
            if (name.Length == 0) return "";
            for (int group = 0; group < Groups.Length; group++)
                if (Array.IndexOf(Groups[group], name) >= 0) return CampKeys[group];
            return "";
        }
    }
}
