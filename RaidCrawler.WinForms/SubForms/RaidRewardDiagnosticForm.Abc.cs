using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Imports externally instrumented A/B/C reward observations. This form
/// intentionally does NOT attempt to scan Lua VM heaps or claim that current
/// sys-botbase has runtime event interception.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private void ImportAbcCaptureEvidence()
    {
        using var picker = new OpenFileDialog
        {
            Title = "导入 A/B/C 奖励生命周期捕获（必须来自独立运行时采集器）",
            Filter = "JSONL (*.jsonl;*.json)|*.jsonl;*.json|All files (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(picker.FileName);
            if (info.Length is < 1 or > RaidRewardAbcEvidence.MaxBytes)
                throw new InvalidDataException("A/B/C JSONL 必须是 1B～2MiB。");
            var configured = remoteRewardTemplate is null
                ? null : RaidRewardEditor.DecodeSlots(remoteRewardTemplate);
            var report = RaidRewardAbcEvidence.Analyze(
                File.ReadAllText(info.FullName), configured);
            Append("abc-evidence-import",
                $"导入 {info.Name}：{report.Lines} 行，" +
                $"有效场次 {report.Runs.Count}，不符合格式 {report.Rejected}，" +
                $"非 A/B/C 日志 {report.Ignored}。来自外部的捕获内容未经 Switch 原生认证。",
                new
                {
                    fileName = info.Name, report.Lines, report.Rejected,
                    report.Ignored, runs = report.Runs.Count,
                    currentConsoleRunId = raidRunId,
                    correlationToCurrentConsole = "NOT_VERIFIED",
                    directABCRuntimeObservation = "NOT_IMPLEMENTED",
                    report.OverallNativeHit,
                    source = "UNATTESTED_IMPORTED_CAPTURES",
                    gameMemoryWrites = false
                });
            foreach (var run in report.Runs.Take(30))
            {
                Append("abc-stage-comparison",
                    $"run={run.RunId}：A={run.A}({run.ARows?.ToString() ?? "?"})，" +
                    $"B={run.B}({run.BRows?.ToString() ?? "?"})，" +
                    $"C={run.C}({run.CRows?.ToString() ?? "?"})；" +
                    $"A→B={run.AToB}；B→C={run.BToC}；" +
                    $"模板 A/B/C={run.TemplateInA}/{run.TemplateInB}/{run.TemplateInC}；" +
                    $"结论={run.Result}（导入记录，非实机命中证明）。",
                    new
                    {
                        run.RunId, run.A, run.B, run.C, run.AToB, run.BToC,
                        run.TemplateInA, run.TemplateInB, run.TemplateInC,
                        run.Result, run.EvidenceQuality,
                        run.ARows, run.BRows, run.CRows,
                        currentConsoleRunId = raidRunId,
                        directCpuHitObserved = false, bagDelta = "NOT_OBSERVED",
                        saved = "NOT_OBSERVED", gameMemoryWrites = false
                    });
            }
            foreach (string warning in report.Warnings.Take(20))
                Append("abc-evidence-warning", warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            Append("abc-evidence-error", "A/B/C 导入失败：" + ex.Message);
        }
    }
}
