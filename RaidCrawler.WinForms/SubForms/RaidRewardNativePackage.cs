using System.Text;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Makes the EXACT native-plugin input package. It does not copy over
/// the network or silently arm a Hook on the console.
/// </summary>
internal static class RaidRewardNativePackage
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidCrawler", "native-hook");

    public static string Save(IReadOnlyList<RaidRewardEntry?> slots)
    {
        byte[] data = RaidRewardEditor.EncodeSlots(slots);
        Directory.CreateDirectory(Folder);
        string destination = Path.Combine(Folder, "rewards.bin");
        string temp = destination+".tmp";
        try
        {
            File.WriteAllBytes(temp, data);
            File.Move(temp, destination, overwrite:true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }

        string modePath = Path.Combine(Folder,"hook-mode.txt");
        // Never auto-arm executable patches on the user's hardware.
        if (!File.Exists(modePath)) File.WriteAllText(modePath, "observe\n");
        File.WriteAllText(Path.Combine(Folder, "README-复制到Switch.txt"),
            "RaidCrawler Switch 原生 Hook（仅《紫》4.0.0）\r\n\r\n"+
            "1. 复制 rewards.bin 与 hook-mode.txt 到 Switch SD 卡 /switch/raidcrawler/。\r\n"+
            "2. 安装已编译的 skyline 原生插件（需要支持的 Skyline/Atmosphere 环境）。\r\n"+
            "3. hook-mode.txt 默认 observe：仅观察，不修改任何奖励。\r\n"+
            "4. 确认 ELF 指纹和 HOOK_HIT 后，如要进行授权实验，改为 apply 并重启游戏。\r\n"+
            "5. 查看 SD 卡 /switch/raidcrawler/hook.log。\r\n"+
            "注意：支持的 ELF 仅《紫》4.0.0；当前没有实机端到端发奖验收。\r\n");
        return destination;
    }
}
