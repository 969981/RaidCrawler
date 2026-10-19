using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Violet 4.0.0 serializer instruction fingerprint. Exact RVA/words were
/// cross-checked against the matching retail ELF. Static instructions do
/// NOT prove that a battle-build callback was executed.
/// </summary>
public static class NativeRaidCodeFingerprint
{
    // The ELF's RX .text p_memsz ends here, and the following R .rodata starts
    // at 0x3440000. Padding MAY be mapped RX at runtime; do not assume it is!
    public const ulong EndOfTextPaddingRva = 0x343FC90;
    public const int EndOfTextPaddingLength = 0x370;
    public const ulong NextReadOnlySegmentRva = 0x3440000;
    public const ulong SerializerHookRva = 0xE32984;
    // Verified against the user's exact ELF SHA256:
    // 0x343FC8C is the last executable PLT instruction (br x17).
    // The zero-filled 880B candidate is the tail of the same 4KiB page.
    // Same-page layout is static evidence, NOT a Switch runtime X-permission query.
    public const ulong LastPltInstructionRva = 0x343FC8C;
    public const uint LastPltInstructionOpcode = 0xD61F0220;
    public const ulong TextPageBytes = 0x1000;
    public static bool CandidateSharesLastPltPage =>
        (LastPltInstructionRva / TextPageBytes) ==
        (EndOfTextPaddingRva / TextPageBytes) &&
        EndOfTextPaddingRva + (ulong)EndOfTextPaddingLength ==
        NextReadOnlySegmentRva;

    public readonly record struct Anchor(ulong Rva, uint AArch64Opcode, string Meaning);

    public static IReadOnlyList<Anchor> Violet400 => [
        new(0xE32814, 0xAA0003F4, "mov x20, x0 -- serializer input"),
        new(0xE32828, 0x52815416, "mov w22,#0xAA0 -- plain length"),
        new(0xE3283C, 0xAA0003F7, "mov x23,x0 -- plain allocation"),
        new(0xE32968, 0x9101E281, "source RaidPoint +0x78"),
        new(0xE3296C, 0x52807902, "copy tail 0x3C8"),
        new(0xE32970, 0xF9035EEB, "output RaidPoint +0x6B8"),
        new(0xE32980, 0x94982624, "memcpy tail"),
        new(0xE32984, 0xAA1503E8, "copy complete, before serializer tailcall"),
    ];

    public static (bool Matches, uint? Actual) Check(Anchor anchor, ReadOnlySpan<byte> data)
    {
        if (data.Length != 4) return (false, null);
        uint actual = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return (actual == anchor.AArch64Opcode, actual);
    }
}
