using System.Collections.Generic;
using StepUpAdvanced.Configuration;

namespace StepUpAdvanced.Infrastructure.Network;

/// <summary>
/// Bidirectional mapping between the persisted <see cref="StepUpOptions"/>
/// shape and the narrow <see cref="ConfigSyncPacket"/> wire shape.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToPacket"/> runs server-side just before broadcast — it
/// produces the wire DTO from the authoritative server config.
/// </para>
/// <para>
/// <see cref="Apply"/> runs client-side after receive — it merges the
/// ten wire fields into the client's existing <see cref="StepUpOptions"/>
/// instance, leaving every other field untouched. This is the central
/// invariant the type split enforces: client-local fields like
/// <c>StepHeight</c>, <c>StepSpeed</c>, <c>QuietMode</c>, and the probe
/// tunables survive a server push, where previously the wholesale
/// <c>ConfigStore.UpdateConfig(config)</c> replace clobbered them all.
/// </para>
/// <para>
/// The mapper is intentionally a static class with no fields and no
/// dependencies on VS APIs — it is fully unit-testable. Single-player
/// and integrated-server-host overrides (forcing
/// <c>ServerEnforceSettings = false</c>) live at the call site, not in
/// the mapper, because that override depends on the runtime side context
/// the mapper does not know about.
/// </para>
/// </remarks>
internal static class ConfigSyncPacketMapper
{
    /// <summary>
    /// Builds a wire packet from the current options. Pure function:
    /// does not mutate <paramref name="options"/>.
    /// </summary>
    public static ConfigSyncPacket ToPacket(StepUpOptions options) => new()
    {
        ServerEnforceSettings = options.ServerEnforceSettings,
        AllowClientChangeStepHeight = options.AllowClientChangeStepHeight,
        AllowClientChangeStepSpeed = options.AllowClientChangeStepSpeed,
        AllowClientConfigReload = options.AllowClientConfigReload,
        ServerMinStepHeight = options.ServerMinStepHeight,
        ServerMaxStepHeight = options.ServerMaxStepHeight,
        ServerMinStepSpeed = options.ServerMinStepSpeed,
        ServerMaxStepSpeed = options.ServerMaxStepSpeed,
        ShowServerEnforcedNotice = options.ShowServerEnforcedNotice,
        BlockBlacklist = options.BlockBlacklist,
    };

    /// <summary>
    /// Applies the wire packet's ten enforcement fields to
    /// <paramref name="current"/>, leaving all other fields unchanged.
    /// Replaces the previous wholesale <c>ConfigStore.UpdateConfig</c>
    /// call in the client receive handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Does not mutate <paramref name="packet"/>. Idempotent: applying
    /// the same packet twice produces the same end state.
    /// </para>
    /// <para>
    /// A null <see cref="ConfigSyncPacket.BlockBlacklist"/> on the wire
    /// collapses to a fresh empty list on <paramref name="current"/> —
    /// never to <c>null</c> — because downstream readers
    /// (<c>IsNearBlacklistedBlock</c>, <c>/sua list</c>) treat the field
    /// as a non-null collection and a null would force a defensive
    /// branch at every read site.
    /// </para>
    /// </remarks>
    public static void Apply(StepUpOptions current, ConfigSyncPacket packet)
    {
        current.ServerEnforceSettings = packet.ServerEnforceSettings;
        current.AllowClientChangeStepHeight = packet.AllowClientChangeStepHeight;
        current.AllowClientChangeStepSpeed = packet.AllowClientChangeStepSpeed;
        current.AllowClientConfigReload = packet.AllowClientConfigReload;
        current.ServerMinStepHeight = packet.ServerMinStepHeight;
        current.ServerMaxStepHeight = packet.ServerMaxStepHeight;
        current.ServerMinStepSpeed = packet.ServerMinStepSpeed;
        current.ServerMaxStepSpeed = packet.ServerMaxStepSpeed;
        current.ShowServerEnforcedNotice = packet.ShowServerEnforcedNotice;
        current.BlockBlacklist = packet.BlockBlacklist ?? new List<string>();
    }
}
