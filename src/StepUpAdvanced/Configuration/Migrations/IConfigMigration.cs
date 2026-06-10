namespace StepUpAdvanced.Configuration.Migrations;

/// <summary>
/// A one-time, schema-version-gated transformation applied to a loaded
/// <see cref="StepUpOptions"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are framework-free by design — they take only a config
/// instance, mutate it in place, and return whether they changed anything.
/// No <c>ICoreAPI</c>, no logging, no I/O. This makes them trivially
/// unit-testable and reusable across server / client / single-player.
/// </para>
/// <para>
/// A migration runs when the config's <c>SchemaVersion</c> is strictly less
/// than its <see cref="TargetVersion"/>. Migrations should be idempotent — running the same migration twice on
/// a config should produce the same result as running it once. This is a
/// safety net for the schema-version-tracking system's edge cases.
/// </para>
/// </remarks>
internal interface IConfigMigration
{
    /// <summary>
    /// The schema version this migration brings configs up to. Applied when
    /// the config's <c>SchemaVersion</c> is strictly less than this value.
    /// </summary>
    int TargetVersion { get; }

    /// <summary>
    /// Applies the migration to <paramref name="cfg"/> in place.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the migration changed any field on <paramref name="cfg"/>;
    /// <c>false</c> if the config was already in the desired shape (e.g. a
    /// previous run had already applied this transformation).
    /// </returns>
    bool Apply(StepUpOptions cfg);
}
