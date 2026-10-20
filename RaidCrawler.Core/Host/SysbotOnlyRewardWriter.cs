using System.Security.Cryptography;
using SysBot.Base;

namespace RaidCrawler.Core.Host;

/// <summary>
/// Experimental Plan A: standard sys-botbase process-memory write only.
/// IMPORTANT: NO static proof that a vtable-matched object is actively sent.
/// Writes exactly ONE reward row in an explicitly selected candidate RaidPoint.
/// This is not a network reward or bag-delivery guarantee.
/// </summary>
public sealed class SysbotOnlyRewardWriter(ISwitchConnectionAsync connection)
{
    public const uint ExperimentalItem = 15; // Quick Ball only
    public const int MaxQuantity = 10;
    private readonly SysbotOnlyRaidPointInspector _inspector = new(connection);
    private readonly SvRaidLifecycleReader _lifecycle = new(connection);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// Requires two consistent snapshots, the live three-vtable identity,
    /// and an explicit current lobby-with-guests signal. Hexbyt3 source says
    /// version 4.0.0, but the literal lobby offsets still carry 3.0.0 comments,
    /// so the lobby signal remains experimental.
    /// </summary>
    public async Task<HostPlanAWritePermit> PrepareAsync(
        ulong ownerAddress, uint quantity, CancellationToken token)
    {
        if (quantity is 0 or > MaxQuantity)
            throw new InvalidOperationException("目前仅支持先机球×1–10，且只能追加一行。");

        await CheckStateAsync(token).ConfigureAwait(false);
        var observed = await _inspector.InspectAsync(ownerAddress, true, token)
            .ConfigureAwait(false);
        if (!observed.SnapshotsStable || !observed.OfflineAppendEligible)
            throw new InvalidOperationException($"原始对象不稳定或奖励表无合法空槽：{observed.Reason}");

        ulong raidPoint = observed.AbsoluteRaidPoint;
        byte[] original = await connection.ReadBytesAbsoluteAsync(
            raidPoint, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        SysbotOnlyRaidPointInspector.GetRaidPointName(original);
        byte[] next = await connection.ReadBytesAbsoluteAsync(
            raidPoint, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
        if (!original.AsSpan().SequenceEqual(next))
            throw new InvalidOperationException("准备阶段的对象数据已发生变化；拒绝下发修改。");

        var additions = new[] { new RewardRow(ExperimentalItem, quantity, 0, 5) };
        var plan = RewardAppendPlanner.Plan(original, additions, id => id == ExperimentalItem);
        if (!plan.Accepted)
            throw new InvalidOperationException($"内存追加计划未通过：{plan.Reason}");

        string audit = HostPlanAWriteAudit.Check(original, plan.CandidateRaidPoint,
            plan.OriginalCount, ExperimentalItem, quantity);
        if (audit != HostPlanAWriteAudit.Correct)
            throw new InvalidOperationException($"单行写入字节审计失败：{audit}");

        int slotOffset = RaidPointLayout.RewardsInRaidPoint +
                         plan.OriginalCount * RaidPointLayout.RewardRowLength;
        byte[] replacement = plan.CandidateRaidPoint.AsSpan(slotOffset, 16).ToArray();
        if (replacement.AsSpan().SequenceEqual(original.AsSpan(slotOffset, 16)))
            throw new InvalidOperationException("没有识别到一条真正的新奖励记录。");
        return new HostPlanAWritePermit(
            ownerAddress, raidPoint, observed.RaidPointName,
            DateTimeOffset.UtcNow, original, plan.CandidateRaidPoint,
            plan.OriginalCount, slotOffset, ExperimentalItem, quantity,
            Convert.ToHexString(SHA256.HashData(original)),
            "candidate-only; vtable identity + provisional lobby observation are NOT proof of active network serialization");
    }

    public async Task<HostPlanAWriteResult> ApplyAsync(
        HostPlanAWritePermit permit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permit);
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - permit.PreparedUtc > TimeSpan.FromSeconds(15))
                throw new InvalidOperationException("预览凭证超过15秒，需要重新读取当前房间的对象。");

            if (permit.ItemId != ExperimentalItem || permit.Quantity is 0 or > MaxQuantity ||
                permit.OriginalRaidPoint.Length != RaidPointLayout.RaidPointLength ||
                permit.PlannedRaidPoint.Length != RaidPointLayout.RaidPointLength ||
                permit.RowOffset != RaidPointLayout.RewardsInRaidPoint + permit.OriginalRows * 16)
                throw new InvalidOperationException("准备凭证参数不合法。");

            var plan = RewardAppendPlanner.Plan(permit.OriginalRaidPoint,
                [new RewardRow(ExperimentalItem, permit.Quantity, 0, 5)],
                id => id == ExperimentalItem);
            if (!plan.Accepted || plan.OriginalCount != permit.OriginalRows ||
                !plan.CandidateRaidPoint.AsSpan().SequenceEqual(permit.PlannedRaidPoint) ||
                HostPlanAWriteAudit.Check(permit.OriginalRaidPoint, permit.PlannedRaidPoint,
                    permit.OriginalRows, ExperimentalItem, permit.Quantity) != HostPlanAWriteAudit.Correct)
                throw new InvalidOperationException("离线追加计划或无损字节审计与准备时的原始对象不一致。");

            await CheckStateAsync(token).ConfigureAwait(false);
            await ValidateOwnerAsync(permit.OwnerAddress, token).ConfigureAwait(false);
            // Stock sys-botbase has no conditional compare-and-swap operation.
            // This check shrinks but DOES NOT eliminate the time-of-check/write race.
            byte[] current = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(permit.OriginalRaidPoint))
                throw new InvalidOperationException("原版 RaidPoint 在准备和写入期间变化，拒绝写入。");

            ulong rowAddress = checked(permit.RaidPointAddress + (ulong)permit.RowOffset);
            byte[] appended = permit.PlannedRaidPoint.AsSpan(permit.RowOffset, 16).ToArray();
            await connection.WriteBytesAbsoluteAsync(appended, rowAddress, token)
                .ConfigureAwait(false);
            byte[] after = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!after.AsSpan().SequenceEqual(permit.PlannedRaidPoint))
                return new HostPlanAWriteResult(false, "WRITE_UNCERTAIN",
                    "发送行写入后整体校验失败。对象可能被游戏更新，禁止假设奖励已经发送，也不要盲目恢复。", rowAddress);
            return new HostPlanAWriteResult(true, "ONE_ROW_WRITTEN_TO_CANDIDATE",
                "仅证实本次候选 RaidPoint 的一行已写入并读回；未证实已进入房主发包、参与者到账或保存。", rowAddress);
        }
        finally { _writeGate.Release(); }
    }

    /// <summary>
    /// Best-effort manual restore if the entire candidate still exactly
    /// matches the post-write buffer. NEVER roll back unrelated changes.
    /// Does not undo any packet already sent by the game.
    /// </summary>
    public async Task<HostPlanAWriteResult> RestoreAsync(
        HostPlanAWritePermit permit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permit);
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _lifecycle.CheckViolet400Async(token).ConfigureAwait(false);
            await ValidateOwnerAsync(permit.OwnerAddress, token).ConfigureAwait(false);
            byte[] current = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(permit.PlannedRaidPoint))
                throw new InvalidOperationException("候选对象已变化，拒绝恢复，以免覆盖游戏的新奖励。");

            ulong rowAddress = checked(permit.RaidPointAddress + (ulong)permit.RowOffset);
            byte[] original = permit.OriginalRaidPoint.AsSpan(permit.RowOffset, 16).ToArray();
            await connection.WriteBytesAbsoluteAsync(original, rowAddress, token)
                .ConfigureAwait(false);
            byte[] verify = await connection.ReadBytesAbsoluteAsync(
                permit.RaidPointAddress, RaidPointLayout.RaidPointLength, token).ConfigureAwait(false);
            bool ok = verify.AsSpan().SequenceEqual(permit.OriginalRaidPoint);
            return new HostPlanAWriteResult(ok, ok ? "RESTORED_CANDIDATE" : "RESTORE_UNCERTAIN",
                ok ? "候选对象原始内容已读回验证；不能撤回此前可能发送的数据。"
                   : "恢复后校验未通过，对象可能已被游戏修改。", rowAddress);
        }
        finally { _writeGate.Release(); }
    }

    private async Task CheckStateAsync(CancellationToken token)
    {
        await _lifecycle.CheckViolet400Async(token).ConfigureAwait(false);
        var first = await _lifecycle.ReadAsync(true, token).ConfigureAwait(false);
        await Task.Delay(250, token).ConfigureAwait(false);
        var second = await _lifecycle.ReadAsync(true, token).ConfigureAwait(false);
        // Only candidate lobby with at least one participant; no battle.
        // These offsets are STILL unverified on retail 4.0.0, so permission
        // requires explicit user confirmation in the UI, not automation.
        static bool IsLobbyWithGuest(SvRaidSignals s) =>
            s.IsLobby == true && s.GuestCount is > 0 &&
            s.IsBattle == false && s.IsOverworld == false;
        if (!IsLobbyWithGuest(first) || !IsLobbyWithGuest(second))
            throw new InvalidOperationException(
                "未连续两次识别到房间内有参与者且尚未战斗（Hexbyt3旧版偏移尚未实机校准）；禁止写入。");
    }

    private async Task ValidateOwnerAsync(ulong owner, CancellationToken token)
    {
        ulong mainBase = await connection.GetMainNsoBaseAsync(token).ConfigureAwait(false);
        byte[] functionPointer = await connection.ReadBytesMainAsync(
            SysbotOnlyRaidPointInspector.SerializerEntrySlotRva, 8, token).ConfigureAwait(false);
        if (functionPointer.Length != 8 ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(functionPointer) !=
            checked(mainBase + SysbotOnlyRaidPointInspector.SerializerRva))
            throw new InvalidOperationException("当前游戏的序列化虚表入口与ELF不一致。");
        byte[] ownerHeader = await connection.ReadBytesAbsoluteAsync(owner, 0x30, token)
            .ConfigureAwait(false);
        if (!SysbotOnlyRaidPointInspector.MatchesSerializerType(ownerHeader, mainBase))
            throw new InvalidOperationException("当前对象不符合已核验的三张虚表，拒绝内存修改。");
        byte[] opcodes = await connection.ReadBytesMainAsync(
            SysbotOnlyRaidPointInspector.SerializerRva,
            SysbotOnlyRaidPointInspector.SerializerFingerprintLength, token).ConfigureAwait(false);
        SysbotOnlyRaidPointInspector.ValidateSerializerFingerprint(opcodes);
    }
}

public sealed record HostPlanAWritePermit(
    ulong OwnerAddress, ulong RaidPointAddress, string RaidPointName,
    DateTimeOffset PreparedUtc, byte[] OriginalRaidPoint, byte[] PlannedRaidPoint,
    int OriginalRows, int RowOffset, uint ItemId, uint Quantity,
    string OriginalSha256, string EvidenceLimit);

public sealed record HostPlanAWriteResult(
    bool Verified, string Code, string Explanation, ulong RowAddress);
