using Vintagestory.API.Client;

namespace StepUpAdvanced.Infrastructure.Input;

/// <summary>
/// Encapsulates the "fire once per key press" pattern for hotkey handlers
/// where holding the key down should not retrigger the action (e.g. the
/// toggle and reload hotkeys, where every per-tick re-fire while held
/// would flip the state back and forth or spam the chat with reload
/// confirmations).
/// </summary>
/// <remarks>
/// Self-subscribes to <see cref="IClientEventAPI.KeyUp"/> at construction
/// time. The hotkey's <c>KeyCode</c> is resolved per-event from
/// <c>capi.Input.HotKeys[id].CurrentMapping.KeyCode</c> rather than cached,
/// to honor runtime hotkey remaps. The lookup is a dictionary indexer +
/// property access — KeyUp fires only on actual user input (never per-tick),
/// so the cost is negligible.
/// </remarks>
internal sealed class KeyHoldTracker
{
    private readonly ICoreClientAPI capi;
    private readonly string hotkeyId;
    private bool held;

    public KeyHoldTracker(ICoreClientAPI capi, string hotkeyId)
    {
        this.capi = capi;
        this.hotkeyId = hotkeyId;
        capi.Event.KeyUp += OnKeyUp;
    }

    /// <summary>
    /// Returns <c>true</c> if the key was not already in a "held" state
    /// from a prior press; simultaneously marks it held until KeyUp clears
    /// the flag. Returns <c>false</c> while the key is held.
    /// </summary>
    public bool TryFire()
    {
        if (held) return false;
        held = true;
        return true;
    }

    private void OnKeyUp(KeyEvent ke)
    {
        // Indexer rather than TryGetValue: if the hotkey id isn't registered
        // we want to fail loudly (it indicates a registration-order bug),
        // not silently no-op. In practice the tracker is constructed in
        // RegisterHotkeys() right after the matching binder.Bind() call,
        // so the key is always present.
        if (ke.KeyCode == capi.Input.HotKeys[hotkeyId].CurrentMapping.KeyCode)
        {
            held = false;
        }
    }
}
