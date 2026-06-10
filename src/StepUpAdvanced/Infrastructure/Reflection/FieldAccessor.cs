using System;
using System.Linq.Expressions;
using System.Reflection;

namespace StepUpAdvanced.Infrastructure.Reflection;

/// <summary>
/// Compiled-delegate field accessor (setter + getter). Replaces the
/// per-call <see cref="FieldInfo.SetValue(object, object)"/> /
/// <see cref="FieldInfo.GetValue(object)"/> pattern, which boxes value-type
/// arguments/returns on every invocation (a <c>float</c> access allocates
/// ~16 bytes per call on .NET).
/// </summary>
/// <remarks>
/// The accessor resolves the field at construction by walking candidate names
/// (the VS API has used both <c>StepHeight</c> and <c>stepHeight</c> across
/// versions); first match wins. If none are found, <see cref="IsAvailable"/>
/// stays <c>false</c> and both <see cref="TrySet"/> / <see cref="TryGet"/>
/// are no-ops returning <c>false</c>.
/// </remarks>
internal sealed class FieldAccessor<TTarget, TValue> where TTarget : class
{
    private readonly Action<TTarget, TValue>? setter;
    private readonly Func<TTarget, TValue>? getter;

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
        getter = CompileGetter(fi);
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

    /// <summary>
    /// Reads the field on <paramref name="target"/> into
    /// <paramref name="value"/>. Returns <c>true</c> on success, <c>false</c>
    /// when the accessor wasn't available (in which case <paramref name="value"/>
    /// is <c>default</c>). Like the setter, this is a compiled delegate — no
    /// per-call boxing of the returned value type, unlike
    /// <see cref="FieldInfo.GetValue(object)"/>.
    /// </summary>
    /// <remarks>
    /// Used for read-back verification: comparing the desired value against the
    /// LIVE field means an external reset of the physics behavior can't leave a
    /// stale cache suppressing the next write.
    /// </remarks>
    public bool TryGet(TTarget target, out TValue value)
    {
        if (getter == null)
        {
            value = default!;
            return false;
        }
        value = getter(target);
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

    private static Func<TTarget, TValue> CompileGetter(FieldInfo fi)
    {
        // Build the lambda: (target) => (TValue)target.<field>;
        // The cast is only emitted when the field's actual type and TValue
        // differ — symmetric with CompileSetter. In the common matching-type
        // case the read is direct.
        var targetParam = Expression.Parameter(typeof(TTarget), "target");
        Expression fieldAccess = Expression.Field(targetParam, fi);

        if (fi.FieldType != typeof(TValue))
        {
            fieldAccess = Expression.Convert(fieldAccess, typeof(TValue));
        }

        var lambda = Expression.Lambda<Func<TTarget, TValue>>(fieldAccess, targetParam);
        return lambda.Compile();
    }
}
