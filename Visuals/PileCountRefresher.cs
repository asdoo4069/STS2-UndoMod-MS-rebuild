using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using UndoModMS.Snapshot;
using System.Reflection;

namespace UndoModMS.Visuals;

internal static class PileCountRefresher
{
    private static FieldInfo? _pileFieldCache;

    public static void Refresh()
    {
        if (ReflectionCache.NCombatCardPileType == null)
        {
            UndoLogger.Warn("[PileCount] NCombatCardPileType null — cannot refresh");
            return;
        }
        var room = NCombatRoom.Instance;
        if (room == null) return;

        if (_pileFieldCache == null)
            _pileFieldCache = AccessTools.Field(ReflectionCache.NCombatCardPileType, "_pile");

        foreach (var node in SnapshotRestorer.WalkNodeTree(room)
            .Where(ReflectionCache.NCombatCardPileType.IsInstanceOfType))
        {
            if (_pileFieldCache?.GetValue(node) is not CardPile pile) continue;

            int actual = pile.Cards.Count;
            ReflectionCache.NCombatCardPileCurrentCountField?.SetValue(node, actual);

            var label = ReflectionCache.NCombatCardPileCountLabelField?.GetValue(node);
            if (label != null)
            {
                var setText = AccessTools.Method(label.GetType(), "SetTextAutoSize");
                if (setText != null)
                {
                    try { setText.Invoke(label, [actual.ToString()]); }
                    catch (Exception ex) { UndoLogger.Warn($"[PileCount] SetTextAutoSize failed: {ex.Message}"); }
                }
                else if (label is Label gd) gd.Text = actual.ToString();
            }
        }
    }
}