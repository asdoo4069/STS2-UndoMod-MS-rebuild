using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using UndoModMS.Snapshot;
using System.Reflection;

namespace UndoModMS.Visuals;

internal static class PotionRefresher
{
    private static Type? _containerType;
    private static Type? _holderType;
    private static Type? _potionType;
    private static FieldInfo? _holdersField;
    private static FieldInfo? _holderPotionBackingField;
    private static FieldInfo? _holderDisabledField;
    private static FieldInfo? _holderEmptyIconField;
    private static MethodInfo? _holderAddPotionMethod;
    private static MethodInfo? _potionCreateMethod;
    private static bool _initialized;

    public static void Refresh()
    {
        InitTypes();
        if (_containerType == null || _holderType == null || _potionType == null) return;

        var nRun = NRun.Instance;
        if (nRun == null) return;

        var container = SnapshotRestorer.WalkNodeTree(nRun)
            .FirstOrDefault(_containerType.IsInstanceOfType);
        if (container == null) { UndoLogger.Warn("[Potion] NPotionContainer not found in scene"); return; }

        if (_holdersField?.GetValue(container) is not System.Collections.IList holders) return;

        var player = ResolvePlayer();
        if (player == null) return;

        int n = Math.Min(holders.Count, player.PotionSlots.Count);
        for (int i = 0; i < n; i++)
        {
            if (holders[i] is not Node holder) continue;
            var desired = player.PotionSlots[i];

            try
            {
                foreach (var child in holder.GetChildren())
                {
                    if (_potionType.IsInstanceOfType(child))
                    {
                        holder.RemoveChild(child);
                        ((Node)child).QueueFree();
                    }
                }
                _holderPotionBackingField?.SetValue(holder, null);
                _holderDisabledField?.SetValue(holder, false);
                if (holder is Control hControl) hControl.Modulate = Colors.White;
                if (_holderEmptyIconField?.GetValue(holder) is Control emptyIcon)
                    emptyIcon.Modulate = Colors.White;

                if (desired != null && _potionCreateMethod != null && _holderAddPotionMethod != null)
                {
                    var nPotion = _potionCreateMethod.Invoke(null, [desired]);
                    if (nPotion != null)
                    {
                        ((Node)nPotion).Set("position", new Vector2(-30f, -30f));
                        _holderAddPotionMethod.Invoke(holder, [nPotion]);
                    }
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[Potion] slot[{i}]: {ex.Message}"); }
        }
    }

    private static void InitTypes()
    {
        if (_initialized) return;
        _initialized = true;

        _containerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Potions.NPotionContainer");
        _holderType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Potions.NPotionHolder");
        _potionType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Potions.NPotion");

        if (_containerType != null)
            _holdersField = AccessTools.Field(_containerType, "_holders");

        if (_holderType != null)
        {
            _holderPotionBackingField = AccessTools.Field(_holderType, "<Potion>k__BackingField");
            _holderDisabledField = AccessTools.Field(_holderType, "_disabledUntilPotionRemoved");
            _holderEmptyIconField = AccessTools.Field(_holderType, "_emptyIcon");
            _holderAddPotionMethod = AccessTools.Method(_holderType, "AddPotion");
        }

        if (_potionType != null)
            _potionCreateMethod = AccessTools.Method(_potionType, "Create", [typeof(PotionModel)]);
    }

    private static Player? ResolvePlayer()
    {
        var rm = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
        var stateProp = AccessTools.Property(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "State");
        var runState = stateProp?.GetValue(rm) as MegaCrit.Sts2.Core.Runs.RunState;
        return runState?.Players.FirstOrDefault();
    }
}