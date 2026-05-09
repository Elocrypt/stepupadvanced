using System.Collections.Generic;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Client-side block blacklist data: codes of blocks the player has chosen
/// to opt out of step-up behavior for.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate file from <see cref="StepUpOptions"/> because it's
/// purely client-side preference (the server's blacklist lives in
/// <see cref="StepUpOptions.BlockBlacklist"/>). Client and server
/// blacklists are merged at runtime by the proximity checker.
/// </para>
/// <para>
/// The on-disk JSON file name is <c>StepUpAdvanced_BlockBlacklist.json</c> —
/// kept identical to pre-Phase-2c so existing user data loads unchanged.
/// </para>
/// </remarks>
public class BlockBlacklistOptions
{
    /// <summary>
    /// Schema version. Bumped when the file shape requires migration.
    /// Currently no migrations exist for this options file (only one schema version).
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Block codes currently on the client-side blacklist.
    /// Stored as full asset locations (e.g. <c>game:soil-low-normal</c>).
    /// </summary>
    public List<string> BlockCodes { get; set; } = new List<string>();

    /// <summary>
    /// Ambient global access. Reads happen from the proximity checker and
    /// chat commands; writes happen only inside <see cref="BlockBlacklistStore"/>.
    /// </summary>
    public static BlockBlacklistOptions Current { get; internal set; } = new();
}
