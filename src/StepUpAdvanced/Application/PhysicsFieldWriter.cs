using System;
using StepUpAdvanced.Infrastructure.Reflection;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace StepUpAdvanced.Application;

/// <summary>
/// Owns the two compiled-delegate field accessors that write into the VS
/// player physics behavior, plus their per-field idempotency caches.
/// Single class because the two fields are peers — same target type
/// (<see cref="EntityBehaviorControlledPhysics"/>), same access pattern
/// (lazy-init against the runtime type, write-once-per-change), same
/// cache lifetime (invalidated together on config reload).
/// </summary>
/// <remarks>
/// Lazy-init binds against <c>phys.GetType()</c> (the runtime type) so a
/// subclass that shadows <c>StepHeight</c> resolves to its own field.
/// Both write methods return <c>true</c> on success or no-op, <c>false</c>
/// only when the field could not be resolved.
/// </remarks>
internal sealed class PhysicsFieldWriter
{
    private readonly ICoreClientAPI capi;

    // Lazy-initialized on first write so we can resolve fields against
    // the runtime type of the physics behavior — a subclass may shadow
    // StepHeight / elevateFactor.
    private FieldAccessor<EntityBehaviorControlledPhysics, float>? stepHeightAccessor;
    private FieldAccessor<EntityBehaviorControlledPhysics, double>? elevateAccessor;

    // StepHeight uses read-back (see WriteStepHeight) so it has no cache.
    // The elevate path keeps a value cache; nothing in the player path reads
    // elevateFactor, so drift there doesn't matter.
    private double lastAppliedElevate = double.NaN;

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

    /// <summary>
    /// Writes <paramref name="value"/> to the physics behavior's
    /// elevate-factor field. Returns <c>true</c> on a successful write or
    /// no-op; <c>false</c> when the field could not be resolved.
    /// </summary>
    public bool WriteElevateFactor(EntityBehaviorControlledPhysics phys, double value)
    {
        if (Math.Abs(value - lastAppliedElevate) < 1e-6) return true;

        // 'elevateFactor' is the canonical lowercase VS field name;
        // 'ElevateFactor' is a defensive fallback for future renames.
        elevateAccessor ??= new FieldAccessor<EntityBehaviorControlledPhysics, double>(
            phys.GetType(), "elevateFactor", "ElevateFactor");

        if (!elevateAccessor.TrySet(phys, value)) return false;
        lastAppliedElevate = value;
        return true;
    }

    /// <summary>
    /// Resets the elevate idempotency cache so the next elevate write fires
    /// unconditionally. Called by the config-reload path.
    /// </summary>

    public void InvalidateCache()
    {
        lastAppliedElevate = double.NaN;
    }
}
