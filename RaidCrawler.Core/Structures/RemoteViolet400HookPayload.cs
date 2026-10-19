using System.Buffers.Binary;
using System.Security.Cryptography;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Exact Violet 4.0.0 880-byte trampoline layout assembled offline from stub.S.
/// Initial mode is odd/disarmed. Windows controls it through existing sys-botbase.
/// The code is not portable to other title versions and not evidence of a hit.
/// </summary>
public static class RemoteViolet400HookPayload
{
    public const int TotalBytes = 880;
    public const int CodeBytes = 0x90;
    public const int DataOffset = 0xA0;
    public const uint OriginalInstruction = 0xAA1503E8;
    public const string CodeSha256 =
        "7C54A5CA250A1683F5D41AD6DD8FC928D83C7E94494FDAFD1E1DA18CDA9158B8";

    // Generated from clang --target=aarch64-none-elf stub.S; the +0x8C
    // slot already contains the branch back to main+0xE32988.
    // Do not edit by hand. CI cross-checks this against stub.S output.
    private static readonly byte[] Code = Convert.FromHexString(
        "E92BBFA9EB33BFA9ED3BBFA9290400102EFDDF884E030037EA5E43F94D2A8CD2" +
        "2D8DACF20DEACDF22DCDEDF25F010DEB610200546B030010EC721E91AD058052" +
        "6929C1A8892981A8AD050071A1FFFF54090200102AFDDF885F010E6B00010054" +
        "8BF20491EC721E91AD0580526929C1A8892981A8AD050071A1FFFF54ED3BC1A8" +
        "EB33C1A8E92BC1A8E80315AA1BCB6717");

    private static readonly byte[] Marker = "RHS2RaidPoin"u8.ToArray();

    public static ReadOnlySpan<byte> CodeSpan => Code;

    public static uint EntryBranch => Branch(
        NativeRaidCodeFingerprint.SerializerHookRva,
        NativeRaidCodeFingerprint.EndOfTextPaddingRva);

    private static uint Branch(ulong from, ulong to)
    {
        long diff = (long)to - (long)from;
        if (diff % 4 != 0 || diff is < -(1L << 27) or >= (1L << 27))
            throw new InvalidOperationException("AArch64 B out of range.");
        return 0x14000000U | (uint)((diff / 4) & 0x03FFFFFF);
    }

    public static byte[] MakeDisarmed(ReadOnlySpan<byte> rewardRows)
    {
        if (rewardRows.Length != RaidRewardEditor.RewardBytes)
            throw new ArgumentException("Requires 720-byte reward template.");
        RaidRewardEditor.DecodeSlots(rewardRows);
        if (Code.Length != CodeBytes ||
            !Convert.ToHexString(SHA256.HashData(Code)).Equals(
                CodeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trampoline code fingerprint mismatch.");

        byte[] result = new byte[TotalBytes];
        Code.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x90), 1);
        Marker.CopyTo(result, 0x94);
        rewardRows.CopyTo(result.AsSpan(DataOffset));
        return result;
    }

    public static bool MatchesReader(ReadOnlySpan<byte> image)
    {
        return image.Length >= CodeBytes && image[..CodeBytes].SequenceEqual(Code);
    }

    public static bool MatchesHeader(ReadOnlySpan<byte> header)
    {
        return header.Length == 16 &&
            header[4..].SequenceEqual(Marker) &&
            BinaryPrimitives.ReadUInt32LittleEndian(header) is
                >= RemoteRewardEpochUpdater.MinEpoch and
                <= RemoteRewardEpochUpdater.MaxEpoch;
    }
}
