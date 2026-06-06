using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

// diff 방식 대신 전체 재빌드를 사용하는 이유:
// holder identity가 반복 undo에서 어긋나면 "보이지만 클릭 안 되는" 카드가 생긴다.
// 전체 재빌드로 이 문제를 원천 차단. 카드 수가 적어 비용은 무시할 수준.
internal static class HandRefresher
{
    public static void Refresh(CombatSnapshot snap)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null) return;
        if (!snap.PileRefs.TryGetValue(PileType.Hand, out var savedHand)) return;

        var currentCards = new List<CardModel>();
        foreach (var holder in hand.ActiveHolders)
        {
            var nc = holder.CardNode;
            if (nc?.Model is CardModel cm) currentCards.Add(cm);
        }

        foreach (var card in currentCards)
        {
            try { hand.Remove(card); }
            catch (Exception ex) { UndoLogger.Warn($"[Hand] remove {card.Id} failed: {ex.Message}"); }
        }

        ForceRemovePhantomHolders(hand);

        for (int i = 0; i < savedHand.Count; i++)
        {
            try
            {
                var nc = NCard.Create(savedHand[i], ModelVisibility.Visible);
                if (nc == null) { UndoLogger.Warn($"[Hand] NCard.Create returned null for {savedHand[i].Id}"); continue; }
                nc.Scale = Vector2.One;
                hand.Add(nc, i);
            }
            catch (Exception ex) { UndoLogger.Warn($"[Hand] add {savedHand[i].Id} at {i} failed: {ex.Message}"); }
        }

        try { hand.ForceRefreshCardIndices(); } catch { }
    }

    private static void ForceRemovePhantomHolders(NPlayerHand hand)
    {
        // CardNode가 null인 phantom holder — 카드는 제거됐지만 holder가 남아
        // ActiveHolders.Count가 실제보다 커져 CanPlayCards를 막는다.
        var holdersField = AccessTools.Field(typeof(NPlayerHand), "_holders")
            ?? AccessTools.Field(typeof(NPlayerHand), "_activeHolders")
            ?? AccessTools.Field(typeof(NPlayerHand), "<ActiveHolders>k__BackingField");
        if (holdersField?.GetValue(hand) is not System.Collections.IList list) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not Node holderNode) continue;
            var cardNodeProp = AccessTools.Property(holderNode.GetType(), "CardNode");
            if (cardNodeProp?.GetValue(holderNode) != null) continue;

            list.RemoveAt(i);
            try { holderNode.QueueFree(); } catch { }
        }
    }
}