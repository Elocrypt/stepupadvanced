using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using stepupadvanced;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

[HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), "TryStepSmooth")]
[HarmonyPatch(new Type[]
{
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
		Console.WriteLine("[StepUp Advanced] Transpiler applied to TryStepSmooth.");
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		for (int i = 0; i < codes.Count; i++)
		{
			if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.1) < 0.0001)
			{
				Console.WriteLine($"[StepUp Advanced] Replacing 0.10 (sprint factor) at index {i}");
				MethodInfo method = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod("GetSprintElevateFactor", BindingFlags.Static | BindingFlags.Public);
				if (method == null)
				{
					throw new Exception("[StepUp Advanced] GetSprintElevateFactor method not found!");
				}
				CodeInstruction newInstruction = new CodeInstruction(OpCodes.Call, method);
				newInstruction.labels.AddRange(codes[i].labels);
				codes[i] = newInstruction;
			}
			else if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.025) < 0.0001)
			{
				Console.WriteLine($"[StepUp Advanced] Replacing 0.025 (sneak factor) at index {i}");
				MethodInfo method2 = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod("GetSneakElevateFactor", BindingFlags.Static | BindingFlags.Public);
				if (method2 == null)
				{
					throw new Exception("[StepUp Advanced] GetSneakElevateFactor method not found!");
				}
				CodeInstruction newInstruction2 = new CodeInstruction(OpCodes.Call, method2);
				newInstruction2.labels.AddRange(codes[i].labels);
				codes[i] = newInstruction2;
			}
			else if (codes[i].opcode == OpCodes.Ldc_R8 && Math.Abs((double)codes[i].operand - 0.05) < 0.0001)
			{
				Console.WriteLine($"[StepUp Advanced] Replacing 0.05 (default factor) at index {i}");
				MethodInfo method3 = typeof(EntityBehaviorPlayerPhysicsPatch).GetMethod("GetDefaultElevateFactor", BindingFlags.Static | BindingFlags.Public);
				if (method3 == null)
				{
					throw new Exception("[StepUp Advanced] GetDefaultElevateFactor method not found!");
				}
				CodeInstruction newInstruction3 = new CodeInstruction(OpCodes.Call, method3);
				newInstruction3.labels.AddRange(codes[i].labels);
				codes[i] = newInstruction3;
			}
		}
		return codes.AsEnumerable();
	}

    public static double GetSprintElevateFactor()
	{
		double factor = (double)StepUpAdvancedConfig.Current.StepSpeed * 0.1;
		Console.WriteLine($"[StepUp Advanced] Sprint Elevate Factor: {factor}");
		return factor;
	}

    public static double GetSneakElevateFactor()
	{
		double factor = (double)StepUpAdvancedConfig.Current.StepSpeed * 0.025;
		Console.WriteLine($"[StepUp Advanced] Sneak Elevate Factor: {factor}");
		return factor;
	}

	public static double GetDefaultElevateFactor()
	{
		double factor = (double)StepUpAdvancedConfig.Current.StepSpeed * 0.05;
		Console.WriteLine($"[StepUp Advanced] Default Elevate Factor: {factor}");
		return factor;
	}
}
