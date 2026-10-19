using System.Security.Cryptography;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Pure, offline model of the native on-console hook handoff. This does NOT
/// install an AArch64 trampoline, discover a live pointer or write Switch RAM.
/// A future game-side hook identifies the ACTUAL battle; no selected UI den
/// or selected seed is required. Current SelectedSeed is legacy telemetry only.
/// Neither official Internet nor local LDN is assumed until runtime verified.
/// </summary>
public sealed record RaidRewardHookContext(
    string BuildId,
    string SessionId,
    uint SelectedSeed, // deprecated: optional UI snapshot; NEVER a target selector
    uint ObservedSeed, // the actual game-side battle value, captured at runtime
    bool NativeHostConfirmed,
    bool OutboundBootstrapConfirmed,
    bool BeforeCompression,
    bool IsOfficialOnline);

public sealed record RaidRewardHookResult(bool Accepted, string Reason, byte[]? ModifiedPlaintext);

public sealed class RaidRewardHookContract
{
    public const string VerifiedViolet400BuildId = "709bfd66115298640155fcc4979dba151c7cc79a";
    private readonly HashSet<string> completedSessions = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>
    /// Creates a patched copy of one uncompressed 0xAA0 frame when explicit
    /// evidence is available. No external process/memory access is performed.
    /// </summary>
    public RaidRewardHookResult TryStageOffline(RaidRewardHookContext context,
        ReadOnlySpan<byte> uncompressed, IReadOnlyList<RaidRewardEntry?> slots)
    {
        if (!string.Equals(context.BuildId, VerifiedViolet400BuildId, StringComparison.OrdinalIgnoreCase))
            return new(false, "Unknown build: exact 4.0.0 ELF build required.", null);
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return new(false, "Missing per-battle session identity.", null);
        if (!context.NativeHostConfirmed || !context.OutboundBootstrapConfirmed || !context.BeforeCompression)
            return new(false, "No verified outbound host bootstrap hook at the pre-compression stage.", null);
        if (uncompressed.Length != RaidPointOfflinePatch.BootstrapSize)
            return new(false, "Unexpected bootstrap frame length.", null);

        lock (gate)
        {
            if (completedSessions.Contains(context.SessionId))
                return new(false, "This session has already been staged.", null);
            try
            {
                var output = RaidPointOfflinePatch.PatchBootstrap(uncompressed, slots);
                if (!RaidPointOfflinePatch.UntouchedFieldsEqual(uncompressed, output,
                        RaidPointOfflinePatch.BootstrapPointOffset))
                    return new(false, "Non-reward metadata changed.", null);
                completedSessions.Add(context.SessionId);
                return new(true, "Battle-agnostic offline fixture staged; no Switch hook or memory write performed.", output);
            }
            catch (ArgumentException ex)
            {
                return new(false, ex.Message, null);
            }
        }
    }

    public static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));
}
