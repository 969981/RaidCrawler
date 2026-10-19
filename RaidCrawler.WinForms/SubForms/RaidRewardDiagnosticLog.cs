using System.Text.Json;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Append-only local diagnostics log. Never records Nintendo account tokens,
/// remote participant information, or game memory dumps.
/// </summary>
internal sealed class RaidRewardDiagnosticLog : IDisposable
{
    private readonly object sync = new();
    private readonly StreamWriter writer;
    private long sequence;
    public string Path { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public RaidRewardDiagnosticLog()
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidCrawler", "raid-diagnostics");
        Directory.CreateDirectory(folder);
        Path = System.IO.Path.Combine(folder,
            $"raid-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SessionId[..8]}.jsonl");
        writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
        Write("created", "Local reward diagnostic session started");
    }

    public void Write(string stage, string message, object? detail = null)
    {
        lock (sync)
        {
            sequence++;
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                sequence,
                utc = DateTimeOffset.UtcNow,
                session = SessionId,
                stage,
                message,
                detail,
            }));
        }
    }

    public void Dispose()
    {
        lock (sync) writer.Dispose();
    }
}
