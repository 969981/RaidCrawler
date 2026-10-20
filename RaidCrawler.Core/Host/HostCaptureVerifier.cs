using System.Security.Cryptography;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Pure read-only analyzer for snapshots collected in the SAME real raid.
/// It does not capture Switch memory or claim receiver-side confirmation.
/// </summary>
public static class HostCaptureVerifier
{
    /// <summary>
    /// Copies the 45-slot reward area out of a transient game-memory snapshot.
    /// The caller must not keep a borrowed pointer after the synchronous hook.
    /// </summary>
    public static byte[] CopySourceRewards(ReadOnlySpan<byte> raidPoint) =>
        RaidPointLayout.GetRaidPointRewards(raidPoint).ToArray();

    public static byte[] CopyBootstrapRewards(ReadOnlySpan<byte> decodedBootstrap) =>
        RaidPointLayout.GetBootstrapRewards(decodedBootstrap).ToArray();

    public static HostCaptureComparison Compare(
        ReadOnlySpan<byte> sourceRaidPoint,
        ReadOnlySpan<byte> decodedBootstrap)
    {
        ReadOnlySpan<byte> source = RaidPointLayout.GetRaidPointRewards(sourceRaidPoint);
        ReadOnlySpan<byte> wire = RaidPointLayout.GetBootstrapRewards(decodedBootstrap);
        string sourceHash = Convert.ToHexString(SHA256.HashData(source));
        string wireHash = Convert.ToHexString(SHA256.HashData(wire));

        int firstMismatch = -1;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == wire[i])
                continue;

            firstMismatch = i;
            break;
        }

        string? field = firstMismatch < 0 ? null : (firstMismatch % RaidPointLayout.RewardRowLength) / 4 switch
        {
            0 => nameof(RewardRow.ItemId),
            1 => nameof(RewardRow.Quantity),
            2 => nameof(RewardRow.RareType),
            _ => nameof(RewardRow.SubjectType),
        };

        return new HostCaptureComparison(
            firstMismatch < 0,
            sourceHash,
            wireHash,
            firstMismatch,
            firstMismatch < 0 ? -1 : firstMismatch / RaidPointLayout.RewardRowLength,
            field);
    }
}

/// <summary>
/// These hashes cover only the 720 reward bytes, not PII, keys, or full frames.
/// A MATCH means two supplied blobs match, NOT that a live hook fired.
/// </summary>
public sealed record HostCaptureComparison(
    bool Matches,
    string SourceRowsSha256,
    string BootstrapRowsSha256,
    int FirstMismatchByte,
    int FirstMismatchRow,
    string? FirstMismatchField);
