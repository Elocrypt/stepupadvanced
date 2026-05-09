using StepUpAdvanced.Configuration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace StepUpAdvanced.Core;

/// <summary>
/// Chat-formatting helpers for StepUp Advanced. Builds colored, bolded, and
/// localized chat strings with the consistent <c>[StepUp Advanced]</c> tag.
/// </summary>
/// <remarks>
/// Aliased as <c>SuaChat</c> at call sites in <c>StepUpAdvancedModSystem</c> via
/// a <c>using</c> directive, so existing call sites keep compiling unchanged.
/// Phase 8 will sweep call sites to use the new full name.
/// </remarks>
internal static class ChatFormatting
{
    public const string CAccent = "#5bc0de";
    public const string CGood = "#5cb85c";
    public const string CWarn = "#f0ad4e";
    public const string CBad = "#d9534f";
    public const string CValue = "#ffd54f";
    public const string CMuted = "#a0a4a8";
    public const string CList = "#a12eff";

    public static string Font(string text, string hex) => $"<font color=\"{hex}\">{text}</font>";
    public static string Bold(string text) => $"<strong>{text}</strong>";

    /// <summary>Localizes a key under the <c>sua:</c> namespace. Trivial wrapper kept for terseness at call sites.</summary>
    public static string L(string key, params object[] args)
        => Lang.Get($"sua:{key}", args);

    public static string Tag => Bold(Font($"[{L("modname")}]", CAccent));
    public static string Ok(string t) => Bold(Font(t, CGood));
    public static string Warn(string t) => Bold(Font(t, CWarn));
    public static string Err(string t) => Bold(Font(t, CBad));
    public static string Val(string t) => Bold(Font(t, CValue));
    public static string Muted(string t) => Font(t, CMuted);
    public static string Arrow => Muted("» ");

    /// <summary>
    /// Sends a chat message to the local client, suppressed by <c>QuietMode</c>.
    /// </summary>
    public static void Client(ICoreClientAPI capi, string msg)
    {
        if (StepUpOptions.Current?.QuietMode == true) return;
        capi?.ShowChatMessage(msg);
    }

    /// <summary>
    /// Sends a chat message to a specific server-side player. Not gated by
    /// <c>QuietMode</c> — that's a client-side preference.
    /// </summary>
    public static void Server(IServerPlayer p, string msg) =>
        p?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
}
