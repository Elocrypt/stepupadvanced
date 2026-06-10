using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using StepUpAdvanced.Configuration;
using Vintagestory.GameContent;

namespace StepUpAdvanced;

/// <summary>
/// Scales the vanilla step-up "elevate" speed by the configured StepSpeed by
/// rewriting the three elevate constants in
/// <see cref="EntityBehaviorPlayerPhysics.TryStepSmooth"/>.
/// </summary>
/// <remarks>
/// The vanilla site is a single double ternary:
/// <code>double elevateFactor = controls.Sprint ? 0.10 : controls.Sneak ? 0.025 : 0.05;</code>
/// All three values load as <c>ldc.r8</c> and feed one <c>stloc</c> (the
/// <c>elevateFactor</c> local). The transpiler swaps each constant for a call
/// to the matching <c>Get*ElevateD</c> getter, which multiplies the base
/// factor by <c>StepUpOptions.Current.StepSpeed</c>.
///
/// <para>
/// Matching is an ANCHORED TRIPLE-WINDOW, not a value-only scan. A bare
/// "replace any 0.05/0.1/0.025 anywhere in the method" would silently corrupt
/// an unrelated literal a future VS version might add to this method (these are
/// very common magic numbers). Instead we require the three values to appear as
/// three <em>consecutive</em> <c>ldc.r8</c> occurrences forming exactly the set
/// {0.10, 0.025, 0.05} and terminated by a <c>stloc</c> before the next double
/// load — i.e. the elevate triple feeding a single local. The method's other
/// doubles (0.03, 0.3, 0.001) never form that window. If the window is not
/// found, the method is returned unmodified (safe mode), so a vanilla refactor
/// disables the tweak rather than mis-patching.
/// </para>
///
/// <para>
/// The anchor deliberately keys on the value-triple + store, not on the
/// surrounding branch opcodes: <c>EntityControls.Sprint</c>/<c>Sneak</c> could
/// compile to <c>ldfld</c> or <c>callvirt</c>, and we don't want to couple to
/// that. The vanilla site is double-only, so there is no float (<c>ldc.r4</c>)
/// path here.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), "TryStepSmooth")]
public static class EntityBehaviorPlayerPhysicsPatch
{
    private const double SneakFactor = 0.025;
    private const double DefaultFactor = 0.05;
    private const double SprintFactor = 0.10;

    public static double GetSneakElevateD() => StepUpOptions.Current.StepSpeed * SneakFactor;
    public static double GetDefaultElevateD() => StepUpOptions.Current.StepSpeed * DefaultFactor;
    public static double GetSprintElevateD() => StepUpOptions.Current.StepSpeed * SprintFactor;

    private static bool Approx(double a, double b) => Math.Abs(a - b) < 1e-8;

    private static bool IsTarget(double v) =>
        Approx(v, SprintFactor) || Approx(v, SneakFactor) || Approx(v, DefaultFactor);

    private static bool IsStloc(OpCode op) =>
        op == OpCodes.Stloc || op == OpCodes.Stloc_S ||
        op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 ||
        op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);

        try
        {
            // Every ldc.r8 in the method, in IL order, with its list index.
            var doubleLoads = new List<(int index, double value)>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R8 && list[i].operand is double v)
                {
                    doubleLoads.Add((i, v));
                }
            }

            // Scan for three CONSECUTIVE ldc.r8 occurrences whose value-set is
            // exactly {Sprint, Sneak, Default}, then confirm a stloc follows the
            // third before the next double load (or end). That terminator is what
            // proves the triple feeds one local — the elevate ternary — and not a
            // coincidental cluster of same-valued literals.
            for (int k = 0; k + 2 < doubleLoads.Count; k++)
            {
                double a = doubleLoads[k].value;
                double b = doubleLoads[k + 1].value;
                double c = doubleLoads[k + 2].value;

                bool allTargets = IsTarget(a) && IsTarget(b) && IsTarget(c);
                bool coversAll =
                    new[] { a, b, c }.Any(x => Approx(x, SprintFactor)) &&
                    new[] { a, b, c }.Any(x => Approx(x, SneakFactor)) &&
                    new[] { a, b, c }.Any(x => Approx(x, DefaultFactor));

                if (!allTargets || !coversAll)
                {
                    continue;
                }

                // Bound the stloc search at the next double load (exclusive), or
                // end of method if the triple is the last cluster of ldc.r8.
                int thirdIndex = doubleLoads[k + 2].index;
                int nextLdcIndex = (k + 3 < doubleLoads.Count) ? doubleLoads[k + 3].index : list.Count;

                bool storeFollows = false;
                for (int j = thirdIndex + 1; j < nextLdcIndex; j++)
                {
                    if (IsStloc(list[j].opcode)) { storeFollows = true; break; }
                }

                if (!storeFollows)
                {
                    continue;
                }

                // Window confirmed — swap each constant for its getter, mapping by
                // value (robust to branch-block reordering within the ternary).
                for (int t = 0; t < 3; t++)
                {
                    (int idx, double value) = doubleLoads[k + t];
                    MethodInfo target = GetterFor(value);
                    var repl = new CodeInstruction(OpCodes.Call, target);
                    if (list[idx].labels.Count > 0) repl.labels.AddRange(list[idx].labels);
                    if (list[idx].blocks.Count > 0) repl.blocks.AddRange(list[idx].blocks);
                    list[idx] = repl;
                }

                if (Harmony.DEBUG)
                {
                    Console.WriteLine("[StepUp Advanced] TryStepSmooth elevate triple matched and swapped.");
                }
                return list.AsEnumerable();
            }

            if (Harmony.DEBUG)
            {
                Console.WriteLine("[StepUp Advanced] TryStepSmooth elevate triple not found; leaving method unmodified.");
            }
            return instructions;
        }
        catch (Exception ex)
        {
            if (Harmony.DEBUG)
            {
                Console.WriteLine($"[StepUp Advanced] Transpiler guard: {ex.Message}");
            }
            return instructions;
        }
    }

    private static MethodInfo GetterFor(double value)
    {
        if (Approx(value, SprintFactor)) return AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSprintElevateD));
        if (Approx(value, SneakFactor)) return AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSneakElevateD));
        return AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetDefaultElevateD));
    }
}
