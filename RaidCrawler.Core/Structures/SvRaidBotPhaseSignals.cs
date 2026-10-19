namespace RaidCrawler.Core.Structures;

/// <summary>
/// Read-only candidate flags reproduced from hexbyt3/SVRaidBot's
/// PokeDataOffsetsSV for retail Scarlet/Violet. Its constants are annotated
/// 3.0.0 upstream even though its game version says 4.0.0. They are NOT yet
/// behaviorally calibrated on this particular Violet 4.0.0 installation.
/// </summary>
public static class SvRaidBotPhaseSignals
{
    public const ulong LobbyFlagMainRva = 0x043DF430;
    public const ulong LoadedFlagMainRva = 0x047D2020;
    public static readonly long[] OverworldPointer =
        [0x473ADE0, 0x160, 0xE8, 0x28];

    public sealed record ReadResult(
        RaidPhaseObservationMachine.Signals Signals,
        bool OverworldPointerAvailable,
        string Source = "SVRAIDBOT_CANDIDATE_UNCALIBRATED");

    /// <summary>
    /// No button presses, no writes. Unknown/invalid data stays unknown;
    /// the resolved overworld byte can be unavailable without losing the
    /// lobby and loaded signals.
    /// </summary>
    public static async Task<ReadResult> ReadAsync(
        Func<ulong, int, CancellationToken, Task<byte[]>> readMain,
        Func<IReadOnlyList<long>, int, CancellationToken, Task<byte[]>> readPointer,
        CancellationToken ct = default)
    {
        byte? lobby = null, loaded = null, overworld = null;

        byte[] lobbyBytes = await readMain(LobbyFlagMainRva, 1, ct);
        byte[] loadedBytes = await readMain(LoadedFlagMainRva, 1, ct);
        if (lobbyBytes.Length == 1) lobby = lobbyBytes[0];
        if (loadedBytes.Length == 1) loaded = loadedBytes[0];

        try
        {
            byte[] value = await readPointer(OverworldPointer, 1, ct);
            if (value.Length == 1) overworld = value[0];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
            ArgumentException or IndexOutOfRangeException)
        {
            // Pointer chains may not be initialized on title/lobby screens.
            // Do NOT substitute a fabricated overworld value.
        }
        return new(new(lobby, loaded, overworld), overworld.HasValue);
    }
}
