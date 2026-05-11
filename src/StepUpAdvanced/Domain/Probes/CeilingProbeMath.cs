using System;
using System.Collections.Generic;

namespace StepUpAdvanced.Domain.Probes;

/// <summary>
/// Framework-free 2D math for the ceiling-clearance and forward-column
/// probes. Returns value-tuple offsets rather than VS <c>BlockPos</c>
/// instances so the Domain layer stays independent of the VS API.
/// Callers compose <c>BlockPos</c> from <c>(dx, dz)</c> at the
/// world-query boundary.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate convention is inherited from the pre-Phase-5
/// <c>StepUpAdvancedModSystem.ForwardBlock</c>: <c>sx = sin(yaw) * dist</c>
/// and <c>sz = cos(yaw) * dist</c>, both rounded to int. This matches VS's
/// yaw convention (Y-up, X-east-ish, Z-south-ish) for the player's facing
/// direction at the cardinal angles.
/// </para>
/// <para>
/// Phase 5 keeps <see cref="ForwardSpanOffsets"/> allocating — it returns
/// a List wrapped in <see cref="IReadOnlyList{T}"/>. Phase 6 replaces the
/// per-tick allocation with a scratch buffer, but that's an infrastructure
/// concern (mutable state) that doesn't belong in pure math.
/// </para>
/// </remarks>
internal static class CeilingProbeMath
{
    /// <summary>
    /// Cardinal-snapped forward offset at the given yaw and distance.
    /// </summary>
    /// <remarks>
    /// The rounding behavior is deliberate: <c>(int)Math.Round(Math.Sin(yaw))</c>
    /// snaps to {-1, 0, 1}, which means an oblique yaw (e.g. 45°) collapses
    /// to a cardinal direction. This is what the pre-Phase-5 probe did and
    /// what the rest of the probe code relies on — a "forward direction" in
    /// units of whole-block offsets.
    /// </remarks>
    public static (int sx, int sz) ForwardOffset(double yawRad, int distance)
    {
        int sx = (int)Math.Round(Math.Sin(yawRad)) * distance;
        int sz = (int)Math.Round(Math.Cos(yawRad)) * distance;
        return (sx, sz);
    }

    /// <summary>
    /// Cardinal-snapped perpendicular unit offset, rotated 90° from the
    /// forward direction at this yaw. Used as the spanning axis for the
    /// forward-column fan-out — left/right of the player's facing.
    /// </summary>
    /// <remarks>
    /// Formula: take the unit forward <c>(fx, fz) = (round(sin), round(cos))</c>
    /// and rotate to <c>(-fz, fx)</c>. The rotation handedness matches the
    /// pre-Phase-5 <c>BuildForwardColumns</c>; "left" vs "right" is not
    /// meaningful here since the probe fans out symmetrically in both
    /// directions, just that the +/- sign is consistent across calls.
    /// </remarks>
    public static (int px, int pz) PerpendicularOffset(double yawRad)
    {
        int fx = (int)Math.Round(Math.Sin(yawRad));
        int fz = (int)Math.Round(Math.Cos(yawRad));
        return (-fz, fx);
    }

    /// <summary>
    /// Caps the requested step height to <paramref name="hardCap"/>
    /// whole blocks. Used by <c>HasLandingSupport</c> to decide how far
    /// down to scan for solid ground under the landing spot — there's no
    /// point looking deeper than the player could fall.
    /// </summary>
    public static int MaxRiseClamp(float requestedStep, int hardCap)
    {
        int floorOfStep = (int)Math.Floor(requestedStep);
        return floorOfStep < hardCap ? floorOfStep : hardCap;
    }

    /// <summary>
    /// Computes the inclusive Y-range (<c>[yFrom, yTopInclusive]</c>) of
    /// cells the probe needs to check above a prospective landing spot to
    /// confirm the player would fit there. Returns
    /// <c>yTopInclusive &lt; yFrom</c> when the entity is too short to
    /// need any clearance — callers should treat that as "no probe needed."
    /// </summary>
    /// <param name="baseY">Y of the player's feet block, pre-step.</param>
    /// <param name="requestedStep">Step height being attempted.</param>
    /// <param name="entityHeight">Player's collision-box height (Y2 - Y1).</param>
    /// <param name="headroomPad">
    ///   Cosmetic padding subtracted from the entity height — keeps the probe
    ///   from rejecting a landing just because the player's head is exactly
    ///   at the ceiling block boundary.
    /// </param>
    public static (int yFrom, int yTopInclusive) LandingClearanceRange(
        int baseY, float requestedStep, float entityHeight, float headroomPad)
    {
        int yFeetLanding = baseY + (int)Math.Floor(requestedStep);
        int yFrom = yFeetLanding + 1;
        int yTopInclusive = yFeetLanding + (int)Math.Floor(entityHeight - headroomPad);
        return (yFrom, yTopInclusive);
    }

    /// <summary>
    /// Builds the offsets for the forward-column fan-out:
    /// span=0 → 1 column (center), span=1 → 3 columns (center + ±1
    /// perpendicular), span=2 → 5 columns (center + ±1 + ±2). Offsets are
    /// returned as <c>(dx, dz)</c> deltas from the player's base position;
    /// callers compose absolute world coordinates.
    /// </summary>
    /// <remarks>
    /// Allocates a List per call. Phase 6 will replace this with a scratch
    /// buffer at the world-query boundary; the math itself doesn't change.
    /// </remarks>
    public static IReadOnlyList<(int dx, int dz)> ForwardSpanOffsets(
        double yawRad, int distance, int span)
    {
        var (fx, fz) = ForwardOffset(yawRad, distance);
        var center = (dx: fx, dz: fz);

        if (span <= 0)
        {
            return new[] { center };
        }

        var (px, pz) = PerpendicularOffset(yawRad);
        var list = new List<(int dx, int dz)>(span >= 2 ? 5 : 3)
        {
            center,
            (center.dx + px, center.dz + pz),
            (center.dx - px, center.dz - pz),
        };

        if (span >= 2)
        {
            list.Add((center.dx + 2 * px, center.dz + 2 * pz));
            list.Add((center.dx - 2 * px, center.dz - 2 * pz));
        }

        return list;
    }
}
