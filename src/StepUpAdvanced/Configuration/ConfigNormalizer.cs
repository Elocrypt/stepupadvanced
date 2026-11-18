using System.Collections.Generic;
using StepUpAdvanced.Domain.Physics;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Load-time validation for the two config files. Corrects out-of-range
/// values in place and reports each correction as a message for the caller
/// to log once.
/// </summary>
/// <remarks>
/// <para>
/// Pure and framework-free so it can be unit-tested. Idempotent: running it
/// on its own output returns <c>false</c> and adds no messages, which is what
/// keeps a corrected file from being rewritten (and the watcher re-triggered)
/// on every load.
/// </para>
/// <para>
/// The client side checks only its own floors. Server caps are not applied to
/// <c>StepHeight</c>, <c>StepSpeed</c> or <c>DefaultHeight</c> here: they
/// belong to the server, arrive by packet, and are applied at runtime by the
/// controllers. Clamping them into the player's file would permanently lower
/// the player's own settings because of one server's limits.
/// </para>
/// </remarks>
internal static class ConfigNormalizer
{
    /// <summary>Hard ceilings for server caps; protects against pathological values.</summary>
    internal const float ServerCapMinHeight = StepHeightClamp.ClientMin;
    internal const float ServerCapMaxHeight = 20f;
    internal const float ServerCapMinSpeed = ElevateFactorMath.ClientMin;
    internal const float ServerCapMaxSpeed = 20f;

    internal const float MinIncrement = 0.1f;
    internal const int MaxForwardProbeDistance = 4;
    internal const int MaxForwardProbeSpan = 2;
    internal const float MaxCeilingHeadroomPad = 0.4f;

    /// <summary>Validates the server file. Returns <c>true</c> if anything changed.</summary>
    public static bool NormalizeServer(StepUpServerOptions s, List<string> messages)
    {
        bool changed = false;

        if (s.BlockBlacklist == null)
        {
            s.BlockBlacklist = new List<string>();
            changed = true;
        }

        // Zero or negative (or missing/NaN) caps fall back to the shipped
        // defaults rather than the hard floor: a cap of 0 means "unset", not
        // "as low as possible".
        var defaults = new StepUpServerOptions();
        changed |= DefaultIfUnset(s.ServerMinStepHeight, defaults.ServerMinStepHeight, v => s.ServerMinStepHeight = v, "ServerMinStepHeight", messages);
        changed |= DefaultIfUnset(s.ServerMaxStepHeight, defaults.ServerMaxStepHeight, v => s.ServerMaxStepHeight = v, "ServerMaxStepHeight", messages);
        changed |= DefaultIfUnset(s.ServerMinStepSpeed, defaults.ServerMinStepSpeed, v => s.ServerMinStepSpeed = v, "ServerMinStepSpeed", messages);
        changed |= DefaultIfUnset(s.ServerMaxStepSpeed, defaults.ServerMaxStepSpeed, v => s.ServerMaxStepSpeed = v, "ServerMaxStepSpeed", messages);

        changed |= Cap(s.ServerMinStepHeight, ServerCapMinHeight, ServerCapMaxHeight, v => s.ServerMinStepHeight = v, "ServerMinStepHeight", messages);
        changed |= Cap(s.ServerMaxStepHeight, ServerCapMinHeight, ServerCapMaxHeight, v => s.ServerMaxStepHeight = v, "ServerMaxStepHeight", messages);
        changed |= Cap(s.ServerMinStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed, v => s.ServerMinStepSpeed = v, "ServerMinStepSpeed", messages);
        changed |= Cap(s.ServerMaxStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed, v => s.ServerMaxStepSpeed = v, "ServerMaxStepSpeed", messages);

        // Swap rather than collapse: an admin who typed the two values the
        // wrong way round almost certainly meant the range, not a single point.
        if (s.ServerMinStepHeight > s.ServerMaxStepHeight)
        {
            (s.ServerMinStepHeight, s.ServerMaxStepHeight) = (s.ServerMaxStepHeight, s.ServerMinStepHeight);
            messages.Add($"ServerMinStepHeight was above ServerMaxStepHeight. Swapped them ({s.ServerMinStepHeight}..{s.ServerMaxStepHeight}).");
            changed = true;
        }
        if (s.ServerMinStepSpeed > s.ServerMaxStepSpeed)
        {
            (s.ServerMinStepSpeed, s.ServerMaxStepSpeed) = (s.ServerMaxStepSpeed, s.ServerMinStepSpeed);
            messages.Add($"ServerMinStepSpeed was above ServerMaxStepSpeed. Swapped them ({s.ServerMinStepSpeed}..{s.ServerMaxStepSpeed}).");
            changed = true;
        }

        return changed;
    }

    /// <summary>Validates the client file. Returns <c>true</c> if anything changed.</summary>
    public static bool NormalizeClient(StepUpClientOptions c, List<string> messages)
    {
        bool changed = false;

        changed |= Floor(c.StepHeightIncrement, MinIncrement, v => c.StepHeightIncrement = v, "StepHeightIncrement", messages);
        changed |= Floor(c.StepSpeedIncrement, MinIncrement, v => c.StepSpeedIncrement = v, "StepSpeedIncrement", messages);
        changed |= Floor(c.StepHeight, StepHeightClamp.ClientMin, v => c.StepHeight = v, "StepHeight", messages);
        changed |= Floor(c.StepSpeed, ElevateFactorMath.ClientMin, v => c.StepSpeed = v, "StepSpeed", messages);
        changed |= Floor(c.DefaultHeight, StepHeightClamp.ClientMin, v => c.DefaultHeight = v, "DefaultHeight", messages);

        // A distance or span of 0 disables the forward probe entirely; the
        // feature is turned off with ForwardProbeCeiling instead.
        changed |= CapInt(c.ForwardProbeDistance, 1, MaxForwardProbeDistance, v => c.ForwardProbeDistance = v, "ForwardProbeDistance", messages);
        changed |= CapInt(c.ForwardProbeSpan, 1, MaxForwardProbeSpan, v => c.ForwardProbeSpan = v, "ForwardProbeSpan", messages);
        changed |= Cap(c.CeilingHeadroomPad, 0f, MaxCeilingHeadroomPad, v => c.CeilingHeadroomPad = v, "CeilingHeadroomPad", messages);

        return changed;
    }

    private static bool DefaultIfUnset(float value, float fallback, System.Action<float> set, string name, List<string> messages)
    {
        if (value > 0f) return false;
        set(fallback);
        messages.Add($"{name} was not set (0 or less). Set to the default ({fallback}).");
        return true;
    }

    private static bool Floor(float value, float min, System.Action<float> set, string name, List<string> messages)
    {
        // NaN fails every comparison, so test for "not at least min" rather than "below min".
        if (value >= min) return false;
        set(min);
        messages.Add($"{name} was below the minimum ({min}). Set to {min}.");
        return true;
    }

    private static bool Cap(float value, float min, float max, System.Action<float> set, string name, List<string> messages)
    {
        float clamped = !(value >= min) ? min : value > max ? max : value;
        if (clamped == value) return false;
        set(clamped);
        messages.Add($"{name} was outside {min}..{max}. Set to {clamped}.");
        return true;
    }

    private static bool CapInt(int value, int min, int max, System.Action<int> set, string name, List<string> messages)
    {
        int clamped = value < min ? min : value > max ? max : value;
        if (clamped == value) return false;
        set(clamped);
        messages.Add($"{name} was outside {min}..{max}. Set to {clamped}.");
        return true;
    }
}
