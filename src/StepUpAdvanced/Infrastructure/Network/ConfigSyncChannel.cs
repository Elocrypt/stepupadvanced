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
/// The channel name is a wire-protocol identifier and must remain
/// <c>"stepupadvanced"</c> across versions.
/// <see cref="ConfigSyncPacket"/> is the wire DTO — a narrow type separate
/// from <see cref="StepUpOptions"/>, so client-only fields (StepHeight,
/// StepSpeed, increments, probe tunables, QuietMode) are never broadcast.
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

    public void BroadcastToAll()
    {
        if (sapi == null || serverChannel == null) return;
        if (StepUpOptions.Current == null) return;

        var packet = ConfigSyncPacketMapper.ToPacket(StepUpOptions.Current);
        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            serverChannel.SendPacket(packet, player);
    }

    /// <summary>
    /// Pushes the current options to a single joining player.
    /// Fires unconditionally so clients always receive the current
    /// enforcement state, not just when enforcement is active.
    /// </summary>
    private void OnPlayerJoin(IServerPlayer player)
    {
        // Both are non-null after registration; null here indicates a registration-order bug.
        if (sapi == null || serverChannel == null) return;

        if (StepUpOptions.Current == null)
        {
            ModLog.Error(sapi, "Cannot process OnPlayerJoin: config is null.");
            return;
        }

        serverChannel.SendPacket(ConfigSyncPacketMapper.ToPacket(StepUpOptions.Current), player);
    }
}
