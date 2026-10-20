using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Manual import of Switch SD N0/N1 native telemetry; never queries Switch.</summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private readonly Button nativeRewardImport = new()
    {
        Text = "导入实机 0x2F 奖励快照 JSONL", AutoSize = true
    };

    private void ConfigureNativeRewardCaptureImport(FlowLayoutPanel toolbar)
    {
        toolbar.Controls.Add(nativeRewardImport);
        nativeRewardImport.Click += (_, _) => ImportNativeRewardCapture();
    }

    private void ImportNativeRewardCapture()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "从 Switch SD:/switch/raidcrawler/ 导入 raid-native-rewards.jsonl",
            Filter = "原生奖励快照 (*.jsonl)|*.jsonl|全部文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length is < 1 or > NativeRaidRewardCapture.MaxJsonlBytes)
                throw new InvalidDataException("原生奖励快照文件大小必须为 1..4MiB");
            var analysis = NativeRaidRewardCapture.Analyze(File.ReadAllText(dialog.FileName));
            Append("native-raid-reward-import",
                $"Switch 原生 0x2F 快照：有效 {analysis.Events.Count}，拒绝 {analysis.Rejected}，忽略 {analysis.Ignored}。"+
                " 仅代表外部日志记录，未证实接收方实际收到或保存。",
                new
                {
                    source = "MANUALLY_IMPORTED_SWITCH_NATIVE_JSONL_UNATTESTED",
                    valid = analysis.Events.Count, analysis.Rejected, analysis.Ignored,
                    attempts = analysis.Events.Select(x => new
                    {
                        seq = x.Sequence,
                        baseRows = x.SourceNonzero,
                        wireRows = x.WireNonzero,
                        matches = x.SourceMatchesWire,
                        unknownSourceRows = x.SourceUnrecognized,
                        unknownWireRows = x.WireUnrecognized,
                        wireTailZeroCandidates = x.WireTailZeros,
                        sourceFnv64 = x.SourceFnv64, wireFnv64 = x.WireFnv64
                    }).Take(50).ToArray(),
                    networkDirectionConfirmed = false,
                    guestReceiptConfirmed = false,
                    gameWrites = false
                });
            if (analysis.Events.Count == 0) return;

            using var viewer = new Form
            {
                Text = "N0/N1 真机 0x2F 原生奖励快照（日志导入，未认证接收）",
                Size = new Size(1120, 710),
                MinimumSize = new Size(850, 480),
                StartPosition = FormStartPosition.CenterParent
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(8),
                ColumnCount = 1, RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            viewer.Controls.Add(layout);
            var heading = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Color.DarkRed,
                Text = "Switch 上的 Native 0x2F 序列化 Hook：x20 源数据 与 x23 发送明文副本。" +
                    Environment.NewLine +
                    "全部 45 行来自已导入的日志；全零仅是候选空槽，不能自动安全追加。"+
                    Environment.NewLine +
                    "日志来源未经签名认证，不证明房主发送方向、参与者领奖、背包或存档。",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(heading, 0, 0);
            var choice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var evt in analysis.Events)
                choice.Items.Add($"Hit #{evt.Sequence} | 原版 {evt.SourceNonzero} 行 / 输出 {evt.WireNonzero} 行 | " +
                    $"源/输出一致={evt.SourceMatchesWire} | 末尾全零候选={evt.WireTailZeros}");
            layout.Controls.Add(choice, 0, 1);
            var rows = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            foreach (var (field, name) in new (string,string)[]
            {
                ("slot","槽位"), ("sourceItem","源道具"), ("sourceNum","源数量"),
                ("sourceRare","源Rare"), ("sourceSubject","源Subject"),
                ("sourceState","源记录"),
                ("wireItem","输出道具"), ("wireNum","输出数量"),
                ("wireRare","输出Rare"), ("wireSubject","输出Subject"),
                ("wireState","输出记录")
            })
                rows.Columns.Add(field,name);
            layout.Controls.Add(rows, 0, 2);
            choice.SelectedIndexChanged += (_, _) =>
            {
                rows.Rows.Clear();
                if (choice.SelectedIndex < 0) return;
                var selected = analysis.Events[choice.SelectedIndex];
                for (int i = 0; i < NativeRaidRewardCapture.Slots; i++)
                {
                    var a = selected.SourceRows[i];
                    var b = selected.WireRows[i];
                    rows.Rows.Add(i, a.ItemId, a.Quantity, a.Rare, a.Subject, a.State,
                        b.ItemId, b.Quantity, b.Rare, b.Subject, b.State);
                }
            };
            choice.SelectedIndex = 0;
            viewer.ShowDialog(this);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   UnauthorizedAccessException or ArgumentException)
        {
            Append("native-raid-reward-import-error",
                $"Switch 原生 JSONL 导入失败：{ex.GetType().Name}；不影响游戏。");
        }
    }
}
