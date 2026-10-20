using PKHeX.Core;

namespace RaidCrawler.Core.Structures;

public static class Areas
{
    private static readonly string[] AreaList =
    [
        "南第1区",
        "", // Unused
        "", // Unused
        "南第2区",
        "南第4区",
        "南第6区",
        "南第5区",
        "南第3区",
        "西第1区",
        "烘烘沙漠",
        "西第2区",
        "西第3区",
        "印记树林",
        "东第3区",
        "东第1区",
        "东第2区",
        "锦穴山道",
        "大锅湖",
        "霜抹山",
        "北第3区",
        "北第1区",
        "北第2区",
    ];

    private static readonly string[] AreaListKitakami =
    [
        "北上公路",
        "苹果之丘",
        "Reveler's Road",
        "鬼山",
        "地狱谷",
        "晶光池",
        "思念原野",
        "苔藓河汇流处",
        "鬼角峡谷",
        "乐土荒地",
        "永恒森林",
    ];

    private static readonly string[] AreaListBlueberry =
    [
        "草原区",
        "海岸区",
        "峡谷区",
        "极地区",
        "草原区",
        "海岸区",
        "峡谷区",
        "极地区",
    ];

    public static string GetArea(int index, TeraRaidMapParent type) => type switch
    {
        TeraRaidMapParent.Kitakami => AreaListKitakami[index],
        TeraRaidMapParent.Blueberry => AreaListBlueberry[index],
        _ => AreaList[index],
    };
}
