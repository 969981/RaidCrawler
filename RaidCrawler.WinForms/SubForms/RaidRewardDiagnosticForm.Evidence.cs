using System.Security.Cryptography;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// All observations are explicit about their provenance. A read-only
/// timer cannot attest native CPU callback execution or reward delivery.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private readonly ComboBox evidenceMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 160,
    };
    private readonly CheckBox evidenceCharm = new()
    {
        ThreeState = true, CheckState = CheckState.Indeterminate,
        Text = "本角色持有晶耀护符", AutoSize = true,
    };
    private readonly System.Windows.Forms.Timer integrityTimer = new()
    {
        Interval = 5000,
    };
    private string? raidRunId;
    private int readOnlyBattleSamples;
    private const int MaxBattleSamples = 160;

    private void ConfigureRewardEvidenceControls(FlowLayoutPanel buttons)
    {
        evidenceMode.Items.AddRange([
            "单人离线",
            "本地 LDN 房主",
            "本地 LDN 参与者",
            "官方互联网房主",
            "官方互联网参与者",
            "模式未确认",
        ]);
        evidenceMode.SelectedIndex = 5;
        buttons.Controls.Add(evidenceMode);
        buttons.Controls.Add(evidenceCharm);

        Button Add(string text, EventHandler handler)
        {
            var button = new Button { Text = text, AutoSize = true };
            buttons.Controls.Add(button);
            button.Click += handler;
            return button;
        }
        var snapshot = Add("只读 Hook 快照", async (_, _) =>
            await CaptureEvidenceSnapshotAsync("manual-snapshot"));
        var import = Add("导入 LDN 证据 JSONL", (_, _) => ImportPokeLdnEvidence());
        var bag = Add("记录背包前后数量", (_, _) => RecordBagDelta());
        Add("关联结算截图", (_, _) => RecordEvidenceScreenshot());
        Add("输出证据结论", (_, _) => AppendEvidenceSummary());

        integrityTimer.Tick += async (_, _) =>
        {
            if (closing || remoteBusy || raidRunId is null || readOnlyBattleSamples >= MaxBattleSamples)
            {
                if (readOnlyBattleSamples >= MaxBattleSamples)
                    integrityTimer.Stop();
                return;
            }
            readOnlyBattleSamples++;
            await CaptureEvidenceSnapshotAsync("battle-periodic");
        };
    }

    private string Scenario => evidenceMode.SelectedItem?.ToString() ?? "模式未确认";
    private bool? ConfirmedCharm => evidenceCharm.CheckState switch
    {
        CheckState.Checked => true,
        CheckState.Unchecked => false,
        _ => null,
    };

    private async Task CaptureEvidenceSnapshotAsync(string source)
    {
        if (connection is null || remoteBusy || closing)
            return;
        remoteBusy = true;
        // No memory writes, but serialize reads with Hook management commands.
        remoteHookButtons.ForEach(x => x.Enabled = false);
        inspect.Enabled = false;
        testWrite.Enabled = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await VerifyViolet400IdentityAsync(timeout.Token);
            string? expectedSha = remoteRewardTemplate is null ? null :
                Convert.ToHexString(SHA256.HashData(remoteRewardTemplate));
            var sample = await RemoteHookIntegritySnapshot.CaptureAsync(
                (rva, count, token) => connection.ReadBytesMainAsync(rva, count, token),
                expectedSha, timeout.Token);

            Append("read-only-hook-snapshot",
                $"[{source}] 入口={sample.EntryState}；reader={sample.ReaderState}；" +
                $"epoch={sample.Epoch} stable={sample.StableEpoch} armed={sample.Armed}；" +
                $"奖励 {sample.Rows}/45，Subject0={sample.Subject0}，Subject5={sample.Subject5}；" +
                $"SHA={sample.RewardSha256[..16]}…；配置比对={sample.TemplateComparison}；" +
                "HOOK_HIT=NOT_OBSERVED。",
                new {
                    runId=raidRunId, source, scenario=Scenario, charm=ConfirmedCharm,
                    sample.EntryState, sample.ReaderState, sample.StableEpoch,
                    sample.Epoch, sample.Armed, sample.RewardSha256, sample.Rows,
                    sample.Subject0, sample.Subject5, sample.TemplateComparison,
                    nativeHookHit="NOT_OBSERVED", bootstrap="NOT_OBSERVED",
                    receiver="NOT_OBSERVED", filtering="NOT_OBSERVED",
                    bag="NOT_OBSERVED", save="NOT_OBSERVED",
                    nativeEventCounterExists=false,
                    snapshotProof="CONTROL_PLANE_INTEGRITY_ONLY",
                    gameMemoryWrites=false
                });
        }
        catch (OperationCanceledException)
        {
            Append("snapshot-error", "只读采样超时或窗口关闭；本次采样缺失。",
                new {runId=raidRunId, source, state="MISSING_OBSERVATION"});
        }
        catch (Exception ex)
        {
            Append("snapshot-error", "只读采样失败：" + ex.GetType().Name + " / " + ex.Message,
                new {runId=raidRunId, source, state="MISSING_OBSERVATION"});
        }
        finally
        {
            remoteBusy = false;
            if (!IsDisposed && !Disposing && !closing)
            {
                remoteHookButtons.ForEach(x => x.Enabled = true);
                inspect.Enabled = true;
                testWrite.Enabled = true;
            }
        }
    }

    private void ImportPokeLdnEvidence()
    {
        using var picker = new OpenFileDialog
        {
            Title = "导入 PokéLDN 自愿开启的奖励追踪 JSONL",
            Filter = "JSONL (*.jsonl;*.json)|*.jsonl;*.json|All files (*.*)|*.*",
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(picker.FileName);
            if (info.Length > RaidRewardTraceEvidence.MaxTraceBytes)
                throw new InvalidDataException("导入大小必须小于 2 MiB。");
            string jsonl = File.ReadAllText(picker.FileName);
            string? expectedSha = remoteRewardTemplate is null ? null :
                Convert.ToHexString(SHA256.HashData(remoteRewardTemplate));
            var analysis = RaidRewardTraceEvidence.Analyze(jsonl, expectedSha);
            Append("ldn-evidence-import",
                $"导入文件 {info.Name}，解析 {analysis.Lines} 行，拒绝 {analysis.Rejected} 行，" +
                $"{analysis.Runs.Count} 个 PokéLDN run；Retail HOOK_HIT=NOT_OBSERVED。",
                new {
                    importedFile=info.Name, analysis.Lines, analysis.Rejected,
                    runCount=analysis.Runs.Count, nativeHookHit="NOT_OBSERVED",
                    provenance="EXTERNAL_POKELDN_JSONL",
                    automaticallyLinkedToCurrentConsole=false
                });
            foreach (var run in analysis.Runs.Take(30))
                Append("ldn-pipeline",
                    $"LDN run={run.RunId}, station={run.Station?.ToString() ?? "N/A"}：" +
                    $"S1={run.Source}; S2_host={run.Host}; " +
                    $"wire_loopback={run.WireLoopback}; receiver={run.Receiver}; " +
                    $"S3={run.Filtering}; 与本地模板={run.TemplateCompare}。",
                    new { importedRunId=run.RunId, station=run.Station, currentConsoleRunId=raidRunId,
                        run.Events, run.Source, run.Host, run.WireLoopback,
                        run.Receiver, run.Filtering, run.TemplateCompare,
                        run.HostRows, nativeHookHit="NOT_OBSERVED",
                        origin="IMPORTED_NOT_NATIVE_RETAIL" });
            foreach (string warning in analysis.Warnings.Take(20))
                Append("ldn-parse-warning", warning);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or
            UnauthorizedAccessException or InvalidDataException)
        {
            Append("ldn-evidence-error", "导入失败：" + ex.Message);
        }
    }

    private void RecordBagDelta()
    {
        using var dialog = new Form
        {
            Text = "手动记录背包证据（不可冒充自动捕获）",
            StartPosition = FormStartPosition.CenterParent,
            Width = 430, Height = 250,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        NumericUpDown Field(string label, int max, int row)
        {
            var input = new NumericUpDown { Minimum = 0, Maximum = max, Width = 160 };
            layout.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
            layout.Controls.Add(input, 1, row);
            return input;
        }
        var id = Field("道具 ID", 65535, 0);
        var before = Field("团战前背包数量", 999999, 1);
        var after = Field("团战后背包数量", 999999, 2);
        var ok = new Button { Text = "记录", DialogResult = DialogResult.OK };
        layout.Controls.Add(ok, 1, 3);
        dialog.AcceptButton = ok;
        dialog.Controls.Add(layout);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Append("manual-bag-delta",
            $"用户记录：物品 #{id.Value}，背包 {before.Value} → {after.Value}，" +
            $"增量 {after.Value - before.Value}；未进行 Switch 背包自动读取。",
            new { runId=raidRunId, scenario=Scenario, playerCharm=ConfirmedCharm,
                itemId=(int)id.Value, before=(int)before.Value,
                after=(int)after.Value, delta=(int)(after.Value-before.Value),
                source="USER_REPORTED", gameMemoryRead=false,
                nativeHookHit="NOT_OBSERVED", saved="NOT_VERIFIED" });
    }

    private void RecordEvidenceScreenshot()
    {
        using var picker = new OpenFileDialog
        {
            Title = "关联团战结算截图（仅登记文件名和 SHA，不上传）",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp",
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(picker.FileName);
            if (info.Length is < 1 or > 20_000_000)
                throw new InvalidDataException("图片大小需在 1B 到 20MB 之间。");
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(info.FullName)));
            Append("manual-reward-screenshot",
                $"用户关联结算截图 {info.Name}，SHA256={digest}；图片内容未进行自动奖励识别。",
                new { runId=raidRunId, scenario=Scenario,
                    fileName=info.Name, bytes=info.Length,
                    imageSha256=digest, source="USER_SUPPLIED_SCREENSHOT",
                    automaticRewardRecognition=false, gameHookHit="NOT_OBSERVED" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Append("screenshot-evidence-error", "图片关联失败：" + ex.Message);
        }
    }

    private void AppendEvidenceSummary()
    {
        Append("evidence-summary",
            $"场景={Scenario}；晶耀护符={(ConfirmedCharm is null ? "未确认" : ConfirmedCharm.Value ? "有" : "无")}；" +
            "安装/epoch只读核验 ≠ Hook命中；S1/S2/接收方需独立采样；" +
            "本窗口仍未取得真实 HOOK_HIT、接收端筛选及保存证据。",
            new {
                runId=raidRunId, scenario=Scenario, playerCharm=ConfirmedCharm,
                switchPatchState="SEE_READ_ONLY_SNAPSHOTS",
                nativeHit="NOT_OBSERVED", pointNameGuard="NOT_OBSERVED",
                bootstrap="NOT_OBSERVED", receiver="NOT_OBSERVED",
                rewardFiltering="NOT_OBSERVED", inventory="USER_REPORTED_IF_PRESENT",
                persistedSave="NOT_OBSERVED",
                inferredCause="UNDETERMINED",
            });
    }
}
