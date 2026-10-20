using System.Buffers.Binary;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// P1-B2: bounded READ-ONLY Lua 5.4 main-state / CallInfo structural sampling
/// for exact Pokémon Violet 4.0.0. This is NOT a VM callback, breakpoint,
/// Lua closure identity proof, reward hook, or A/B/C data capture.
/// </summary>
public static class Violet400LuaVmFrameProbe
{
    public const int HeaderBytes = 0x70;
    public const int CallInfoBytes = 0x20;
    public const int TValueBytes = 0x10;
    public const ulong MainGlobalOffset = 0xC8;
    public const ulong BaseCallInfoOffset = 0x60;
    public const byte LuaThreadObjectTag = 0x08;
    // Standard Lua 5.4 TValue tag, with bit 6 marking a collectable object.
    // The pointer behind a plausible tag is deliberately not dereferenced yet.
    public const byte LuaClosureTValueTag = 0x66;

    public sealed record Observation(
        string Status,
        string RootStatus,
        bool MainStateLayoutMatched,
        bool StackPointersPlausible,
        bool ActiveCallInfoCandidate,
        bool LuaClosureValueTagObserved,
        string FunctionSlotClassification,
        bool SnapshotStable,
        string RuntimeCallHit = "NOT_OBSERVED",
        string PrototypeIdentity = "NOT_VERIFIED",
        string AbcRewards = "NOT_CAPTURED",
        IReadOnlyList<string>? ChangedFields = null,
        int ReadAttempts = 1)
    {
        public bool ReadOnly => true;
        public bool ClosureIdentityVerified => false;
        public bool RewardHookInstalled => false;
    }

    private static bool Pointer(ulong value, ulong alignment = 8) =>
        value >= 0x10000 && value < (1UL << 48) && value % alignment == 0;

    private static ulong Q(ReadOnlySpan<byte> b, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(offset, 8));

    private static bool InStack(ulong ptr, ulong low, ulong last) =>
        Pointer(ptr, 16) && ptr >= low && last < ulong.MaxValue - 0x100 &&
        ptr <= last + 0x100;

    private static Observation Fail(string why, string rootStatus,
        bool layout = false, bool stack = false, bool active = false) =>
        new(why, rootStatus, layout, stack, active, false, "NOT_OBSERVED", false);

    /// <summary>
    /// Requires externally verified TitleID/BuildID/version and unchanged
    /// MainNSOBase. This method reads, but NEVER writes, memory.
    /// At most 4+1+2+1+2 = 10 bounded remote reads:
    /// root chain (4), state header (1), current CI / TValue (2),
    /// state header recheck (1), root chain recheck (2).
    /// No heap scan, no GC object or closure pointer traversal.
    /// </summary>
    public static async Task<Observation> ProbeAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> readMain,
        Func<ulong, int, CancellationToken, Task<byte[]>> readAbsolute,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(readMain);
        ArgumentNullException.ThrowIfNull(readAbsolute);
        token.ThrowIfCancellationRequested();
        var root = await Violet400LuaVmRootProbe.ProbeAsync(readMain, readAbsolute, token);
        if (!root.StableAcrossReads || root.LuaStateCandidate is not ulong state)
            return Fail("ROOT_NOT_STABLE_OR_NULL", root.Status);

        // This is a bounded read of a previously version-locked candidate.
        byte[] first = await readAbsolute(state, HeaderBytes, token);
        if (first.Length != HeaderBytes)
            return Fail("STATE_HEADER_SHORT_READ", root.Status);
        if (first[8] != LuaThreadObjectTag || !Pointer(Q(first, 0x18)) ||
            Q(first, 0x18) != state + MainGlobalOffset)
            return Fail("MAIN_STATE_LAYOUT_MISMATCH", root.Status);
        ulong stack = Q(first, 0x30), last = Q(first, 0x28);
        ulong top = Q(first, 0x10), tbclist = Q(first, 0x40);
        ulong ci = Q(first, 0x20);
        ulong baseFunc = Q(first, 0x60), baseTop = Q(first, 0x68);
        bool validStack = Pointer(stack, 16) && Pointer(last, 16) &&
            last >= stack && last - stack <= 0x1000000 &&
            InStack(top, stack, last) && InStack(tbclist, stack, last) &&
            InStack(baseFunc, stack, last) && InStack(baseTop, stack, last) &&
            Pointer(ci);
        if (!validStack)
            return Fail("STACK_OR_BASE_CI_LAYOUT_MISMATCH", root.Status, layout:true);

        bool active = ci != state + BaseCallInfoOffset;
        bool luaClosureTag = false;
        string functionSlot = active ? "UNRESOLVED" : "BASE_CI_IDLE";
        if (active)
        {
            byte[] frame = await readAbsolute(ci, CallInfoBytes, token);
            if (frame.Length != CallInfoBytes)
                return Fail("CALLINFO_SHORT_READ", root.Status, true, true, true);
            ulong func = Q(frame, 0), frameTop = Q(frame, 8);
            if (!InStack(func, stack, last) || !InStack(frameTop, stack, last) ||
                frameTop < func)
                return Fail("CALLINFO_FRAME_RANGE_MISMATCH", root.Status, true, true, true);
            byte[] funcSlot = await readAbsolute(func, TValueBytes, token);
            if (funcSlot.Length != TValueBytes)
                return Fail("FUNCTION_TVALUE_SHORT_READ", root.Status, true, true, true);
            // A value-type hint, NOT a verified closure pointer or Proto.
            int tag = funcSlot[8];
            functionSlot = tag switch
            {
                LuaClosureTValueTag => "LUA_CLOSURE_TVALUE_TAG_CANDIDATE",
                0x56 => "C_CLOSURE_TVALUE_TAG_CANDIDATE",
                0x06 => "LIGHT_C_FUNCTION_TVALUE_TAG_CANDIDATE",
                _ => "OTHER_OR_TRANSIENT_TVALUE_TAG"
            };
            luaClosureTag = tag == LuaClosureTValueTag;
        }

        byte[] second = await readAbsolute(state, HeaderBytes, token);
        if (second.Length != HeaderBytes)
            return Fail("STATE_HEADER_RECHECK_SHORT_READ", root.Status, true, true, active);
        // Record FIELD NAMES only, never the volatile pointer values.
        // A moving ci or tbclist is often normal VM activity and differs from
        // structural corruption. This remains a non-atomic, rejected snapshot.
        (string Name, int Offset)[] fields = [
            ("global_State", 0x18), ("CallInfo", 0x20), ("stack_last", 0x28),
            ("stack_base", 0x30), ("tbclist", 0x40), ("base_ci.func", 0x60),
            ("base_ci.top", 0x68),
        ];
        string[] changed = fields.Where(f => Q(first, f.Offset) != Q(second, f.Offset))
            .Select(f => f.Name).ToArray();
        if (changed.Length != 0)
            return Fail("STATE_FIELDS_CHANGED_DURING_READ", root.Status, true, true, active)
                with { ChangedFields = changed };

        // Recheck pointer chain without reinterpreting/storing a stale L.
        ulong reference2 = Q(await Exact(readMain, Violet400LuaVmRootProbe.GlobalReferenceSlotRva, token), 0);
        if (root.GlobalReference != reference2)
            return Fail("GLOBAL_REFERENCE_CHANGED_DURING_READ", root.Status, true, true, active);
        ulong state2 = Q(await Exact(readAbsolute, reference2 - 0x18UL, token), 0);
        if (state2 != state)
            return Fail("LUA_STATE_CHANGED_DURING_READ", root.Status, true, true, active);

        return new(active
                ? "ACTIVE_CALLINFO_SAMPLED_NO_EXECUTION_ATTESTATION"
                : "MAIN_STATE_LAYOUT_MATCH_BASE_CI_IDLE",
            root.Status, true, true, active, luaClosureTag, functionSlot, true);
    }

    /// <summary>
    /// Explicit user-triggered limited retries for a concurrent Lua VM.
    /// No claim of an atomic snapshot: an unchanged sequence is only a
    /// short-lived structural observation. No retries on invalid layouts.
    /// </summary>
    public static async Task<Observation> ProbeWithRetryAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> readMain,
        Func<ulong, int, CancellationToken, Task<byte[]>> readAbsolute,
        CancellationToken token,
        int maxAttempts = 3)
    {
        if (maxAttempts is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        Observation? prior = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var current = await ProbeAsync(readMain, readAbsolute, token);
            prior = current with { ReadAttempts = attempt };
            if (current.Status != "STATE_FIELDS_CHANGED_DURING_READ")
                return prior;
            if (attempt < maxAttempts)
                await Task.Delay(40, token);
        }
        return prior!;
    }

    private static async Task<byte[]> Exact(
        Func<ulong, int, CancellationToken, Task<byte[]>> read,
        ulong address, CancellationToken token)
    {
        byte[] value = await read(address, 8, token);
        if (value.Length != 8)
            throw new InvalidDataException("Short 8-byte pointer recheck.");
        return value;
    }
}
