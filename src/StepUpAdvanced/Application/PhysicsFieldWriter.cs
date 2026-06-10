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
/// <para>
/// Phase 7a extracts this from <c>StepUpAdvancedModSystem</c>. Behavior
/// is preserved verbatim — the writer is a structural move, not a logic
/// change. The lazy-init still binds against <c>phys.GetType()</c> (the
/// runtime type, not <see cref="EntityBehaviorControlledPhysics"/>
/// itself) so a subclass that shadows <c>StepHeight</c> or
/// <c>elevateFactor</c> resolves to its own field, not the base's.
/// </para>
/// <para>
/// The threshold no-op check stays inside the writer because the cache
/// lives here. From the caller's perspective <see cref="WriteStepHeight"/>
/// and <see cref="WriteElevateFactor"/> return <c>true</c> on both
/// "wrote successfully" and "skipped, no change"; they return
/// <c>false</c> only when the accessor could not resolve the field.
/// Callers gate their one-time field-missing diagnostic on the
/// <c>false</c> branch.
/// </para>
/// </remarks>
internal sealed class PhysicsFieldWriter
{
    // Reserved for future diagnostic emission. Phase 7a deliberately
    // keeps the field-missing warnings on the controllers (they own
    // their warned-once flags), so this reference is currently unused.
    // Stored for API stability — Phase 8+ may move diagnostic emission
    // here once the controllers are settled.
    private readonly ICoreClientAPI capi;

    // Lazy-initialized on first write so we can resolve fields against
    // the runtime type of the physics behavior — a subclass may shadow
    // StepHeight / elevateFactor.
    private FieldAccessor<EntityBehaviorControlledPhysics, float>? stepHeightAccessor;
    private FieldAccessor<EntityBehaviorControlledPhysics, double>? elevateAccessor;

    // Per-field idempotency. StepHeight is verified by READ-BACK against the
    // live field (see WriteStepHeight), so it keeps no cached "last written"
    // value — that cache was the #6 drift bug: an external reset of the physics
    // behavior left the cache thinking the field was already correct, so we
    // stopped re-applying. The (vestigial) elevate path keeps its value cache;
    // nothing in the player path reads elevateFactor, so its drift is moot.
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
    /// <remarks>
    /// StepHeight no longer participates: it is read-back verified on every
    /// write (see <see cref="WriteStepHeight"/>), so it re-asserts itself
    /// against the live field without needing the cache cleared. The call is
    /// retained for the (vestigial) elevate path and as a stable API for the
    /// reload site.
    /// </remarks>
    public void InvalidateCache()
    {
        lastAppliedElevate = double.NaN;
    }
}
