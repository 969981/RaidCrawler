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

    public static void ApplyConfig(Form window)
    {
        if (!Chinese) return;
        var names = new Dictionary<string, string> {
            ["tabGeneral"] = "常规",
            ["tabMatch"] = "筛选与匹配",
            ["tabAdvanceDate"] = "日期推进",
            ["tabWebhook"] = "通知",
            ["tabExperimental"] = "实验功能",
            ["tabAbout"] = "关于",
            ["FocusWindow"] = "匹配时切到窗口",
            ["EnableAlert"] = "匹配时显示提示窗口",
            ["PlayTone"] = "匹配时播放提示音",
            ["LabelMatchFound"] = "找到匹配结果时：",
            ["L_AdvanceDate"] = "日期推进设置（毫秒）：",
            ["L_BaseDelay"] = "输入操作的基础延迟：",
            ["L_DdownInput"] = "向下按键次数：",
            ["L_OpenHOME"] = "打开 HOME 菜单延迟：",
            ["L_NavigateSettings"] = "进入系统设置延迟：",
            ["L_OpenSettingsDelay"] = "打开设置延迟：",
            ["L_ScrollSystem"] = "滚动到“主机”项目持续时间：",
            ["L_SubmenuDelay"] = "打开子菜单延迟：",
            ["L_DateChangeDelay"] = "打开日期修改延迟：",
            ["L_ReturnHomeDelay"] = "返回 HOME 菜单延迟：",
            ["L_ReOpenGameDelay"] = "重新进入游戏延迟：",
            ["L_DaysToSkip"] = "需要推进的天数／月份：",
            ["UseTouch"] = "使用触屏输入（实验性）",
            ["EnableDiscordNotifications"] = "发送 Discord Webhook 通知",
            ["label13"] = "实验性设置：",
            ["ExperimentalView"] = "显示直播用太晶坑视图",
            ["LocationGroup"] = "扫描区域",
            ["KitakamiScanCheck"] = "北上乡",
            ["PaldeaScanCheck"] = "帕底亚",
            ["BlueberryScanCheck"] = "蓝莓学院",
            ["Protocol_label"] = "连接协议：",
            ["label23"] = "程序设置",
            ["LabelEventProgress"] = "活动进度：",
            ["LabelGame"] = "游戏版本：",
            ["LabelStoryProgress"] = "主线进度：",
            ["ZyroMethod"] = "使用 Zyro 的 usb-botbase 方案",
            ["UseMapTrick"] = "使用地图技巧减少卡顿",
            ["L_ExtraOverworldWait"] = "等待地图加载的额外时长：",
            ["L_RelaunchDelay"] = "重新启动游戏的额外等待：",
            ["L_SystemReset"] = "推进多少次后重启游戏：",
            ["UseSetStick"] = "使用 SetStick 操作",
            ["DodgeSystemUpdate"] = "跳过系统更新提示",
            ["L_SaveGame"] = "等待游戏存档的时间：",
            ["SaveGame"] = "找到匹配结果后存档",
            ["L_OvershootHold"] = "按住按键跳过目标项的时长：",
            ["UseOvershoot"] = "使用越界跳转方式（实验性）",
            ["EmojiConfig"] = "表情配置",
            ["labelWebhooks"] = "Webhook 状态",
            ["label21"] = "常规设置",
            ["label14"] = "消息内容（可 @ 用户）",
            ["btnTestWebHook"] = "测试 Webhook",
            ["denToggle"] = "显示坑位",
            ["LocationSettings_label"] = "地点设置",
            ["IVverbose"] = "详细个体值",
            ["label19"] = "个体值显示风格",
            ["label18"] = "个体值设置",
            ["EnableEmoji"] = "启用表情",
            ["label17"] = "实例名称：",
        };
        static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var item in Descendants(child)) yield return item;
            }
        }
        foreach (Control control in Descendants(window))
            if (names.TryGetValue(control.Name, out var title)) control.Text = title;
        window.Text = "RaidCrawler 设置";
    }

    public static string Error(string source)
    {
        if (!Chinese) return source;
        // Only translate known presentation-time validation failures; keep original details for debugging.
        if (source.Contains("RaidPoint supports at most"))
            return "奖励数量超过 45 行上限。";
        if (source.Contains("use a real item ID") || source.Contains("specify an actual item ID"))
            return "道具 ID 必须为 1～65535 的真实道具编号，不能使用计算预览占位 ID。";
        if (source.Contains("quantity must be") || source.Contains("quantity must be between"))
            return "每行道具数量必须为 1～999。";
        if (source.Contains("rare flag must be") || source.Contains("rare must be"))
            return "稀有标记仅可为 0 或 1。";
        if (source.Contains("subject must be"))
            return "发放条件 Subject 仅可选择 0～5。";
        if (source.Contains("PokéLDN custom rewards support only") ||
            source.Contains("PokéLDN currently accepts custom"))
            return "当前 PokéLDN 自定义主持仅支持 Subject=0（全员）或 5（晶耀护符），且 Rare 必须为 0。";
        if (source.Contains("Event raids require"))
            return "活动坑必须提供匹配的活动配信数据，本版不支持把活动坑作为普通坑主持。";
        return source;
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
