using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace UndoModMS;

[ModInitializer(nameof(ModInit))]
public partial class ModStart : Node
{
	public const string ModId = "UndoMod-MS";

	public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; }
		= new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

	public static void ModInit()
	{
		var harmony = new Harmony(ModId);
		try
		{
			harmony.PatchAll(typeof(ModStart).Assembly);
			Patches.SnapshotPatches.InstallAll(harmony);
			UndoLogger.Info("initialized.");
		}
		catch (Exception ex)
		{
			UndoLogger.Warn($"init failed: {ex.Message}");
		}
	}
}