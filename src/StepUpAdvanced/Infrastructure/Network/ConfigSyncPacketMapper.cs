using System.Collections.Generic;
using StepUpAdvanced.Configuration;

namespace StepUpAdvanced.Infrastructure.Network;

/// <summary>
/// Bidirectional mapping between the persisted <see cref="StepUpOptions"/>
/// shape and the narrow <see cref="ConfigSyncPacket"/> wire shape.
/// </summary>
/// <remarks>
/// <see cref="ToPacket"/> runs server-side; <see cref="Apply"/> runs
/// client-side and merges only the enforcement fields, leaving every
/// client-local field (StepHeight, StepSpeed, QuietMode, probe tunables)
/// untouched. Static with no VS API dependencies — fully unit-testable.
/// </remarks>
internal static class ConfigSyncPacketMapper
{
    /// <summary>Builds a wire packet from the current options. Does not mutate <paramref name="options"/>.</summary>
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
    /// Applies the wire packet's enforcement fields to <paramref name="current"/>,
    /// leaving all client-local fields unchanged.
    /// </summary>
    /// <remarks>
    /// Idempotent. A null <see cref="ConfigSyncPacket.BlockBlacklist"/> on
    /// the wire collapses to an empty list — never <c>null</c> — so downstream
    /// readers don't need to null-check the field.
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
