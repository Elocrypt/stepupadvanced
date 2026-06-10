using System;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Infrastructure.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace StepUpAdvanced.Application;

/// <summary>
/// Server-side broadcaster + <c>/sua</c> command body owner. Consolidates
/// the four "mutate the server-authoritative blacklist + push to clients"
/// flows and the two reload flows under one class with a consistent
/// pipeline: Suppress → Save (or LoadOrUpgrade) → Broadcast → MarkDirty.
/// </summary>
/// <remarks>
/// <para>
/// Command handlers validate input (<c>TextCommandCallingArgs</c>, block
/// selection) and delegate here; the coordinator is free of command-framework
/// types so its logic can be tested independently.
/// </para>
/// <para>
/// <b>MarkDirty is a closure, not a WorldProbe reference.</b>
/// <c>StartServerSide</c> runs before <c>StartClientSide</c> in VS, so the
/// ModSystem's <c>worldProbe</c> field is null when the coordinator is
/// constructed. A closure resolves it at call-time instead.
/// </para>
/// <para>
/// <b>Two reload entry points</b> with different suppress semantics:
/// <c>/sua reload</c> suppresses the watcher (we're about to write);
/// the watcher event does not (the triggering write is already past).
/// </para>
/// </remarks>
internal sealed class ServerEnforcementCoordinator
{
    /// <summary>
    /// Watcher-suppress window in milliseconds. Long enough to cover the
    /// disk write that <see cref="ConfigStore.Save"/> issues without
    /// triggering <c>OnConfigFileChanged</c> in a feedback loop.
    /// </summary>
    private const int SuppressWindowMs = 150;

    private readonly ICoreServerAPI sapi;
    private readonly DebouncedConfigWatcher watcher;
    private readonly ConfigSyncChannel channel;

    // Late-bound: closes over the ModSystem's worldProbe field reference
    // so MarkDirty resolves the current value at call time. In SP this
    // sees the client-side worldProbe once StartClientSide has run; on
    // a dedicated server the field stays null and the closure no-ops.
    private readonly Action markBlacklistDirty;

    public ServerEnforcementCoordinator(
        ICoreServerAPI sapi,
        DebouncedConfigWatcher watcher,
        ConfigSyncChannel channel,
        Action markBlacklistDirty)
    {
        this.sapi = sapi;
        this.watcher = watcher;
        this.channel = channel;
        this.markBlacklistDirty = markBlacklistDirty;
    }

    /// <summary>
    /// Adds <paramref name="blockCode"/> to the server-authoritative
    /// <see cref="StepUpOptions.BlockBlacklist"/>. Idempotent: if the
    /// code is already present, returns a Warn result without mutating
    /// or broadcasting.
    /// </summary>
    public TextCommandResult AddToBlacklist(string blockCode)
    {
        if (StepUpOptions.Current.BlockBlacklist.Contains(blockCode))
            return CommandResults.Warn(ChatFormatting.L("cmd.already-listed-server"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");

        StepUpOptions.Current.BlockBlacklist.Add(blockCode);
        PersistAndBroadcast();
        markBlacklistDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.added-server-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");
    }

    /// <summary>
    /// Removes <paramref name="blockCode"/> from the server blacklist.
    /// Idempotent: if the code isn't present, returns a Warn result
    /// without mutating or broadcasting.
    /// </summary>
    public TextCommandResult RemoveFromBlacklist(string blockCode)
    {
        if (!StepUpOptions.Current.BlockBlacklist.Contains(blockCode))
            return CommandResults.Warn(ChatFormatting.L("cmd.not-on-server-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");

        StepUpOptions.Current.BlockBlacklist.Remove(blockCode);
        PersistAndBroadcast();
        markBlacklistDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.removed-server-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");
    }

    /// <summary>
    /// Clears the entire server blacklist. No-ops (with an info result) if
    /// already empty. Does not touch any client's local blacklist.
    /// </summary>
    public TextCommandResult ClearBlacklist()
    {
        var list = StepUpOptions.Current?.BlockBlacklist;
        int removed = list?.Count ?? 0;

        if (removed == 0)
            return CommandResults.Info(ChatFormatting.L("cmd.blacklist-already-empty-server"));

        list!.Clear();
        PersistAndBroadcast();
        markBlacklistDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.cleared-server-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(removed.ToString())}");
    }

    /// <summary>
    /// <c>/sua reload</c> command body. Suppresses the watcher, reloads from
    /// disk, and broadcasts to all clients. The broadcast's receive handler
    /// marks the probe cache dirty, so no explicit call is needed here.
    /// </summary>
    public TextCommandResult ReloadFromCommand()
    {
        watcher.Suppress(SuppressWindowMs);
        ConfigStore.LoadOrUpgrade(sapi);
        channel.BroadcastToAll();

        if (StepUpOptions.Current.ServerEnforceSettings)
            return CommandResults.Ok(ChatFormatting.L("cmd.server-config-reloaded"),
                $"{ChatFormatting.Muted(ChatFormatting.L("cmd.pushed"))} {ChatFormatting.Warn(ChatFormatting.L("cmd.enforced"))} {ChatFormatting.Muted(ChatFormatting.L("cmd.config"))}");

        return CommandResults.Ok(ChatFormatting.L("cmd.server-config-reloaded"), ChatFormatting.Muted(ChatFormatting.L("cmd.clients-may-use-local")));
    }

    /// <summary>
    /// File-watcher event handler. Triggered by
    /// <see cref="DebouncedConfigWatcher.ConfigFileChanged"/> after an
    /// external edit to the config file (debounced). Does NOT Suppress
    /// — the triggering write is already past, and suppressing now
    /// would silence the next 150 ms of legitimate external edits.
    /// </summary>
    public void ReloadFromWatcher()
    {
        ModLog.Event(sapi, "Detected config file change. Reloading...");
        ConfigStore.LoadOrUpgrade(sapi);
        channel.BroadcastToAll();

        if (StepUpOptions.Current.ServerEnforceSettings)
            ModLog.Verbose(sapi, "Server-enforced config pushed to all clients.");
        else
            ModLog.Verbose(sapi, "Config pushed (server enforcement disabled, allows client-side config again).");
    }

    /// <summary>
    /// Shared tail for the three mutation methods. The Suppress + Save
    /// + Broadcast sequence is identical across Add/Remove/Clear; only
    /// the locale keys and the input checks differ, so those stay
    /// at the call site.
    /// </summary>
    private void PersistAndBroadcast()
    {
        watcher.Suppress(SuppressWindowMs);
        ConfigStore.Save(sapi);
        // Watcher is suppressed so OnConfigFileChanged won't fire for
        // this write — broadcast explicitly so connected clients see
        // the new state without waiting for /sua reload or reconnect.
        channel.BroadcastToAll();
    }
}
