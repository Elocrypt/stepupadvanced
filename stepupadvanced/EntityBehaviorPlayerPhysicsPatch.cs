using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.GameContent;

namespace stepupadvanced
{
    [HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), "TryStepSmooth")]
    public static class EntityBehaviorPlayerPhysicsPatch
    {
        public static float GetSneakElevateF() => (float)(StepUpAdvancedConfig.Current.StepSpeed * 0.025);
        public static float GetDefaultElevateF() => (float)(StepUpAdvancedConfig.Current.StepSpeed * 0.05);
        public static float GetSprintElevateF() => (float)(StepUpAdvancedConfig.Current.StepSpeed * 0.10);

        public static double GetSneakElevateD() => StepUpAdvancedConfig.Current.StepSpeed * 0.025;
        public static double GetDefaultElevateD() => StepUpAdvancedConfig.Current.StepSpeed * 0.05;
        public static double GetSprintElevateD() => StepUpAdvancedConfig.Current.StepSpeed * 0.10;

        private static bool Approx(float a, float b) => Math.Abs(a - b) < 1e-6f;
        private static bool Approx(double a, double b) => Math.Abs(a - b) < 1e-8;

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            bool replacedAny = false;

            var fSneak = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSneakElevateF));
            var fDefault = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetDefaultElevateF));
            var fSprint = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSprintElevateF));

            var dSneak = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSneakElevateD));
            var dDefault = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetDefaultElevateD));
            var dSprint = AccessTools.Method(typeof(EntityBehaviorPlayerPhysicsPatch), nameof(GetSprintElevateD));

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                try
                {
                    // FLOAT sites (ldc.r4)
                    if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f)
                    {
                        MethodInfo target = null;
                        if (Approx(f, 0.025f)) target = fSneak;
                        else if (Approx(f, 0.05f)) target = fDefault;
                        else if (Approx(f, 0.10f)) target = fSprint;

                        if (target != null)
                        {
                            var repl = new CodeInstruction(OpCodes.Call, target);
                            if (ins.labels != null && ins.labels.Count > 0) repl.labels.AddRange(ins.labels);
                            if (ins.blocks != null && ins.blocks.Count > 0) repl.blocks.AddRange(ins.blocks);
                            list[i] = repl;
                            replacedAny = true;
                            continue;
                        }
                    }

                    // DOUBLE sites (ldc.r8)
                    if (ins.opcode == OpCodes.Ldc_R8 && ins.operand is double d)
                    {
                        MethodInfo target = null;
                        if (Approx(d, 0.025)) target = dSneak;
                        else if (Approx(d, 0.05)) target = dDefault;
                        else if (Approx(d, 0.10)) target = dSprint;

                        if (target != null)
                        {
                            var repl = new CodeInstruction(OpCodes.Call, target);
                            if (ins.labels != null && ins.labels.Count > 0) repl.labels.AddRange(ins.labels);
                            if (ins.blocks != null && ins.blocks.Count > 0) repl.blocks.AddRange(ins.blocks);
                            list[i] = repl;
                            replacedAny = true;
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Harmony.DEBUG)
                    {
                        Console.WriteLine($"[StepUp Advanced] Transpiler guard at i={i}: {ex.Message}");
                    }
                    return instructions;
                }
            }

            if (!replacedAny)
            {
                if (Harmony.DEBUG)
                {
                    Console.WriteLine("[StepUp Advanced] TryStepSmooth constants not found; leaving method unmodified.");
                }
                return instructions;
            }

            if (Harmony.DEBUG)
            {
                Console.WriteLine("[StepUp Advanced] TryStepSmooth elevate constants successfully swapped.");
            }
                return list.AsEnumerable();
        }
    }
}