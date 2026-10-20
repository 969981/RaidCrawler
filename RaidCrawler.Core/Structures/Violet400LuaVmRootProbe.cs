using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Read-only, exact-build static pointer-chain *candidate* probe.
/// The global slot and field displacements were recovered from Violet 4.0.0
/// AArch64. Neither pointer stability nor successful reads attest an actual
/// Lua call, active closure, decoded Table, hook hit, or reward delivery.
/// Caller MUST verify TitleID, version, BuildID, and NSO base before use.
/// </summary>
public static class Violet400LuaVmRootProbe
{
    public const ulong GlobalReferenceSlotRva = 0x4759EB0;
    public const ulong ContextFromReference = 0x88;
    public const ulong LuaStateFieldFromContext = 0x70;
    public const ulong LuaStateFieldBelowReference =
        ContextFromReference - LuaStateFieldFromContext; // 0x18

    public sealed record Observation(
        string Status,
        bool GlobalReferencePresent,
        bool LuaStateCandidatePresent,
        bool StableAcrossReads,
        ulong? GlobalReference = null,
        ulong? LuaStateCandidate = null,
        string RuntimeVerification = "NOT_ATTESTED_BY_POINTER_READ",
        string HookCpuHit = "NOT_OBSERVED",
        string AbcRuntimeCapture = "NOT_OBSERVED")
    {
        public bool ReadOnly => true;
        public bool VmStateConfirmed => false;
    }

    private static bool PlausiblePointer(ulong value) =>
        value >= 0x10000UL && value < (1UL << 48) && (value & 7UL) == 0;

    private static ulong U64(byte[] bytes) =>
        bytes.Length == 8
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : throw new InvalidDataException("Expected exactly 8 bytes from Switch read.");

    /// <summary>
    /// This does not access the Switch until the caller supplies the two
    /// separate read-only functions. No scanning and no memory writes.
    /// Read location of the proposed state: *( *(main + 0x4759EB0) - 0x18).
    /// </summary>
    public static async Task<Observation> ProbeAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> readMain,
        Func<ulong, int, CancellationToken, Task<byte[]>> readAbsolute,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(readMain);
        ArgumentNullException.ThrowIfNull(readAbsolute);
        token.ThrowIfCancellationRequested();

        ulong reference = U64(await readMain(GlobalReferenceSlotRva, 8, token));
        if (reference == 0)
            return new("GLOBAL_SLOT_NULL", false, false, false);
        if (!PlausiblePointer(reference) ||
            reference < LuaStateFieldBelowReference + 0x10000UL)
            return new("GLOBAL_SLOT_INVALID_POINTER", false, false, false);

        ulong fieldAddress = reference - LuaStateFieldBelowReference;
        ulong state1 = U64(await readAbsolute(fieldAddress, 8, token));
        if (state1 == 0)
            return new("LUA_STATE_FIELD_NULL", true, false, false, reference);
        if (!PlausiblePointer(state1))
            return new("LUA_STATE_FIELD_INVALID_POINTER", true, false, false, reference);

        // Recheck after the potentially racing read. A zero/changed pointer
        // never permits treating a stale candidate as a stable observation.
        ulong state2 = U64(await readAbsolute(fieldAddress, 8, token));
        ulong reference2 = U64(await readMain(GlobalReferenceSlotRva, 8, token));
        if (reference != reference2 || state1 != state2)
            return new("POINTER_CHANGED_DURING_SAMPLE", true, true, false);

        return new("CANDIDATE_POINTER_STABLE_NOT_VM_VERIFIED",
            true, true, true, reference, state1);
    }
}
