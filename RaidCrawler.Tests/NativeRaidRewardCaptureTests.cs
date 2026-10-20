using System.Text.Json;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class NativeRaidRewardCaptureTests
{
    private static object[] MakeRows(uint item = 492, uint count = 1)
    {
        return Enumerable.Range(0, 45).Select(i =>
            (object)new
            {
                slot = i, itemId = i == 0 ? item : 0U,
                quantity = i == 0 ? count : 0U, rare = 0U,
                subject = i == 0 ? 5U : 0U,
                state = i == 0 ? "NONZERO" : "ZERO_CANDIDATE"
            }).ToArray();
    }

    private static string OneEvent(
        object[]? source = null, object[]? wire = null, string? corruptSourceHash = null)
    {
        source ??= MakeRows();
        wire ??= MakeRows();
        static NativeRaidRewardCapture.Row[] ToRows(object[] items) =>
            items.Select((o, i) =>
            {
                using var d = JsonDocument.Parse(JsonSerializer.Serialize(o));
                var v = d.RootElement;
                return new NativeRaidRewardCapture.Row(i,
                    v.GetProperty("itemId").GetUInt32(), v.GetProperty("quantity").GetUInt32(),
                    v.GetProperty("rare").GetUInt32(), v.GetProperty("subject").GetUInt32(),
                    v.GetProperty("state").GetString()!);
            }).ToArray();
        var s = ToRows(source);
        var d = ToRows(wire);
        static object Counts(NativeRaidRewardCapture.Row[] rows) => new
        {
            nonzero = rows.Count(x => x.State != "ZERO_CANDIDATE"),
            allZero = rows.Count(x => x.State == "ZERO_CANDIDATE"),
            nonzeroUnrecognized = rows.Count(x => x.State == "NONZERO_UNRECOGNIZED"),
            holesAfterFirstZero = 0,
            trailingZeroCandidates = rows.Reverse().TakeWhile(x => x.State == "ZERO_CANDIDATE").Count(),
            subjects = new int[] {0,0,0,0,0,1}
        };
        return JsonSerializer.Serialize(new
        {
            @event = "N1_NATIVE_RAID_REWARD_SNAPSHOT",
            seq = 12UL,
            version = "Violet4.0.0",
            provenance = "NATIVE_SERIALIZER_CALLBACK_NOT_RECEIVER_ATTESTED",
            direction = "UNVERIFIED_FROM_CALLBACK_ALONE",
            sourceAddress = "x20+0x13C", wireAddress = "x23+0x79C",
            rewardSlots = 45, recordBytes = 16, originalSourceMatchesWire = s.SequenceEqual(d),
            sourceFnv64 = corruptSourceHash ?? NativeRaidRewardCapture.Fnv64(s).ToString("X16"),
            wireFnv64 = NativeRaidRewardCapture.Fnv64(d).ToString("X16"),
            sourceStats = Counts(s), wireStats = Counts(d),
            sourceRows = source, wireRows = wire,
            memoryWritten = false, hookMode = "observe-only",
            readyForAppend = false, guestReceiptVerified = false, bagSaveVerified = false
        });
    }

    [Fact]
    public void ValidNativeSnapshotRetainsAll45RowsWithoutClaims()
    {
        var report = NativeRaidRewardCapture.Analyze(OneEvent());
        var evt = Assert.Single(report.Events);
        Assert.Equal(45, evt.SourceRows.Count);
        Assert.True(evt.SourceMatchesWire);
        Assert.Equal(492U, evt.SourceRows[0].ItemId);
        Assert.Equal(5U, evt.SourceRows[0].Subject);
        Assert.Equal(44, evt.SourceTailZeros);
        Assert.False(report.GuestRewardReceivedVerified);
        Assert.False(report.RealSwitchCpuHookVerified);
        Assert.Equal(0, report.Rejected);
    }

    [Fact]
    public void DifferentQuantityIsPreservedAsMismatchNotSilentlyOverwritten()
    {
        var report = NativeRaidRewardCapture.Analyze(OneEvent(wire: MakeRows(492, 2)));
        var evt = Assert.Single(report.Events);
        Assert.False(evt.SourceMatchesWire);
        Assert.Equal(1U, evt.SourceRows[0].Quantity);
        Assert.Equal(2U, evt.WireRows[0].Quantity);
    }

    [Fact]
    public void TamperedRowHashFailsClosed()
    {
        var report = NativeRaidRewardCapture.Analyze(
            OneEvent(corruptSourceHash:"0000000000000000"));
        Assert.Empty(report.Events);
        Assert.Equal(1, report.Rejected);
    }

    [Fact]
    public void ForgedAwardOrWriteClaimIsRejected()
    {
        var payload = OneEvent().Replace(
            "\"guestReceiptVerified\":false", "\"guestReceiptVerified\":true");
        var report = NativeRaidRewardCapture.Analyze(payload);
        Assert.Equal(1, report.Rejected);
        Assert.Empty(report.Events);
    }

    [Fact]
    public void InvalidOrOversizedFilesDoNotCreateTrustedEvents()
    {
        Assert.Throws<InvalidDataException>(() => NativeRaidRewardCapture.Analyze(
            new string('x', NativeRaidRewardCapture.MaxJsonlBytes + 1)));
        var report = NativeRaidRewardCapture.Analyze("not json\n{}\n");
        Assert.Empty(report.Events);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(1, report.Ignored);
    }
}
