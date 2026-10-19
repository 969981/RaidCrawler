namespace RaidCrawler.Core.Structures;

/// <summary>
/// Offline fixture patcher for the two *uncompressed* SV formats. It NEVER touches
/// SysBot, a running Switch, an official online lobby or compressed Pia/LDN packets.
/// A future host-side runtime hook must separately validate game build, role, call
/// lifetime and outgoing message before any real injection may be considered.
/// </summary>
public static class RaidPointOfflinePatch
{
    public const int RaidPointSize = 0x3E8;
    public const int BootstrapSize = 0xAA0;
    public const int BootstrapPointOffset = 0x6B8;
    public const int RewardOffset = 0xE4;
    private static ReadOnlySpan<byte> Magic => "RaidPoint_"u8;

    public static byte[] PatchRaidPoint(ReadOnlySpan<byte> original, IReadOnlyList<RaidRewardEntry?> slots) =>
        Patch(original, 0, RaidPointSize, slots);

    public static byte[] PatchBootstrap(ReadOnlySpan<byte> original, IReadOnlyList<RaidRewardEntry?> slots) =>
        Patch(original, BootstrapPointOffset, BootstrapSize, slots);

    private static byte[] Patch(ReadOnlySpan<byte> original, int pointOffset, int totalSize,
        IReadOnlyList<RaidRewardEntry?> slots)
    {
        if (original.Length != totalSize)
            throw new ArgumentException($"Expected exactly {totalSize} uncompressed bytes.");
        if (!original.Slice(pointOffset, Magic.Length).SequenceEqual(Magic))
            throw new ArgumentException("Input is not a RaidPoint_ object at the expected offset.");
        byte[] encoded = RaidRewardEditor.EncodeSlots(slots);
        var result = original.ToArray();
        encoded.CopyTo(result.AsSpan(pointOffset + RewardOffset));
        return result;
    }

    public static bool UntouchedFieldsEqual(ReadOnlySpan<byte> original, ReadOnlySpan<byte> patched, int pointOffset)
    {
        if (original.Length != patched.Length || pointOffset < 0 ||
            pointOffset + RaidPointSize > original.Length) return false;
        int begin = pointOffset + RewardOffset;
        int end = begin + RaidRewardEditor.RewardBytes;
        return original[..begin].SequenceEqual(patched[..begin]) &&
               original[end..].SequenceEqual(patched[end..]);
    }
}
