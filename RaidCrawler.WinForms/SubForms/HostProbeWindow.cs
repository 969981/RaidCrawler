using RaidCrawler.Core.Host;
using SysBot.Base;
using System.Text;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Single sys-botbase-backed control panel for the experimental N1 mailbox.
/// No new socket/port; no file I/O on Switch and no game reward writes.
/// </summary>
public sealed class HostProbeWindow : Form
{
    private readonly HostProbeMailboxClient _probe;
    private readonly TextBox _address;
    private readonly TextBox _log;
    private readonly Button _statusButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _captureButton;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    public HostProbeWindow(ISwitchConnectionAsync connection)
    {
        _probe = new HostProbeMailboxClient(connection);
        Text = "RaidCrawler Host Probe (N1 / read-only)";
        ClientSize = new Size(660, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var intro = new Label {
            Text = "Violet 4.0.0 only. Paste [RC-N1] mailbox=0x... from the active native module log.",
            Location = new Point(14, 14), Size = new Size(625, 38),
        };
        _address = new TextBox {
            Location = new Point(14, 54), Size = new Size(390, 26),
            PlaceholderText = "0x... (runtime absolute mailbox address)",
            MaxLength = 18,
        };
        _statusButton = MakeButton("Read status", 414, 54, 108);
        _startButton = MakeButton("Start capture", 14, 96, 142);
        _stopButton = MakeButton("Stop capture", 170, 96, 142);
        _captureButton = MakeButton("Read / compare", 326, 96, 196);
        _log = new TextBox {
            Location = new Point(14, 148), Size = new Size(625, 269),
            Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true,
            WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9),
        };
        Controls.AddRange([intro, _address, _statusButton,
            _startButton, _stopButton, _captureButton, _log]);

        _statusButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.ReadStatusAsync(a, t);
            return $"status={Describe(h)}";
        });
        _startButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.SetEnabledAsync(a, true, t);
            return $"Started. {Describe(h)}";
        });
        _stopButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            HostProbeHeader h = await _probe.SetEnabledAsync(a, false, t);
            return $"Stopped. {Describe(h)}";
        });
        _captureButton.Click += async (_, _) => await Run(async (a, t) =>
        {
            var result = await _probe.ReadCaptureAsync(a, t);
            HostCaptureComparison cmp = result.Capture.Compare();
            return $"N1 DATA: count={result.Header.CaptureCount} " +
                   $"matches={cmp.Matches} firstRow={cmp.FirstMismatchRow} " +
                   $"field={cmp.FirstMismatchField ?? "-"}\r\n" +
                   $"Source SHA256:    {cmp.SourceRowsSha256}\r\n" +
                   $"Bootstrap SHA256: {cmp.BootstrapRowsSha256}\r\n" +
                   "This proves ONLY two captured arrays agree, not actual receiver delivery.";
        });
        FormClosed += (_, _) => _closing.Cancel();
        Append("The mailbox starts disabled. This window can only read snapshots " +
               "and set the capture-enable bit; it cannot append rewards.");
    }

    private static Button MakeButton(string label, int x, int y, int width) =>
        new() { Text = label, Location = new Point(x, y), Size = new Size(width, 30) };

    private static string Describe(HostProbeHeader h) =>
        $"hook={h.Status} enabled={h.Enabled} captures={h.CaptureCount} " +
        $"dropped={h.DroppedCount} generation={h.Generation} tick={h.Tick}";

    private void Append(string text) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");

    private async Task Run(Func<ulong, CancellationToken, Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        _address.Enabled = false;
        _statusButton.Enabled = _startButton.Enabled =
            _stopButton.Enabled = _captureButton.Enabled = false;
        try
        {
            ulong address = HostProbeMailbox.ParseAddress(_address.Text);
            string result = await action(address, _closing.Token);
            if (!IsDisposed) Append(result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex) {
            if (!IsDisposed) Append($"ERROR: {ex.Message}");
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) {
                _address.Enabled = true;
                _statusButton.Enabled = _startButton.Enabled =
                    _stopButton.Enabled = _captureButton.Enabled = true;
            }
        }
    }
}
