namespace StepUpAdvanced.Core;

/// <summary>
/// Whether the client is currently in Basic mode (see
/// <c>Domain.Physics.BasicMode</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Fails closed.</b> Every client session starts in Basic mode and only
/// leaves it when the server's config packet arrives. A server running the
/// mod always sends that packet on join, so "packet received" is the proof
/// the server has it. If anything goes wrong (no channel, dropped packet,
/// a server that never installed the mod) the player stays in the safe,
/// locked mode rather than the adjustable one. Single-player needs no
/// special case: the integrated server sends the same packet.
/// </para>
/// <para>
/// <b>Static on purpose.</b> The Harmony speed getters are static and run
/// inside the game's physics step, so they need ambient access, the same way
/// they already read <c>StepUpOptions.Current</c>. <c>volatile</c> because
/// the flag is written on the client main thread and read from physics code,
/// and the read sits on a per-tick path where a lock would be wasteful.
/// </para>
/// </remarks>
internal static class BasicModeState
{
    private static volatile bool active = true;

    /// <summary><c>true</c> while the server has not confirmed it runs the mod.</summary>
    public static bool Active => active;

    /// <summary>Called at client start: assume the server lacks the mod until it proves otherwise.</summary>
    public static void Enter() => active = true;

    /// <summary>Called when the server's config packet arrives.</summary>
    public static void Exit() => active = false;
}
