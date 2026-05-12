using System;
using System.Linq.Expressions;
using System.Reflection;

namespace StepUpAdvanced.Infrastructure.Reflection;

/// <summary>
/// Compiled-delegate field setter. Replaces the per-call
/// <see cref="FieldInfo.SetValue(object, object)"/> pattern, which boxes
/// value-type arguments on every invocation (a <c>float</c> setter call
/// allocates ~16 bytes per write on .NET).
/// </summary>
/// <remarks>
/// <para>
/// At 20 Hz with both step-height and elevate-factor writes, the boxing
/// cost is small but constant; <see cref="FieldAccessor{TTarget, TValue}"/>
/// eliminates it for the hot path. The delegate is built once on the
/// first construction and reused indefinitely.
/// </para>
/// <para>
/// The accessor resolves the field at construction by walking a list of
/// candidate names (the VS API has historically used both <c>StepHeight</c>
/// and <c>stepHeight</c> across versions); first match wins. If none are
/// found, <see cref="IsAvailable"/> stays <c>false</c> and
/// <see cref="TrySet"/> is a no-op returning <c>false</c> — callers can
/// fall back to a one-time user-facing warning.
/// </para>
/// <para>
/// Setter-only by design. Getting reflected field values isn't on the
/// hot path here; <see cref="FieldInfo.GetValue"/> can be used directly
/// if ever needed.
/// </para>
/// </remarks>
internal sealed class FieldAccessor<TTarget, TValue> where TTarget : class
{
    private readonly Action<TTarget, TValue>? setter;

    /// <summary>
    /// True when the field was resolved and a setter delegate compiled.
    /// </summary>
    public bool IsAvailable => setter != null;

    /// <summary>
    /// The resolved field's name on the target type. <c>null</c> when
    /// no candidate matched. Exposed for diagnostic logging.
    /// </summary>
    public string? ResolvedFieldName { get; }

    /// <summary>
    /// Builds the accessor against <paramref name="ownerType"/>, trying
    /// each candidate name in order. Public and non-public instance
    /// fields are both considered.
    /// </summary>
    public FieldAccessor(Type ownerType, params string[] candidateFieldNames)
    {
        var fi = ResolveField(ownerType, candidateFieldNames);
        if (fi == null) return;

        ResolvedFieldName = fi.Name;
        setter = CompileSetter(fi);
    }

    /// <summary>
    /// Sets the field on <paramref name="target"/> to <paramref name="value"/>.
    /// Returns <c>true</c> on success, <c>false</c> when the accessor wasn't
    /// available (caller can emit a one-time error).
    /// </summary>
    public bool TrySet(TTarget target, TValue value)
    {
        if (setter == null) return false;
        setter(target, value);
        return true;
    }

    private static FieldInfo? ResolveField(Type type, string[] candidates)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in candidates)
        {
            var fi = type.GetField(name, flags);
            if (fi != null) return fi;
        }
        return null;
    }

    private static Action<TTarget, TValue> CompileSetter(FieldInfo fi)
    {
        // Build the lambda: (target, value) => target.<field> = (FieldType)value;
        // The cast is only emitted when TValue and the field's actual type
        // differ — e.g. someone constructs FieldAccessor<X, float> against a
        // double field. In the common case (matching types) the assignment
        // is direct.
        var targetParam = Expression.Parameter(typeof(TTarget), "target");
        var valueParam = Expression.Parameter(typeof(TValue), "value");
        var fieldAccess = Expression.Field(targetParam, fi);

        Expression assignedValue = valueParam;
        if (fi.FieldType != typeof(TValue))
        {
            assignedValue = Expression.Convert(valueParam, fi.FieldType);
        }

        var assignment = Expression.Assign(fieldAccess, assignedValue);
        var lambda = Expression.Lambda<Action<TTarget, TValue>>(assignment, targetParam, valueParam);
        return lambda.Compile();
    }
}
