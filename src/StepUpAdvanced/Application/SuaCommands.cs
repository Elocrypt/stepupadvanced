using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Infrastructure.Probes;

namespace StepUpAdvanced.Application;

/// <summary>
/// Owns the entire <c>/sua</c> command surface — both the client-side
/// (<c>.sua</c>, chat privilege) and server-side (<c>/sua</c>,
/// controlserver privilege) command trees and every handler body.
/// </summary>
/// <remarks>
/// <para>
/// Phase 8 Step 8.10 extracts this from <c>StepUpAdvancedModSystem</c>,
/// which is now a thin composition root: it constructs this registrar and
/// calls <see cref="RegisterClient"/> (in <c>StartClientSide</c>) or
/// <see cref="RegisterServer"/> (in <c>StartServerSide</c>). Behavior is
/// preserved verbatim — a structural move, not a logic change.
/// </para>
/// <para>
/// Client and server run as SEPARATE ModSystem instances, so a given
/// <see cref="SuaCommands"/> instance services exactly one side: the
/// matching register method is called and only that side's handlers are
/// ever registered/invoked. The side-specific dependencies use the same
/// <c>null!</c> late-init pattern as the ModSystem's API references — VS
/// guarantees the register method runs before any handler fires, so the
/// other side's fields are never dereferenced.
/// </para>
/// <para>
/// The split between this class and <see cref="ServerEnforcementCoordinator"/>
/// is intentional: the coordinator owns the server-side mutation policy
/// (Suppress → Save → Broadcast → MarkDirty); the server handlers here are
/// thin adapters that validate the caller's block selection and delegate.
/// </para>
/// </remarks>
internal sealed class SuaCommands
{
    // Client-side dependencies (set by RegisterClient; unused on a server
    // instance). null! late-init: see class remarks.
    private ICoreClientAPI capi = null!;
    private WorldProbe worldProbe = null!;

    // Server-side dependencies (set by RegisterServer; unused on a client
    // instance).
    private ICoreServerAPI sapi = null!;
    private ServerEnforcementCoordinator serverCoordinator = null!;

    /// <summary>
    /// Registers the client-side <c>.sua</c> command tree. Called once from
    /// <c>StartClientSide</c> after the world probe is constructed.
    /// </summary>
    public void RegisterClient(ICoreClientAPI capi, WorldProbe worldProbe)
    {
        this.capi = capi;
        this.worldProbe = worldProbe;

        capi.ChatCommands.Create("sua").WithDescription(Lang.Get("desc.sua.client")).RequiresPrivilege("chat")
            .BeginSubCommand("add")
            .WithDescription(Lang.Get("desc.sua.add"))
            .HandleWith(AddToBlacklist)
            .EndSubCommand()
            .BeginSubCommand("remove")
            .WithDescription(Lang.Get("desc.sua.remove"))
            .HandleWith(RemoveFromBlacklist)
            // Nested 'all' sub-command — '.sua remove all' clears the
            // entire client-side list. Independent of the server-side
            // '/sua remove all'; the client config is owned locally and
            // is never touched by server admin actions.
            .BeginSubCommand("all")
            .WithDescription(Lang.Get("desc.sua.remove-all-client"))
            .HandleWith(ClearClientBlacklist)
            .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("list")
            .WithDescription(Lang.Get("desc.sua.list"))
            .HandleWith(ListBlacklist)
            .EndSubCommand();
    }

    /// <summary>
    /// Registers the server-side <c>/sua</c> command tree. Called once from
    /// <c>StartServerSide</c> after the enforcement coordinator is constructed.
    /// </summary>
    public void RegisterServer(ICoreServerAPI sapi, ServerEnforcementCoordinator serverCoordinator)
    {
        this.sapi = sapi;
        this.serverCoordinator = serverCoordinator;

        sapi.ChatCommands.Create("sua").WithDescription(Lang.Get("desc.sua.server")).RequiresPrivilege("controlserver")
            .BeginSubCommand("add")
            .WithDescription(Lang.Get("desc.sua.add"))
            .HandleWith(AddToServerBlacklist)
            .EndSubCommand()
            .BeginSubCommand("remove")
            .WithDescription(Lang.Get("desc.sua.remove"))
            .HandleWith(RemoveFromServerBlacklist)
            // Nested 'all' sub-command — '/sua remove all' clears the
            // entire server-side list. Pointedly does NOT touch any
            // connected client's local list (those live in a separate
            // config that the client owns).
            .BeginSubCommand("all")
            .WithDescription(Lang.Get("desc.sua.remove-all-server"))
            .HandleWith(ClearServerBlacklist)
            .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("reload")
            .WithDescription(Lang.Get("desc.sua.reload"))
            .HandleWith(ReloadServerConfig)
            .EndSubCommand();
    }

    // ─── Client handlers ────────────────────────────────────────────────

    /// <summary>
    /// Returns the calling player cast to <see cref="IClientPlayer"/>, or
    /// <c>null</c> if the caller isn't a player (console, server, etc.).
    /// Emits a chat error toast in the null case so the caller doesn't
    /// need to.
    /// </summary>
    private IClientPlayer? ValidatePlayer(TextCommandCallingArgs args, out int groupId)
    {
        groupId = args.Caller.FromChatGroupId;
        if (!(args.Caller.Player is IClientPlayer result))
        {
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Err(ChatFormatting.L("cmd.must-be-player"))}");
            return null;
        }
        return result;
    }

    private TextCommandResult AddToBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer? clientPlayer = ValidatePlayer(arg, out groupId);
        if (clientPlayer == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
            return CommandResults.Err(ChatFormatting.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (BlockBlacklistOptions.Current.BlockCodes.Contains(blockCode))
            return CommandResults.Warn(ChatFormatting.L("cmd.already-listed"), $"{ChatFormatting.Val(blockCode)} {ChatFormatting.Muted(ChatFormatting.L("cmd.is-on-blacklist"))}");

        BlockBlacklistOptions.Current.BlockCodes.Add(blockCode);
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.added-client-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");
    }

    private TextCommandResult RemoveFromBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer? clientPlayer = ValidatePlayer(arg, out groupId);
        if (clientPlayer == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
            return CommandResults.Err(ChatFormatting.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (!BlockBlacklistOptions.Current.BlockCodes.Contains(blockCode))
            return CommandResults.Warn(ChatFormatting.L("cmd.not-listed-client"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");

        BlockBlacklistOptions.Current.BlockCodes.Remove(blockCode);
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.removed-client-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(blockCode)}");
    }

    /// <summary>
    /// Handler for <c>.sua remove all</c>. Wipes the local client-side
    /// blacklist (<c>BlockBlacklistOptions.Current.BlockCodes</c>).
    /// Independent of the server-side blacklist; <c>/sua remove all</c>
    /// on the server-side never touches this list.
    /// </summary>
    private TextCommandResult ClearClientBlacklist(TextCommandCallingArgs arg)
    {
        IClientPlayer? clientPlayer = ValidatePlayer(arg, out _);
        if (clientPlayer == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));

        var list = BlockBlacklistOptions.Current?.BlockCodes;
        int removed = list?.Count ?? 0;

        if (removed == 0)
            return CommandResults.Info(ChatFormatting.L("cmd.blacklist-already-empty-client"));

        list!.Clear();
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return CommandResults.Ok(ChatFormatting.L("cmd.cleared-client-blacklist"), $"{ChatFormatting.Arrow}{ChatFormatting.Val(removed.ToString())}");
    }

    private TextCommandResult ListBlacklist(TextCommandCallingArgs arg)
    {
        var serverList = StepUpOptions.Current?.BlockBlacklist ?? new List<string>();
        var clientList = BlockBlacklistOptions.Current?.BlockCodes ?? new List<string>();

        var merged = new HashSet<string>(serverList);
        merged.UnionWith(clientList);

        if (merged.Count == 0)
            return CommandResults.Info(ChatFormatting.L("cmd.none-blacklisted"));

        var sorted = merged.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        return CommandResults.List(ChatFormatting.L("cmd.blacklisted-blocks-title"), sorted);
    }

    // ─── Server handlers ────────────────────────────────────────────────

    private TextCommandResult AddToServerBlacklist(TextCommandCallingArgs arg)
    {
        if (serverCoordinator == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.server-api-not-initialized")));
        var player = arg.Caller.Player as IServerPlayer;
        if (player == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));
        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null)
            return CommandResults.Err(ChatFormatting.L("cmd.no-block-targeted"));
        return serverCoordinator.AddToBlacklist(blockSel.Block.Code.ToString());
    }

    private TextCommandResult RemoveFromServerBlacklist(TextCommandCallingArgs arg)
    {
        if (serverCoordinator == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.server-api-not-initialized")));
        var player = arg.Caller.Player as IServerPlayer;
        if (player == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));
        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null)
            return CommandResults.Err(ChatFormatting.L("cmd.no-block-targeted"));
        return serverCoordinator.RemoveFromBlacklist(blockSel.Block.Code.ToString());
    }

    /// <summary>
    /// Handler for <c>/sua remove all</c>. Pointedly does NOT touch any
    /// client's local blacklist — <c>BlockBlacklistOptions</c> is owned
    /// by the client and lives in a separate config file.
    /// </summary>
    private TextCommandResult ClearServerBlacklist(TextCommandCallingArgs arg)
    {
        if (serverCoordinator == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.server-api-not-initialized")));
        return serverCoordinator.ClearBlacklist();
    }

    private TextCommandResult ReloadServerConfig(TextCommandCallingArgs arg)
    {
        if (serverCoordinator == null)
            return CommandResults.Err(ChatFormatting.L("cmd.failed"), ChatFormatting.Muted(ChatFormatting.L("cmd.must-be-player")));
        return serverCoordinator.ReloadFromCommand();
    }
}
