using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Core;

/// <summary>
/// Builders for <see cref="TextCommandResult"/> values returned by chat-command
/// handlers. Wraps each result body in the standard <see cref="ChatFormatting.Tag"/>
/// so command output looks consistent with everything else the mod prints.
/// </summary>
/// <remarks>
/// Aliased as <c>SuaCmd</c> at call sites via a <c>using</c> directive in
/// <c>StepUpAdvancedModSystem</c>; existing call sites compile unchanged.
/// </remarks>
internal static class CommandResults
{
    public static TextCommandResult Ok(string headline, string? detail = null)
        => TextCommandResult.Success($"{ChatFormatting.Tag} {ChatFormatting.Ok(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Warn(string headline, string? detail = null)
        => TextCommandResult.Success($"{ChatFormatting.Tag} {ChatFormatting.Warn(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Err(string headline, string? detail = null)
        => TextCommandResult.Error($"{ChatFormatting.Tag} {ChatFormatting.Err(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Info(string headline, string? detail = null)
        => TextCommandResult.Success($"{ChatFormatting.Tag} {ChatFormatting.Muted(headline)}{(detail == null ? "" : " " + detail)}");

    /// <summary>
    /// Bulleted list with a colored title. Each item is rendered as a bullet
    /// point with <see cref="ChatFormatting.Val"/> styling.
    /// </summary>
    public static TextCommandResult List(string title, IEnumerable<string> items)
    {
        var body = string.Join("\n", items.Select(i => $"• {ChatFormatting.Val(i)}"));
        return TextCommandResult.Success($"{ChatFormatting.Tag} {ChatFormatting.Bold(ChatFormatting.Font(title, ChatFormatting.CList))}\n{body}");
    }
}
