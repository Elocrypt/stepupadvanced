using System.Linq;

namespace StepUpAdvanced.Configuration.Migrations;

/// <summary>
/// Orchestrates application of all known <see cref="IConfigMigration"/>
/// instances against a loaded <see cref="StepUpOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Migrations run in <see cref="IConfigMigration.TargetVersion"/> order,
/// each one gated by the running version: a migration to v3 only runs if
/// the running version is currently below 3. After a migration applies,
/// the running version advances to that target.
/// </para>
/// <para>
/// To register a new migration, add an entry to <see cref="Migrations"/>.
/// Order doesn't matter for correctness (the runner sorts), but conventional
/// declaration order is ascending by target version for readability.
/// </para>
/// <para>
/// The runner does NOT update <c>StepUpOptions.SchemaVersion</c> — that's
/// the caller's responsibility (currently <see cref="ConfigStore.MergeAndMigrate"/>),
/// because the schema-version write is paired with other "this config is
/// now current" bookkeeping that lives outside the migration concern.
/// </para>
/// </remarks>
internal static class MigrationRunner
{
    /// <summary>
    /// All known migrations. Adding a new one is the only change required
    /// to introduce a new schema version.
    /// </summary>
    private static readonly IConfigMigration[] Migrations =
        new IConfigMigration[]
        {
            new MigrationToV2(),
            // Future:
            // new MigrationToV3(),
            // new MigrationToV4(),
        }
        .OrderBy(m => m.TargetVersion)
        .ToArray();

    /// <summary>
    /// Applies every migration whose <see cref="IConfigMigration.TargetVersion"/>
    /// is strictly greater than <paramref name="fromVersion"/>, in ascending
    /// order. Each migration sees the result of the previous one.
    /// </summary>
    /// <param name="cfg">The config to migrate. Mutated in place.</param>
    /// <param name="fromVersion">
    /// The starting schema version. Typically the loaded config's
    /// <c>SchemaVersion</c> (or 0 if unset / negative).
    /// </param>
    /// <returns>
    /// <c>true</c> if any migration changed any field on <paramref name="cfg"/>.
    /// </returns>
    public static bool Run(StepUpOptions cfg, int fromVersion)
    {
        bool changed = false;
        int running = fromVersion;

        foreach (var migration in Migrations)
        {
            if (running < migration.TargetVersion)
            {
                if (migration.Apply(cfg)) changed = true;
                running = migration.TargetVersion;
            }
        }

        return changed;
    }
}
