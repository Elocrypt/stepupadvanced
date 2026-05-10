using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Infrastructure.Input;

/// <summary>
/// Small wrapper that collapses the
/// <c>RegisterHotKey + SetHotKeyHandler</c> pair into a single
/// <see cref="Bind"/> call. Each Bind call describes the hotkey and its
/// handler in one line, replacing the previous pattern of two parallel
/// blocks of six lines each.
/// </summary>
/// <remarks>
/// The wrapper is deliberately thin — it owns no state beyond the
/// <see cref="ICoreClientAPI"/> reference and adds no behavior on top of
/// the underlying VS API. Its value is purely structural: keeping the
/// id / display-name / default-key / handler tuple together at each
/// registration site, and removing the duplicate id literal that the
/// two-call pattern required.
/// </remarks>
internal sealed class HotkeyBinder
{
    private readonly ICoreClientAPI capi;

    public HotkeyBinder(ICoreClientAPI capi) => this.capi = capi;

    /// <summary>
    /// Registers a hotkey and binds its handler.
    /// </summary>
    /// <param name="id">Hotkey identifier (also used to look up <c>CurrentMapping</c> at runtime, e.g. by <c>KeyHoldTracker</c>).</param>
    /// <param name="name">Display name shown in the controls menu — typically <c>Lang.Get("key.foo")</c>.</param>
    /// <param name="defaultKey">Default key binding. Honored only on first run; later runs use the user's saved mapping.</param>
    /// <param name="handler">Handler invoked on key-down. Returns <c>true</c> to mark the event consumed.</param>
    /// <param name="type">Hotkey category for the controls menu. Defaults to <see cref="HotkeyType.GUIOrOtherControls"/>.</param>
    public void Bind(
        string id,
        string name,
        GlKeys defaultKey,
        ActionConsumable<KeyCombination> handler,
        HotkeyType type = HotkeyType.GUIOrOtherControls)
    {
        capi.Input.RegisterHotKey(id, name, defaultKey, type);
        capi.Input.SetHotKeyHandler(id, handler);
    }
}
