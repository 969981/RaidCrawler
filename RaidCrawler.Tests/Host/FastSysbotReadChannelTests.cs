using RaidCrawler.Core.Host;
using System.Text;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class FastSysbotReadChannelTests
{
    private sealed class FragmentStream(byte[] data, int maxChunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunk)], cancellationToken);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(11)]
    public async Task DecodesArbitrarilyFragmentedHexResponses(int chunk)
    {
        byte[] binary = Enumerable.Range(0, 448)
            .Select(i => (byte)(i * 31)).ToArray();
        byte[] packet = Encoding.ASCII.GetBytes(Convert.ToHexString(binary) + "\n");
        await using var stream = new FragmentStream(packet, chunk);
        byte[] read = await FastSysbotReadChannel.ReadHexLineAsync(
            stream, binary.Length, CancellationToken.None);
        Assert.Equal(binary, read);
        Assert.Equal(packet.Length, stream.Position);
    }

    [Fact]
    public async Task AllowsCrLfAndMixedCaseHex()
    {
        await using var stream = new FragmentStream(
            "0aFf09\r\n"u8.ToArray(), 2);
        Assert.Equal(new byte[] { 0x0A, 0xFF, 0x09 },
            await FastSysbotReadChannel.ReadHexLineAsync(
                stream, 3, CancellationToken.None));
    }

    [Theory]
    [InlineData("ABCD", 2)] // missing LF
    [InlineData("ABGX\n", 2)] // invalid hex
    [InlineData("ABCDZ\n", 2)] // unexpected extra byte
    [InlineData("ABCD\rX", 2)] // invalid CRLF
    [InlineData("AB\n", 2)] // incomplete answer
    public async Task RejectsTruncatedAndDesynchronizedReplies(string text, int decoded)
    {
        await using var stream = new FragmentStream(Encoding.ASCII.GetBytes(text), 2);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            FastSysbotReadChannel.ReadHexLineAsync(
                stream, decoded, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNullDataInsteadOfManufacturingZeroes()
    {
        await using var stream = new FragmentStream([], 1);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            FastSysbotReadChannel.ReadHexLineAsync(
                stream, 2, CancellationToken.None));
    }

    [Fact]
    public void TransportIsExplicitlyBounded()
    {
        Assert.Equal(448, FastSysbotReadChannel.InitialChunkSize);
        Assert.Equal(16384, FastSysbotReadChannel.MaxChunkSize);
        Assert.Equal(new[] { 448, 4096, 16384 },
            FastSysbotReadChannel.ProbeSizes);
    }
}
