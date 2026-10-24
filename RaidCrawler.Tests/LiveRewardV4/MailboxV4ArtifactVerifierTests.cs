using System.Security.Cryptography;
using RaidCrawler.Core.LiveRewardV4;
using Xunit;

namespace RaidCrawler.Tests.LiveRewardV4;

public sealed class MailboxV4ArtifactVerifierTests
{
    private static readonly byte[] FakeNso = CreateFakeNso();

    private static byte[] CreateFakeNso()
    {
        var data = new byte[96];
        "NSO0"u8.CopyTo(data);
        for (int i = 4; i < data.Length; ++i) data[i] = (byte)i;
        return data;
    }

    [Fact]
    public void ExactNsoAndManifestHashAreRequired()
    {
        string sha = Convert.ToHexString(SHA256.HashData(FakeNso)).ToLowerInvariant();
        MailboxV4ArtifactVerifier.VerifyBytes(new MailboxV4Manifest(0, sha), FakeNso);
        var corrupt = FakeNso.ToArray();
        corrupt[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4ArtifactVerifier.VerifyBytes(new MailboxV4Manifest(0, sha), corrupt));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4ArtifactVerifier.VerifyBytes(new MailboxV4Manifest(0, sha),
                new byte[96]));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4ArtifactVerifier.VerifyBytes(new MailboxV4Manifest(0, sha),
                new byte[4]));
    }

    [Fact]
    public async Task MissingLocalNsoFailsBeforeRuntimeDiscovery()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "v4-missing-" + Guid.NewGuid().ToString("N"), "subsdk9");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            MailboxV4ArtifactVerifier.VerifyLocalPairAsync(
                new MailboxV4Manifest(0, new string('a', 64)),
                path, CancellationToken.None));
    }
}
