using Xunit;

// Multiple existing raid tests write the same raid_dbg_Paldea.txt; running in parallel races on CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
