using System.Security.Cryptography;

namespace RaidCrawler.Core.Structures;

/// <summary>
/// Pure, offline model of the native on-console hook handoff. This does NOT
/// install an AArch64 trampoline, discover a live pointer or write Switch RAM.
/// Host identity/dispatch/build/seed must be attested by a FUTURE native agent.
/// </summary>
public sealed record RaidRewardHookContext(
    string BuildId,
    string SessionId,
    uint SelectedSeed,
    uint ObservedSeed,
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
        if (!context.IsOfficialOnline)
            return new(false, "This contract targets a separately verified native online host path.", null);
        if (!context.NativeHostConfirmed || !context.OutboundBootstrapConfirmed || !context.BeforeCompression)
            return new(false, "No verified outbound host bootstrap hook at the pre-compression stage.", null);
        if (context.SelectedSeed == 0 || context.ObservedSeed != context.SelectedSeed)
            return new(false, "Selected and observed raid seed do not match.", null);
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
                return new(true, "Offline stage built; not written to any Switch.", output);
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
