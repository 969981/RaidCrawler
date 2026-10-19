using System.Globalization;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms;

/// <summary>
/// Display-only localization. Never changes numeric reward IDs, Subject or PokéLDN command parameters.
/// PKHeX.Core's GameInfo.GetStrings(language) supplies real Pokémon/item/move translations.
/// </summary>
public static class BenefitLanguage
{
    public static bool Chinese { get; private set; } = true;
    public static string Code => Chinese ? "zh-Hans" : "en";
    public static void Configure(string? language)
    {
        Chinese = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string zh, string en) => Chinese ? zh : en;

    public sealed record Choice(int Value, string Label);
    public sealed record ProgressChoice(string Value, string Label);

    public static IReadOnlyList<Choice> RewardSubjects => [
        new(0, T("0－所有玩家", "0 - Everyone")),
        new(1, T("1－房主", "1 - Host")),
        new(2, T("2－参与者", "2 - Guest")),
        new(3, T("3－一次性奖励", "3 - Once eligible")),
        new(4, T("4－团体战料理", "4 - Raid Power")),
        new(5, T("5－晶耀护符", "5 - Glimmering Charm")),
    ];

    public static IReadOnlyList<ProgressChoice> Progress => [
        new("beginning", T("初始阶段", "Beginning")),
        new("tera", T("解锁太晶团战", "Tera unlocked")),
        new("3star", T("解锁三星", "3-star unlocked")),
        new("4star", T("解锁四星", "4-star unlocked")),
        new("5star", T("解锁五星", "5-star unlocked")),
        new("6star", T("解锁六星", "6-star unlocked")),
    ];

    public static string Map(string value) => value switch
    {
        "paldea" => T("帕底亚", "Paldea"),
        "kitakami" => T("北上乡", "Kitakami"),
        "blueberry" => T("蓝莓学院", "Blueberry"),
        _ => value,
    };
    public static string Crystal(string value) => value switch
    {
        "standard" => T("普通坑", "Standard"),
        "black" => T("黑色结晶", "Black crystal"),
        _ => value,
    };
    public static string Version(string value) => value switch
    {
        "scarlet" => T("朱", "Scarlet"),
        "violet" => T("紫", "Violet"),
        _ => value,
    };

    // Main window designer uses English defaults; localize the existing named controls without forking its designer.
    public static void ApplyMain(Form window)
    {
        if (!Chinese) return;
        var values = new Dictionary<string, string> {
            ["ButtonAdvanceDate"] = "推进日期",
            ["CheckEnableFilters"] = "启用筛选",
            ["ButtonDisconnect"] = "断开连接",
            ["ButtonConnect"] = "连接",
            ["LabelSwitchIP"] = "Switch IP：",
            ["LabelLoadedRaids"] = "匹配：0",
            ["LabelTeraType"] = "太晶属性：",
            ["LabelSeed"] = "种子：",
            ["LabelUNK_2"] = "地点：",
            ["LabelIVs"] = "个体值：",
            ["ButtonReadRaids"] = "读取太晶坑",
            ["labelEvent"] = "～～活动～～",
            ["LabelDifficulty"] = "星级：",
            ["ButtonViewRAM"] = "导出坑位",
            ["LabelSpecies"] = "宝可梦：",
            ["LabelMoves"] = "招式：",
            ["LabelNature"] = "性格：",
            ["LabelGender"] = "性别：",
            ["StopFilter"] = "编辑筛选",
            ["LabelAbility"] = "特性：",
            ["ButtonDownloadEvents"] = "拉取活动",
            ["ConfigSettings"] = "打开设置",
            ["Rewards"] = "奖励编辑",
            ["LabelSandwichBonus"] = "团体战料理加成：",
            ["SendScreenshot"] = "截图",
            ["btnOpenMap"] = "打开地图",
            ["groupBox1"] = "太晶坑控制",
            ["StatusLabel"] = "状态：",
            ["ToolStripStatusLabel"] = "未连接。",
            ["USB_Port_label"] = "USB 端口：",
            ["StopAdvance_Button"] = "停止",
            ["B_DateTools"] = "日期工具",
            ["Label_DayAdvance"] = "日期推进成功：0 / 0",
        };
        static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        foreach (var control in Descendants(window))
            if (values.TryGetValue(control.Name, out var text)) control.Text = text;

        // ToolStrip items are not Control descendants.
        foreach (var strip in Descendants(window).OfType<StatusStrip>())
            foreach (ToolStripItem item in strip.Items)
                if (values.TryGetValue(item.Name, out var text)) item.Text = text;
    }

    public static string Status(string source)
    {
        if (!Chinese) return source;
        return source switch
        {
            "Not connected." => "未连接。",
            "Completed!" => "完成！",
            "Reading raids..." => "正在读取太晶坑……",
            "Reading event raid status..." => "正在读取活动坑状态……",
            "Reading story progress..." => "正在读取游戏进度……",
            "Detecting game version..." => "正在检测游戏版本……",
            "Changing date..." => "正在调整日期……",
            "Date advance stopped." => "日期推进已停止。",
            _ => source,
        };
    }
}
