using HarmonyLib;
using stepupadvanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), "TryStepSmooth")]
[HarmonyPatch(new Type[] {
    typeof(EntityControls),
    typeof(EntityPos),
    typeof(Vec2d),
    typeof(float),
    typeof(List<Cuboidd>),
    typeof(Cuboidd)
})]
public static class EntityBehaviorPlayerPhysicsPatch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        System.Console.WriteLine("[StepUp Advanced] Transpiler applied to TryStepSmooth.");
        var codes = new List<CodeInstruction>(instructions);

        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.10) < 0.0001)
            {
                // Log the replacement
                System.Console.WriteLine($"[StepUp Advanced] Replacing 0.10 (sprint factor) at index {i}");

                // Preserve labels
                var method = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod(nameof(GetSprintElevateFactor), BindingFlags.Static | BindingFlags.Public);
                if (method == null) throw new Exception("[StepUp Advanced] GetSprintElevateFactor method not found!");
                var newInstruction = new CodeInstruction(OpCodes.Call, method);
                newInstruction.labels.AddRange(codes[i].labels);
                codes[i] = newInstruction;
            }
            else if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.025) < 0.0001)
            {
                System.Console.WriteLine($"[StepUp Advanced] Replacing 0.025 (sneak factor) at index {i}");
                var method = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod(nameof(GetSneakElevateFactor), BindingFlags.Static | BindingFlags.Public);
                if (method == null) throw new Exception("[StepUp Advanced] GetSneakElevateFactor method not found!");
                var newInstruction = new CodeInstruction(OpCodes.Call, method);
                newInstruction.labels.AddRange(codes[i].labels);
                codes[i] = newInstruction;
            }
            else if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.05) < 0.0001)
            {
                System.Console.WriteLine($"[StepUp Advanced] Replacing 0.05 (default factor) at index {i}");
                var method = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod(nameof(GetDefaultElevateFactor), BindingFlags.Static | BindingFlags.Public);
                if (method == null) throw new Exception("[StepUp Advanced] GetDefaultElevateFactor method not found!");
                var newInstruction = new CodeInstruction(OpCodes.Call, method);
                newInstruction.labels.AddRange(codes[i].labels);
                codes[i] = newInstruction;
            }
        }

        return codes.AsEnumerable();
    }

    // Custom elevate factors
    public static double GetSprintElevateFactor()
    {
        double factor = StepUpAdvancedConfig.Current.StepSpeed * 0.10;
        System.Console.WriteLine($"[StepUp Advanced] Sprint Elevate Factor: {factor}");
        return factor;
    }

    public static double GetSneakElevateFactor()
    {
        double factor = StepUpAdvancedConfig.Current.StepSpeed * 0.025;
        System.Console.WriteLine($"[StepUp Advanced] Sneak Elevate Factor: {factor}");
        return factor;
    }

    public static double GetDefaultElevateFactor()
    {
        double factor = StepUpAdvancedConfig.Current.StepSpeed * 0.05;
        System.Console.WriteLine($"[StepUp Advanced] Default Elevate Factor: {factor}");
        return factor;
    }
}