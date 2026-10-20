using System.Security.Cryptography;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Pure sys-botbase Plan A research implementation for the screenshot's
/// exact 20 rewards, x999, subject 5. This is one contiguous 320-byte write
/// into 20 EMPTY rows of a candidate owner, never a claim of actual delivery.
/// </summary>
public sealed class SysbotOnlyScreenshotPresetWriter(ISwitchConnectionAsync connection)
{
    private readonly SysbotOnlyRaidPointInspector _inspector = new(connection);
    private readonly SysbotOnlyRewardWriter _guard = new(connection);
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public Task<HostScreenshotPresetPermit> PrepareAsync(
        ulong owner, CancellationToken token) => PrepareAsync(owner, ScreenshotRewardPreset.Subject, token);

    public async Task<HostScreenshotPresetPermit> PrepareAsync(
        ulong owner, uint subject, CancellationToken token)
    {
        if (!ScreenshotRewardPreset.IsSupportedSubject(subject))
            throw new InvalidOperationException("只允许 subject=0（全体）或 subject=5（晶耀护符）。");
        await _guard.CheckStateAsync(token).ConfigureAwait(false);
        var observed = await _inspector.InspectAsync(owner, true, token).ConfigureAwait(false);
        if (!observed.SnapshotsStable || !observed.OfflineAppendEligible)
            throw new InvalidOperationException("对象不稳定或原奖励表存在无效行，拒绝执行20条预设。");
        ulong raidPoint = observed.AbsoluteRaidPoint;
        byte[] original = await connection.ReadBytesAbsoluteAsync(
            raidPoint, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        byte[] again = await connection.ReadBytesAbsoluteAsync(
            raidPoint, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        if (!original.AsSpan().SequenceEqual(again))
            throw new InvalidOperationException("准备预设期间对象发生变化；不进行任何写入。");
        string name = SysbotOnlyRaidPointInspector.GetRaidPointName(original);
        var plan = ScreenshotRewardPreset.Plan(original, subject);
        if (!plan.Accepted || plan.AddedCount != ScreenshotRewardPreset.Count)
            throw new InvalidOperationException(
                $"无法无损追加全部20条（原有{plan.OriginalCount}条、最多25条）：{plan.Reason}。不拆分、不覆盖、不删减。");
        string audit = ScreenshotRewardPreset.Audit(original, plan.CandidateRaidPoint, plan.OriginalCount, subject);
        if (audit != "EXACT_20_ROWS_ONLY")
            throw new InvalidOperationException("完整预设字节审计不通过：" + audit);
        int offset = RaidPointLayout.RewardsInRaidPoint +
                     plan.OriginalCount * RaidPointLayout.RewardRowLength;
        return new HostScreenshotPresetPermit(
            owner, raidPoint, name, DateTimeOffset.UtcNow, original, plan.CandidateRaidPoint,
            plan.OriginalCount, offset, subject, Convert.ToHexString(SHA256.HashData(original)),
            "仅候选 RAM 副本：真实网络发送对象/subject条件筛选/背包上限均尚未实机验证");
    }

    public async Task<HostPlanAWriteResult> ApplyAsync(
        HostScreenshotPresetPermit permit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permit);
        await _mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - permit.PreparedUtc > TimeSpan.FromSeconds(15))
                throw new InvalidOperationException("预设准备凭证已超过15秒，请重新读取对象。");
            ValidatePermit(permit);
            // Old Hexbyt3 offsets are still provisional for 4.0.0.
            await _guard.CheckStateAsync(token).ConfigureAwait(false);
            await _guard.ValidateOwnerAsync(permit.OwnerAddress, token).ConfigureAwait(false);
            byte[] current = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(permit.OriginalRaidPoint))
                throw new InvalidOperationException("原奖励数据已变化，禁止写入；请重新准备预设。");
            ulong rowAddress = checked(permit.RaidPointAddress + (ulong)permit.StartOffset);
            byte[] payload = permit.PlannedRaidPoint.AsSpan(
                permit.StartOffset, ScreenshotRewardPreset.Bytes).ToArray();
            // 20 × 16 bytes in ONE sys-botbase write. No loop of 20
            // individually partial-successful writes; no live CAS in sys-botbase.
            await connection.WriteBytesAbsoluteAsync(payload, rowAddress, token)
                .ConfigureAwait(false);
            byte[] verify = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!verify.AsSpan().SequenceEqual(permit.PlannedRaidPoint))
                return new HostPlanAWriteResult(false, "PRESET_WRITE_UNCERTAIN",
                    "写后整个RaidPoint不一致，可能是竞态或部分写入；不要认定到账，不自动强行恢复。", rowAddress);
            return new HostPlanAWriteResult(true, "PRESET_20_ROWS_WRITTEN_TO_CANDIDATE",
                $"候选RAM中20条×999、subject={permit.SubjectType}已完整读回；未验证原版发送、参与者获得或存档持久化。", rowAddress);
        }
        finally { _mutex.Release(); }
    }

    public async Task<HostPlanAWriteResult> RestoreAsync(
        HostScreenshotPresetPermit permit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permit);
        await _mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ValidatePermit(permit);
            await _guard.ValidateOwnerAsync(permit.OwnerAddress, token).ConfigureAwait(false);
            byte[] current = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(permit.PlannedRaidPoint))
                throw new InvalidOperationException(
                    "游戏已经改变了对象，拒绝恢复，以免清除游戏生成的新奖励。");
            ulong rowAddress = checked(permit.RaidPointAddress + (ulong)permit.StartOffset);
            byte[] original20 = permit.OriginalRaidPoint.AsSpan(
                permit.StartOffset, ScreenshotRewardPreset.Bytes).ToArray();
            await connection.WriteBytesAbsoluteAsync(original20, rowAddress, token)
                .ConfigureAwait(false);
            byte[] verify = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            bool ok = verify.AsSpan().SequenceEqual(permit.OriginalRaidPoint);
            return new HostPlanAWriteResult(ok,
                ok ? "PRESET_20_ROWS_RESTORED" : "PRESET_RESTORE_UNCERTAIN",
                ok ? "原来20条空位已读回验证；不能撤回已发送的数据。"
                   : "恢复后的数据与原始快照不一致，请不要继续写入。", rowAddress);
        }
        finally { _mutex.Release(); }
    }

    private static void ValidatePermit(HostScreenshotPresetPermit permit)
    {
        if (permit.OriginalRaidPoint.Length != RaidPointLayout.RaidPointLength ||
            permit.PlannedRaidPoint.Length != RaidPointLayout.RaidPointLength ||
            permit.OriginalRows is < 0 or > ScreenshotRewardPreset.MaxOriginalRows ||
            !ScreenshotRewardPreset.IsSupportedSubject(permit.SubjectType) ||
            permit.StartOffset != RaidPointLayout.RewardsInRaidPoint +
                                  permit.OriginalRows * RaidPointLayout.RewardRowLength)
            throw new InvalidOperationException("20条预设凭证地址或奖励行数非法。");
        var expected = ScreenshotRewardPreset.Plan(permit.OriginalRaidPoint, permit.SubjectType);
        if (!expected.Accepted || expected.OriginalCount != permit.OriginalRows ||
            !expected.CandidateRaidPoint.AsSpan().SequenceEqual(permit.PlannedRaidPoint) ||
            ScreenshotRewardPreset.Audit(permit.OriginalRaidPoint,
                permit.PlannedRaidPoint, permit.OriginalRows, permit.SubjectType) != "EXACT_20_ROWS_ONLY")
            throw new InvalidOperationException("原版奖励、预设内容或追加位置未通过完整审计。");
    }
}

public sealed record HostScreenshotPresetPermit(
    ulong OwnerAddress, ulong RaidPointAddress, string RaidPointName,
    DateTimeOffset PreparedUtc, byte[] OriginalRaidPoint, byte[] PlannedRaidPoint,
    int OriginalRows, int StartOffset, uint SubjectType, string OriginalSha256, string EvidenceLimit);
