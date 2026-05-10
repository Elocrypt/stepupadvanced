namespace StepUpAdvanced.Infrastructure.Input;

/// <summary>
/// Owns toast-suppression state for the client's hotkey-driven UI.
/// Each property is an independent <see cref="OnceFlag"/> that prevents
/// the same toast from being emitted on every per-tick re-press while a
/// limit condition (max / min / blocked) remains active. Resetting a
/// flag re-arms it for the next time the condition transitions back on.
/// </summary>
/// <remarks>
/// <para>
/// Pre-Phase-4 the suppression state lived in six shared <c>hasShown*</c>
/// fields on <c>StepUpAdvancedModSystem</c>. Sharing flags across
/// distinct toasts produced the "I just saw 'max-height', so 'max-speed'
/// is silently swallowed" class of bugs. The split into nine named
/// flags (one per distinct toast) ends that.
/// </para>
/// <para>
/// Typed handles are deliberately preferred over an enum-keyed lookup —
/// the set of toasts is fixed at compile time, IDE discovery is better,
/// and there's no chance of typo'd string keys at call sites.
/// </para>
/// </remarks>
internal sealed class MessageDebouncer
{
    /// <summary>"max-height" toast at the height cap.</summary>
    public OnceFlag HeightAtMax { get; } = new();

    /// <summary>"min-height" toast at the height floor (server cap or client hard floor).</summary>
    public OnceFlag HeightAtMin { get; } = new();

    /// <summary>"max-speed" toast at the speed cap.</summary>
    public OnceFlag SpeedAtMax { get; } = new();

    /// <summary>"min-speed" toast at the speed floor (server cap or client hard floor).</summary>
    public OnceFlag SpeedAtMin { get; } = new();

    /// <summary>
    /// "server-enforced – height-change-blocked" toast. Direction-agnostic:
    /// covers both increase-blocked and decrease-blocked (the toast text
    /// doesn't distinguish, so a single flag is correct here — the
    /// pre-Phase-4 split into <c>MaxE</c>/<c>MinE</c> was an artifact of
    /// the implementation, not a UX choice).
    /// </summary>
    public OnceFlag HeightEnforced { get; } = new();

    /// <summary>"server-enforced – speed-change-blocked" toast. Direction-agnostic; see <see cref="HeightEnforced"/>.</summary>
    public OnceFlag SpeedEnforced { get; } = new();

    /// <summary>"speed-only-mode – height-controls-disabled" toast (XSkills-compatibility mode).</summary>
    public OnceFlag HeightSpeedOnlyMode { get; } = new();

    /// <summary>"server-enforced – reload-blocked" toast — fired by <c>OnReloadConfig</c> when the server forbids client-side reload.</summary>
    public OnceFlag ReloadBlocked { get; } = new();

    /// <summary>
    /// "server-enforcement on/off" notice fired from the receive handler
    /// on enforcement-flag transitions. The receive site reads
    /// <see cref="OnceFlag.IsShown"/> directly to decide between the
    /// "off" and "on" branches; <see cref="OnceFlag.Reset"/> arms the
    /// "on" toast and <see cref="OnceFlag.TryShow"/> emits and marks it.
    /// </summary>
    public OnceFlag ServerEnforcement { get; } = new();
}

/// <summary>
/// "Show once until reset" toast-suppression primitive. Independent
/// instances per toast type. No thread-safety guarantees — toast emission
/// runs on the main client thread.
/// </summary>
internal sealed class OnceFlag
{
    private bool shown;

    /// <summary>
    /// Returns <c>true</c> if the flag has not yet been shown since the last
    /// <see cref="Reset"/>, simultaneously marking it shown. Use as
    /// <c>if (flag.TryShow()) emit-the-toast;</c>.
    /// </summary>
    public bool TryShow()
    {
        if (shown) return false;
        shown = true;
        return true;
    }

    /// <summary>
    /// Re-arms the flag so the next <see cref="TryShow"/> returns <c>true</c>.
    /// Idempotent.
    /// </summary>
    public void Reset() => shown = false;

    /// <summary>
    /// Inspection without state change. Used by callers that have their
    /// own emit/reset logic structure (e.g. the server-enforcement receive
    /// site, which branches on <c>IsEnforced ^ IsShown</c>).
    /// </summary>
    public bool IsShown => shown;
}
