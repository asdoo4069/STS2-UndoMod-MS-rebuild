using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

internal static class OrphanCardCleaner
{
    public static void Clean()
    {
        var root = NGame.Instance;
        if (root == null) return;

        var hand = NPlayerHand.Instance;
        var legitimate = new HashSet<NCard>(ReferenceEqualityComparer.Instance);
        if (hand != null)
        {
            foreach (var holder in hand.ActiveHolders)
            {
                if (holder.CardNode is NCard nc) legitimate.Add(nc);
            }
        }

        foreach (var node in SnapshotRestorer.WalkNodeTree(root))
        {
            if (node is not NCard card) continue;
            if (legitimate.Contains(card)) continue;

            // 스크린 오버레이 소속 NCard 보존
            // (해제 시 스크린의 _card 필드에 좀비 래퍼가 남아 ObjectDisposedException 발생)
            if (HasScreenAncestor(card)) continue;

            KillKnownTweens(card);

            try { if (card.IsInsideTree()) card.QueueFree(); }
            catch (Exception ex) { UndoLogger.Warn($"[Orphan] free failed: {ex.Message}"); }
        }
    }

    private static bool HasScreenAncestor(Node node)
    {
        var p = node.GetParent();
        while (p != null)
        {
            var t = p.GetType();
            if (t.Name.EndsWith("Screen", StringComparison.Ordinal)) return true;
            var ns = t.Namespace;
            if (ns != null && ns.Contains(".Screens", StringComparison.Ordinal)) return true;
            p = p.GetParent();
        }
        return false;
    }

    private static void KillKnownTweens(NCard card)
    {
        foreach (var name in new[] { "_tween", "_currentTween", "_positionTween", "_scaleTween" })
        {
            var f = AccessTools.Field(card.GetType(), name);
            if (f?.GetValue(card) is Tween t && t.IsValid())
                try { t.Kill(); } catch { }
        }
    }
}