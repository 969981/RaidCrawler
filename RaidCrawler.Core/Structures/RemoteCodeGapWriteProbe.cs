namespace RaidCrawler.Core.Structures;

/// <summary>
/// Tests only the existing sys-botbase read/write transport on an UNUSED
/// four-byte word inside Violet 4.0.0's zero-filled tail of the text mapping.
/// Does not install a branch, execute injected code or establish executable
/// permission. All callers MUST verify title, version, Build ID prefix,
/// all opcode anchors, and unchanged main NSO base first.
/// </summary>
public static class RemoteCodeGapWriteProbe
{
    public const ulong GapRva = NativeRaidCodeFingerprint.EndOfTextPaddingRva;
    public const int GapLength = NativeRaidCodeFingerprint.EndOfTextPaddingLength;
    public const ulong ProbeRva = GapRva + 0x30;
    // ARM64 NOP. If someone accidentally executes this word it is not a trap.
    private static readonly byte[] Nop = [0x1F, 0x20, 0x03, 0xD5];
    private static readonly byte[] Original = new byte[4];

    public sealed record Result(bool Accepted, bool WriteVerified, bool RestoreVerified, string Reason);

    public static async Task<Result> RunAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> read,
        Func<byte[], ulong, CancellationToken, Task> write,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        token.ThrowIfCancellationRequested();

        // No modification of a nonzero or occupied code cave is permitted.
        byte[] before = await read(GapRva, GapLength, token).ConfigureAwait(false);
        if (before.Length != GapLength || Array.Exists(before, b => b != 0))
            return new(false, false, false,
                "候选区域不再是完整的 880B 全零区域：拒绝代码写入。");

        bool written = false;
        bool restored = false;
        string reason = "4B ARM64 NOP 写入、读回与恢复完成；尚未验证执行权限，也没有安装 Hook。";
        try
        {
            // Mark attempted BEFORE awaiting network IO: even a failed write
            // might have reached the console. Always attempt rollback.
            await write((byte[])Nop.Clone(), ProbeRva, token).ConfigureAwait(false);
            var observed = await read(ProbeRva, Nop.Length, token).ConfigureAwait(false);
            written = observed.AsSpan().SequenceEqual(Nop);
            if (!written)
                reason = "远程写入后读回不匹配；将尝试恢复原始 4B。";
        }
        catch (Exception ex)
        {
            reason = "写入或读回异常 (" + ex.GetType().Name + ")；将尝试恢复原始 4B。";
        }
        finally
        {
            // Never inherit the cancelled dialog token for rollback.
            // A fresh bounded token gives the debugger one last restore chance.
            using var restoreDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await write((byte[])Original.Clone(), ProbeRva, restoreDeadline.Token)
                    .ConfigureAwait(false);
                var after = await read(ProbeRva, 4, restoreDeadline.Token)
                    .ConfigureAwait(false);
                restored = after.AsSpan().SequenceEqual(Original);
            }
            catch (Exception ex)
            {
                reason += " 恢复异常：" + ex.GetType().Name + "。";
            }
        }
        if (!restored)
            return new(true, written, false,
                reason + " 警告：未能验证恢复；应重启游戏并重新检查全零区域。");
        return new(true, written, true, written ? reason :
            reason + " 已验证原始 4B 恢复。");
    }
}
