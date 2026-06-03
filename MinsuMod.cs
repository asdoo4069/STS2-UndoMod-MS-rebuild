using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;

namespace MinsuMod; // 모드 이름 수정하기

[ModInitializer("ModInit")]
public static class ModStart
{
	public const string ModId = "MinsuMod"; // 모드 이름 수정하기

	public static void ModInit()
	{
		new Harmony(ModId).PatchAll();
		Log.Info($"[{ModId}] Initialized successfully.");
	}
}