using System.Security.Cryptography;

namespace RaidCrawler.Core.LiveRewardV4;

/// <summary>
/// Verifies the local manifest and subsdk9 came from the same build.
/// This is a PREINSTALL consistency check. It does not prove the Switch
/// actually loaded that binary; runtime validation is separate.
/// </summary>
public static class MailboxV4ArtifactVerifier
{
    public static void VerifyBytes(MailboxV4Manifest manifest, ReadOnlySpan<byte> nso)
    {
        if (nso.Length < 64 || !nso[..4].SequenceEqual("NSO0"u8))
            throw new InvalidDataException("V4_EXPECTED_NSO_FILE");
        string actual = Convert.ToHexString(SHA256.HashData(nso)).ToLowerInvariant();
        if (!actual.Equals(manifest.NsoSha256, StringComparison.Ordinal))
            throw new InvalidDataException("V4_MANIFEST_NSO_SHA256_MISMATCH");
    }

    public static async Task VerifyLocalPairAsync(
        MailboxV4Manifest manifest, string nsoPath, CancellationToken token)
    {
        if (!File.Exists(nsoPath))
            throw new FileNotFoundException(
                "需要将配对的 subsdk9 与 mailbox-v4-locator.json 放在同一文件夹，"
                + "否则拒绝自动定位。", nsoPath);
        byte[] contents = await File.ReadAllBytesAsync(nsoPath, token).ConfigureAwait(false);
        VerifyBytes(manifest, contents);
    }
}
