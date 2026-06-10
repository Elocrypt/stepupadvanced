using System;
using System.Threading;
using StepUpAdvanced.Configuration;
using Vintagestory.API.Client;

namespace StepUpAdvanced.Application;

/// <summary>
/// Dual-debounced queue for client-side config writes. Owns its own lock,
/// pending-write flag, and last-save timestamp.
/// </summary>
/// <remarks>
/// <para>
/// Behavior:
/// </para>
/// <list type="bullet">
/// <item><description><b>200 ms callback scheduling.</b>
/// <see cref="RequestSave"/> schedules a save callback via
/// <c>capi.Event.RegisterCallback</c> if one isn't already pending.
/// Multiple calls within the 200 ms window coalesce into one callback.</description></item>
/// <item><description><b>500 ms hard floor.</b> When the scheduled
/// callback fires, it only writes to disk if 500 ms or more have
/// passed since the last successful write. Bounds disk-write rate to
/// ~2/sec regardless of how often <see cref="RequestSave"/> is called.</description></item>
/// </list>
/// <para>
/// Phase 7b Step 5 extracts this from the static
/// <c>ConfigQueueLock</c>/<c>saveQueued</c>/<c>lastSaveTime</c>/<c>MinSaveInterval</c>
/// state that previously lived on <see cref="StepUpAdvancedModSystem"/>.
/// Static per-mod state didn't compose well with VS's per-instance
/// ModSystem lifecycle — an instance owned by ModSystem and exposed to
/// the controllers as a method reference is cleaner.
/// </para>
/// <para>
/// <b>Edge case preserved verbatim:</b> if the user makes one last
/// change and exits within 500 ms of the previous save, the change
/// lives only in memory and is never persisted — the next callback
/// fires after exit, never enters the &gt;500 ms branch. This was the
/// pre-Step-5 behavior; if it becomes a real problem, the fix is a
/// trailing-edge save in <see cref="StepUpAdvancedModSystem.Dispose"/>,
/// scheduled in the Phase 9 roadmap.
/// </para>
/// </remarks>
internal sealed class ConfigSaveQueue
{
    /// <summary>
    /// Delay between <see cref="RequestSave"/> and the callback that
    /// performs the save. Multiple <see cref="RequestSave"/> calls
    /// inside this window collapse into one callback.
    /// </summary>
    private const int CallbackDelayMs = 200;

    /// <summary>
    /// Minimum spacing between disk writes, even across callback
    /// boundaries. The first layer (scheduling) prevents callback
    /// pile-up; this second layer hard-caps the actual write rate.
    /// </summary>
    private static readonly TimeSpan MinSaveInterval = TimeSpan.FromMilliseconds(500);

    private readonly ICoreClientAPI capi;
    // .NET 9 Lock type: under C# 13 the lock statement compiles to
    // Lock.EnterScope() instead of Monitor.Enter on a syncblock — lower
    // overhead for this hot-ish coalescing gate.
    private readonly Lock queueLock = new();
    private volatile bool saveQueued;
    private DateTime lastSaveTime = DateTime.MinValue;

    public ConfigSaveQueue(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>
    /// Schedules a debounced save. Idempotent within the 200 ms
    /// callback-scheduling window — multiple calls coalesce. The
    /// actual disk write happens inside the scheduled callback and is
    /// further bounded by the 500 ms hard floor.
    /// </summary>
    public void RequestSave()
    {
        lock (queueLock)
        {
            if (saveQueued) return;
            saveQueued = true;
        }

        capi.Event.RegisterCallback(_ =>
        {
            // Release the gate BEFORE the throttle check — if the
            // throttle says skip, the next RequestSave should be able
            // to re-schedule. Otherwise a denied write would block all
            // subsequent saves until the next request after the next
            // 500 ms boundary.
            lock (queueLock) saveQueued = false;

            var now = DateTime.UtcNow;
            if (now - lastSaveTime < MinSaveInterval) return;
            lastSaveTime = now;
            ConfigStore.Save(capi);
        }, CallbackDelayMs);
    }
}
