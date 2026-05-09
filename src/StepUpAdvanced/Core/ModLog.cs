using System;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Core;

/// <summary>
/// Centralized logging facade for StepUp Advanced. Every log line emitted through
/// these methods is prefixed with <c>[StepUp Advanced]</c> exactly once, by this
/// class — call sites pass the bare message.
/// </summary>
/// <remarks>
/// Routes through <see cref="ICoreAPI"/>'s <c>World.Logger</c>. The Harmony
/// transpiler in <c>EntityBehaviorPlayerPhysicsPatch</c> can't reach
/// <c>World.Logger</c> from inside a transpile callback, so it keeps using
/// <c>Console.WriteLine</c> directly with a hardcoded prefix; that's the only
/// log site in the mod that bypasses this class.
/// </remarks>
internal static class ModLog
{
    /// <summary>The single source of truth for the log-line prefix.</summary>
    private const string Prefix = "[StepUp Advanced]";

    /// <summary>Verbose-level message. Only visible with verbose logging enabled.</summary>
    public static void Verbose(ICoreAPI api, string message)
        => api.World.Logger.VerboseDebug($"{Prefix} {message}");

    /// <summary>Verbose-level formatted message.</summary>
    public static void Verbose(ICoreAPI api, string format, params object[] args)
        => api.World.Logger.VerboseDebug($"{Prefix} {format}", args);

    /// <summary>Notification-level message — informational, always shown.</summary>
    public static void Notification(ICoreAPI api, string message)
        => api.World.Logger.Notification($"{Prefix} {message}");

    /// <summary>Warning. Indicates a recoverable problem the user should know about.</summary>
    public static void Warning(ICoreAPI api, string message)
        => api.World.Logger.Warning($"{Prefix} {message}");

    /// <summary>Warning, formatted.</summary>
    public static void Warning(ICoreAPI api, string format, params object[] args)
        => api.World.Logger.Warning($"{Prefix} {format}", args);

    /// <summary>Error. The mod tried to do something and it failed; investigate.</summary>
    public static void Error(ICoreAPI api, string message)
        => api.World.Logger.Error($"{Prefix} {message}");

    /// <summary>Error, formatted.</summary>
    public static void Error(ICoreAPI api, string format, params object[] args)
        => api.World.Logger.Error($"{Prefix} {format}", args);

    /// <summary>Event. Lifecycle milestones — init, config-loaded, file-watcher-fired, etc.</summary>
    public static void Event(ICoreAPI api, string message)
        => api.World.Logger.Event($"{Prefix} {message}");

    /// <summary>Event, formatted.</summary>
    public static void Event(ICoreAPI api, string format, params object[] args)
        => api.World.Logger.Event($"{Prefix} {format}", args);
}
