using Vintagestory.API.Client;
using Vintagestory.API.Server;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;

namespace StepUpAdvanced.Infrastructure.Network;

/// <summary>
/// Encapsulates the StepUp Advanced network channel: registration on
/// both sides, the server-to-client broadcast on player join, and the
/// broadcast helpers used by the file-watcher and the <c>/sua reload</c>
/// command.
/// </summary>
/// <remarks>
/// <para>
/// The channel name is a wire-protocol identifier; it must remain
/// <c>"stepupadvanced"</c> across versions, because <c>modinfo.json</c>
/// version-gates the client/server pairing rather than the channel
/// itself doing so. (The same literal is also the mod ID and the
/// Harmony patch ID — they share the spelling but play different roles.)
/// </para>
/// <para>
/// The wire DTO is <see cref="ConfigSyncPacket"/> — a narrow type
/// independent of <see cref="StepUpOptions"/> (which remains the on-disk
/// persistence shape). <see cref="ConfigSyncPacketMapper"/> bridges the
/// two: <c>ToPacket</c> on send, <c>Apply</c> on receive. Persistence and
/// wire contracts evolve independently, so client-only fields on
/// <see cref="StepUpOptions"/> (StepHeight, StepSpeed, increments, probe
/// tunables, QuietMode) are never broadcast.
/// </para>
/// </remarks>
internal sealed class ConfigSyncChannel
{
    /// <summary>
    /// Wire-protocol identifier. Must not change across mod versions.
    /// </summary>
    internal const string ChannelName = "stepupadvanced";

    private ICoreServerAPI? sapi;
    private IServerNetworkChannel? serverChannel;
    private IClientNetworkChannel? clientChannel;

    /// <summary>
    /// Registers the channel on the server side and subscribes the
    /// player-join handler that pushes the current options to each
    /// connecting client.
    /// </summary>
    public void RegisterServer(ICoreServerAPI api)
    {
        sapi = api;
        serverChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<ConfigSyncPacket>();

        if (serverChannel == null)
        {
            ModLog.Error(api, "Failed to register network channel!");
            return;
        }

        api.Event.PlayerNowPlaying += OnPlayerJoin;
    }

    /// <summary>
    /// Registers the channel on the client side with the supplied
    /// receive handler. The handler is invoked on the network thread —
    /// callers must marshal back to the main thread before touching VS
    /// state. (The current client handler does so via
    /// <c>capi.Event.EnqueueMainThreadTask</c>.)
    /// </summary>
    /// <remarks>
    /// The parameter type is <see cref="NetworkServerMessageHandler{T}"/>
    /// (VS-specific delegate) rather than <see cref="System.Action{T}"/>
    /// because that is what <c>IClientNetworkChannel.SetMessageHandler</c>
    /// requires. A compatible method group (matching signature) still
    /// converts implicitly at the call site.
    /// </remarks>
    public void RegisterClient(ICoreClientAPI api, NetworkServerMessageHandler<ConfigSyncPacket> onReceive)
    {
        clientChannel = api.Network.RegisterChannel(ChannelName)
            .RegisterMessageType<ConfigSyncPacket>()
            .SetMessageHandler<ConfigSyncPacket>(onReceive);
    }

    /// <summary>
    /// Sends the current options to every connected player. Used by the
    /// config-file watcher and the <c>/sua reload</c> command after the
    /// server-side config has been reloaded from disk.
    /// </summary>
    /// <remarks>
    /// Loops one player at a time to match the historical behavior; if
    /// any single send fails, others still proceed. The cached
    /// <see cref="serverChannel"/> avoids the per-call name lookup that
    /// the previous inline <c>GetChannel("stepupadvanced")</c> sites
    /// performed. The packet is built once and reused across all
    /// recipients.
    /// </remarks>
    public void BroadcastToAll()
    {
        if (sapi == null || serverChannel == null) return;
        if (StepUpOptions.Current == null) return;

        var packet = ConfigSyncPacketMapper.ToPacket(StepUpOptions.Current);
        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            serverChannel.SendPacket(packet, player);
    }

    /// <summary>
    /// Pushes the current options to a single joining player. As of
    /// Phase 3a this fires unconditionally — previously it only fired
    /// when enforcement was active, which silently dropped clients
    /// across "enforcement turned off" transitions and any other
    /// fields the server might want the client to see.
    /// </summary>
    private void OnPlayerJoin(IServerPlayer player)
    {
        // Defensive — registration captured a valid sapi/serverChannel,
        // so a null here would mean a registration-order bug rather
        // than a runtime condition.
        if (sapi == null || serverChannel == null) return;

        if (StepUpOptions.Current == null)
        {
            ModLog.Error(sapi, "Cannot process OnPlayerJoin: config is null.");
            return;
        }

        serverChannel.SendPacket(ConfigSyncPacketMapper.ToPacket(StepUpOptions.Current), player);
    }
}
