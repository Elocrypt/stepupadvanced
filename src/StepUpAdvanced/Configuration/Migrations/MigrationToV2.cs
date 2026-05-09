namespace StepUpAdvanced.Configuration.Migrations;

/// <summary>
/// Brings configs from schema versions 0 and 1 up to schema version 2.
/// </summary>
/// <remarks>
/// <para>
/// In schema version 2 the forward-probe properties (<c>ForwardProbeSpan</c>,
/// <c>ForwardProbeDistance</c>) gained meaningful defaults of <c>1</c>.
/// Earlier configs may have had them at zero or negative — values that
/// disable the probe entirely. This migration coerces those values to
/// the new defaults so existing configs gain the feature on upgrade.
/// </para>
/// <para>
/// Idempotent: running this migration on an already-migrated config (where
/// the values are positive) returns <c>false</c> without touching anything.
/// </para>
/// </remarks>
internal sealed class MigrationToV2 : IConfigMigration
{
    public int TargetVersion => 2;

    public bool Apply(StepUpOptions cfg)
    {
        bool changed = false;

        if (cfg.ForwardProbeSpan <= 0)
        {
            cfg.ForwardProbeSpan = 1;
            changed = true;
        }

        if (cfg.ForwardProbeDistance <= 0)
        {
            cfg.ForwardProbeDistance = 1;
            changed = true;
        }

        return changed;
    }
}
