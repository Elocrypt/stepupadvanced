namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Pure gate deciding whether the mod's <i>enhanced</i> step height applies on
/// a given tick, based on the player's current sprint/sneak control state and
/// the two opt-in feel flags (<c>SprintOnlyStepUp</c>,
/// <c>DisableStepUpWhileSneaking</c>). Framework-free so it pins under test
/// without any VS API surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behavior preservation:</b> both flags default off, so the common case
/// (<c>sprintOnly == false</c> and <c>disableWhileSneaking == false</c>)
/// returns <c>true</c> unconditionally — the height path behaves exactly as it
/// did before this gate existed until a user opts in.
/// </para>
/// <para>
/// <b>Why a shared helper</b> rather than inline <c>controls.Sprint</c> checks:
/// the sprint-only, disable-while-sneaking, and disable-while-airborne features
/// all decide the same question — "should the enhanced step height apply this
/// tick" — off the player's live control and ground state. Centralizing the
/// policy keeps the height path's composition readable and gives the
/// sprint/sneak/ground interaction a single tested source of truth. Sprint and
/// sneak are mutually exclusive in practice (VS won't set both at once), but the
/// gate treats every input independently so it stays correct even if that
/// invariant ever changes.
/// </para>
/// <para>
/// <b>Scope — height axis only.</b> The rise-speed axis lives in the
/// Harmony-patched <c>EntityBehaviorPlayerPhysics.TryStepSmooth</c>, which
/// already differentiates sprint/sneak/walk via its own elevateFactor branches.
/// Collapsing the step height to the vanilla baseline when the gate is closed
/// is sufficient to make "step up only while sprinting" behave, because a
/// vanilla step height <i>is</i> the no-enhanced-step-up state regardless of
/// rise speed.
/// </para>
/// </remarks>
internal static class StepUpGate
{
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
