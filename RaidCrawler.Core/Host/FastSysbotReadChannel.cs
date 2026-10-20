using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Independent, read-only sys-botbase TCP connection for bounded bulk reads.
/// Does NOT change the existing control connection, install Switch modules,
/// invoke Poke, or assume that one ReceiveAsync returns a complete TCP frame.
/// A successful byte comparison does NOT identify the active raid host.
/// </summary>
public sealed class FastSysbotReadChannel : IAsyncDisposable
{
    public const int InitialChunkSize = 448;
    public const int MaxChunkSize = 16 * 1024;
    public static readonly int[] ProbeSizes = [448, 4096, 16384];
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private bool _unusable;
    private bool _disposed;

    private FastSysbotReadChannel(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public int CalibratedChunkSize { get; private set; } = InitialChunkSize;
    public string CalibrationEvidence { get; private set; } = "UNTESTED";
    public bool IsUsable => !_unusable && !_disposed;

    public static async Task<FastSysbotReadChannel> ConnectAndCalibrateAsync(
        ISwitchConnectionAsync reference, string ip, int port,
        ulong probeAddress, CancellationToken token)
    {
        if (reference is not SwitchSocketAsync)
            throw new NotSupportedException("高速TCP读取仅支持 Wi-Fi sys-botbase；USB继续原有接口。");
        if (string.IsNullOrWhiteSpace(ip) || port is <= 0 or > 65535)
            throw new ArgumentException("必须使用当前已连接的 sys-botbase IP和端口。");
        var client = new TcpClient { NoDelay = true };
        try
        {
            using (var connectTime = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connectTime.CancelAfter(TimeSpan.FromSeconds(4));
                await client.ConnectAsync(ip, port, connectTime.Token).ConfigureAwait(false);
            }
            var channel = new FastSysbotReadChannel(client);
            try
            {
                // Compare EVERY candidate size against the existing SysBot
                // client on a stable ELF code section. Never auto-trust a
                // bigger transfer just because the reply has the right length.
                var evidence = new List<string>();
                int largestVerified = 0;
                foreach (int size in ProbeSizes)
                {
                    token.ThrowIfCancellationRequested();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    byte[] oldRead = await reference.ReadBytesAbsoluteAsync(
                        probeAddress, size, timeout.Token).ConfigureAwait(false);
                    if (oldRead.Length != size)
                        throw new InvalidDataException("标准客户端基准读取长度不足，不能校准高速读通道。");
                    byte[] fastRead;
                    try
                    {
                        fastRead = await channel.ReadOneChunkAsync(
                            probeAddress, size, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        evidence.Add($"{size}B高速读取超时，保留已验证更小分块");
                        channel._unusable = true; // timed-out TCP reply may arrive later
                        break;
                    }
                    catch (IOException e)
                    {
                        evidence.Add($"{size}B协议校验失败：{e.Message}");
                        channel._unusable = true;
                        break;
                    }
                    if (!fastRead.AsSpan().SequenceEqual(oldRead))
                    {
                        evidence.Add($"{size}B字节对照失败：高速通道已禁用");
                        channel._unusable = true;
                        break;
                    }
                    largestVerified = size;
                    evidence.Add($"{size}B=PASS");
                }
                // If any larger read was interrupted, the TCP stream is
                // potentially desynchronized; fail closed instead of reusing.
                if (channel._unusable || largestVerified < InitialChunkSize)
                    throw new IOException("高速通道校准未完成：" + string.Join("；", evidence));
                channel.CalibratedChunkSize = largestVerified;
                channel.CalibrationEvidence = string.Join("；", evidence);
                return channel;
            }
            catch
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Reads exactly N bytes, split only into calibrated-sized commands.</summary>
    public async Task<byte[]> ReadBytesAbsoluteAsync(
        ulong address, int length, CancellationToken token)
    {
        if (!IsUsable)
            throw new IOException("高速TCP读取连接已失效，需要重新连接和字节校准。");
        if (length is <= 0 or > 0x40000)
            throw new ArgumentOutOfRangeException(nameof(length), "单次读最多256KiB。");
        var data = new byte[length];
        for (int position = 0; position < length;)
        {
            token.ThrowIfCancellationRequested();
            int take = Math.Min(length - position, CalibratedChunkSize);
            byte[] piece = await ReadOneChunkAsync(
                checked(address + (ulong)position), take, token).ConfigureAwait(false);
            piece.CopyTo(data, position);
            position += take;
        }
        return data;
    }

    private async Task<byte[]> ReadOneChunkAsync(
        ulong address, int count, CancellationToken token)
    {
        if (_unusable || _disposed) throw new IOException("高速TCP连接已关闭。");
        if (count is < 1 or > MaxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(count));
        await _commandGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            byte[] command = SwitchCommand.PeekAbsolute(address, count);
            await _stream.WriteAsync(command, timeout.Token).ConfigureAwait(false);
            // The response is hex ASCII followed by LF. Do not assume one
            // TCP receive == one response. Reject extra, missing, CR-only,
            // non-hex, or truncated bytes.
            return await ReadHexLineAsync(_stream, count, timeout.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            _unusable = true; // cannot trust the next TCP reply boundary
            _client.Dispose();
            throw;
        }
        finally { _commandGate.Release(); }
    }

    /// <summary>
    /// Exact one-reply parser, public for fragmented-stream regression tests.
    /// The server may split the 2*N ASCII bytes across any number of packets.
    /// </summary>
    public static async Task<byte[]> ReadHexLineAsync(
        Stream responseStream, int decodedLength, CancellationToken token)
    {
        if (decodedLength is < 1 or > MaxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(decodedLength));
        int hexChars = checked(decodedLength * 2);
        byte[] ascii = new byte[hexChars + 2]; // optional CR + mandatory LF
        int pos = 0;
        while (pos < hexChars)
        {
            int n = await responseStream.ReadAsync(
                ascii.AsMemory(pos, hexChars - pos), token).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("十六进制数据提前断流。");
            pos += n;
        }
        // Strictly consume terminal LF (and optional CR) from TCP.
        int terminator = await responseStream.ReadAsync(
            ascii.AsMemory(hexChars, 1), token).ConfigureAwait(false);
        if (terminator == 0) throw new EndOfStreamException("返回数据缺少换行符。");
        if (ascii[hexChars] == (byte)'\r')
        {
            int second = await responseStream.ReadAsync(
                ascii.AsMemory(hexChars + 1, 1), token).ConfigureAwait(false);
            if (second != 1 || ascii[hexChars + 1] != (byte)'\n')
                throw new InvalidDataException("TCP数据结束符无效。");
        }
        else if (ascii[hexChars] != (byte)'\n')
            throw new InvalidDataException("返回数据长度与请求不一致或格式错误。");
        for (int i = 0; i < hexChars; i++)
        {
            byte c = ascii[i];
            if (!((c >= '0' && c <= '9') ||
                  (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                throw new InvalidDataException($"返回数据在第{i}位不是有效十六进制字符。");
        }
        return Convert.FromHexString(Encoding.ASCII.GetString(ascii, 0, hexChars));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        _commandGate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
