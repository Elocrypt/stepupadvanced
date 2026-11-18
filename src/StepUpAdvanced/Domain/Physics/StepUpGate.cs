namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Pure gate deciding whether the mod's <i>enhanced</i> step height applies on
/// a given tick, based on the player's current sprint/sneak control state and
/// the two opt-in feel flags (<c>SprintOnlyStepUp</c>,
/// <c>DisableStepUpWhileSneaking</c>). Framework-free so it pins under test
/// without any VS API surface.
/// </summary>
/// <remarks>
/// All flags default off, so the common case returns <c>true</c> unconditionally
/// until a user opts in. Sprint and sneak are mutually exclusive in practice but
/// are evaluated independently so the gate stays correct regardless.
/// Scope is the height axis only — the rise-speed axis differentiates
/// sprint/sneak/walk via its own elevateFactor branches.
/// </remarks>
internal static class StepUpGate
{
    /// <summary>
    /// Combines the player's own gate flags with what an enforcing server
    /// requires. A server can only add restrictions: each effective flag is
    /// the player's choice OR the server's requirement, and the server side
    /// counts only while enforcement is in force.
    /// </summary>
    /// <remarks>
    /// Sneak suppression has no server counterpart on purpose. It only
    /// changes feel, never what a player can reach, so it stays the player's call.
    /// </remarks>
    public static GateFlags ResolveFlags(
        bool sprintOnly,
        bool disableWhileSneaking,
        bool disableWhileAirborne,
        bool enforced,
        bool serverRequireSprintOnly,
        bool serverRequireNoAirborne)
        => new(
            SprintOnly: sprintOnly || (enforced && serverRequireSprintOnly),
            DisableWhileSneaking: disableWhileSneaking,
            DisableWhileAirborne: disableWhileAirborne || (enforced && serverRequireNoAirborne));

    /// <summary>
    /// Returns <c>true</c> when the mod's enhanced step height should apply
    /// this tick, or <c>false</c> when the height path should fall back to
    /// VS's vanilla baseline.
    /// </summary>
    /// <param name="sprintOnly">
    ///   <c>StepUpOptions.SprintOnlyStepUp</c>: when on, the enhanced step
    ///   height applies only while the player is sprinting.
    /// </param>
    /// <param name="disableWhileSneaking">
    ///   <c>StepUpOptions.DisableStepUpWhileSneaking</c>: when on, the enhanced
    ///   step height is suppressed while the player is sneaking.
    /// </param>
    /// <param name="disableWhileAirborne">
    ///   <c>StepUpOptions.DisableStepUpWhileAirborne</c>: when on, the enhanced
    ///   step height is suppressed while the player is airborne (neither on
    ///   ground nor swimming).
    /// </param>
    /// <param name="sprint">Player's current <c>EntityControls.Sprint</c>.</param>
    /// <param name="sneak">Player's current <c>EntityControls.Sneak</c>.</param>
    /// <param name="onGround">Player's current <c>Entity.OnGround</c>.</param>
    /// <param name="swimming">Player's current <c>Entity.Swimming</c>.</param>
    public static bool ShouldApplyStepUp(
        bool sprintOnly,
        bool disableWhileSneaking,
        bool disableWhileAirborne,
        bool sprint,
        bool sneak,
        bool onGround,
        bool swimming)
    {
        // Sneak suppression wins: if the user disabled step-up while sneaking,
        // sneaking closes the gate even if the player is somehow also flagged
        // sprinting. (See the precedence note on DisableStepUpWhileSneaking.)
        if (disableWhileSneaking && sneak) return false;

        // Sprint-only: closed whenever the player is not sprinting.
        if (sprintOnly && !sprint) return false;

        // Airborne suppression mirrors vanilla's own step gate condition
        // (!OnGround && !Swimming): swimming is not "airborne".
        if (disableWhileAirborne && !onGround && !swimming) return false;

        return true;
    }
}

/// <summary>Effective gate flags for one tick, after server requirements are applied.</summary>
internal readonly record struct GateFlags(bool SprintOnly, bool DisableWhileSneaking, bool DisableWhileAirborne);
