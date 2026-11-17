using System;
using StepUpAdvanced.Infrastructure.Reflection;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace StepUpAdvanced.Application;

/// <summary>
/// Owns the compiled-delegate field accessor that writes the player's step
/// height into the VS physics behavior.
/// </summary>
/// <remarks>
/// Lazy-init binds against <c>phys.GetType()</c> (the runtime type) so a
/// subclass that shadows <c>StepHeight</c> resolves to its own field.
/// Step speed is not written here: VS keeps it in a local variable inside
/// <c>TryStepSmooth</c>, so it is scaled by the Harmony transpiler instead.
/// </remarks>
internal sealed class PhysicsFieldWriter
{
    private readonly ICoreClientAPI capi;

    // Lazy-initialized on first write so the field resolves against the
    // runtime type of the physics behavior (a subclass may shadow StepHeight).
    // No value cache: WriteStepHeight decides by read-back.
    private FieldAccessor<EntityBehaviorControlledPhysics, float>? stepHeightAccessor;

    public PhysicsFieldWriter(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the physics behavior's
    /// step-height field. Returns <c>true</c> on a successful write or
    /// when the write was a no-op (the live field already holds
    /// <paramref name="value"/> within the epsilon threshold); <c>false</c>
    /// only when the field could not be resolved on the target's runtime type.
    /// </summary>
    /// <remarks>
    /// Idempotency is decided by READ-BACK: the desired value is compared
    /// against the behavior's CURRENT field value, not a cached last-written
    /// value. This makes the writer self-correcting — if the physics behavior
    /// is reset out from under us (respawn, dimension change, another mod
    /// re-initializing it, the field zeroed back to vanilla 0.6), the read-back
    /// sees the drift and re-asserts our value on the very next tick, instead
    /// of a stale cache suppressing the write indefinitely. The reflected read
    /// is a compiled delegate (no boxing), so the per-tick cost is negligible.
    /// </remarks>
    public bool WriteStepHeight(EntityBehaviorControlledPhysics phys, float value)
    {
        // VS API has historically used both 'StepHeight' (public) and
        // 'stepHeight' (private) across versions; first match wins.
        stepHeightAccessor ??= new FieldAccessor<EntityBehaviorControlledPhysics, float>(
            phys.GetType(), "StepHeight", "stepHeight");

        if (!stepHeightAccessor.IsAvailable) return false;

        // No-op only when the LIVE field already matches — never on the basis
        // of what we last wrote.
        if (stepHeightAccessor.TryGet(phys, out float current) &&
            Math.Abs(current - value) < 1e-4f)
        {
            return true;
        }

        return stepHeightAccessor.TrySet(phys, value);
    }
}
