using StepUpAdvanced.Domain.Physics;

namespace StepUpAdvanced.Core;

/// <summary>
/// The rise-speed multiplier currently in effect, as read by the Harmony
/// getters in <c>EntityBehaviorPlayerPhysicsPatch</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ElevateFactorController</c> resolves the value (Basic mode, toggle,
/// enforcement caps) whenever one of its inputs changes and publishes it
/// here; the getters only read it. Resolving on change rather than per call
/// keeps the physics hot path to a single field read, and means every rule
/// is applied in one place instead of being re-derived inside the patch.
/// </para>
/// <para>
/// Starts at vanilla speed so nothing is scaled before the controller's
/// first publish. <c>volatile</c> because it is written on the client main
/// thread and read from physics code.
/// </para>
/// </remarks>
internal static class StepSpeedState
{
    private static volatile float effective = ElevateFactorMath.Vanilla;

    /// <summary>The multiplier applied to vanilla rise speed (1.0 = vanilla).</summary>
    public static float Effective => effective;

    /// <summary>Publishes a newly resolved multiplier.</summary>
    public static void Publish(float value) => effective = value;
}
