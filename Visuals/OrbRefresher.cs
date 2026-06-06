using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

// 오브 undo 후 슬롯이 사라지는 이유:
// 카드 플레이 중 NOrb 자식 노드가 해제되고, 모델 롤백만으로는 비주얼 행이 복원되지 않는다.
// 기존 NOrb를 전부 제거 후 채워진 슬롯 + 빈 슬롯 placeholder를 재생성.
internal static class OrbRefresher
{
    public static void Refresh(CombatSnapshot snap)
    {
        if (!snap.HasOrbData) return;
        if (ReflectionCache.NOrbManagerOrbsField == null
            || ReflectionCache.NOrbManagerContainerField == null) return;

        var room = NCombatRoom.Instance;
        var cm = CombatManager.Instance;
        if (room == null || cm == null) return;
        var cs = ReflectionCache.CombatManagerStateField.GetValue(cm) as CombatState;
        if (cs == null) return;

        foreach (var ally in cs.Allies)
        {
            var player = ally.Player;
            if (player == null) continue;

            var nCreature = room.GetCreatureNode(ally);
            if (nCreature == null) continue;

            var orbManager = nCreature.OrbManager;
            if (orbManager == null) continue;

            var orbQueue = player.PlayerCombatState?.OrbQueue;
            if (orbQueue == null) continue;

            try { RebuildOrbManager(orbManager, orbQueue); }
            catch (Exception ex) { UndoLogger.Warn($"[Orbs] visual refresh failed: {ex.Message}"); }
        }
    }

    private static void RebuildOrbManager(NOrbManager orbManager, MegaCrit.Sts2.Core.Entities.Orbs.OrbQueue orbQueue)
    {
        var tween = ReflectionCache.NOrbManagerTweenField?.GetValue(orbManager) as Tween;
        if (tween != null && tween.IsValid()) tween.Kill();

        var nOrbsList = ReflectionCache.NOrbManagerOrbsField!.GetValue(orbManager) as System.Collections.IList;
        var container = ReflectionCache.NOrbManagerContainerField!.GetValue(orbManager) as Control;
        if (nOrbsList == null || container == null) return;

        foreach (var n in nOrbsList)
            if (n is Node node) try { node.QueueFree(); } catch { }
        nOrbsList.Clear();

        bool isLocal = orbManager.IsLocal;
        var orbs = orbQueue.Orbs.ToList();
        int capacity = orbQueue.Capacity;

        for (int i = 0; i < orbs.Count; i++)
        {
            var nOrb = NOrb.Create(isLocal, orbs[i]);
            container.AddChild(nOrb);
            nOrbsList.Add(nOrb);
            nOrb.Position = Vector2.Zero;
        }

        // 빈 슬롯 placeholder — 없으면 오브 0개로 돌아갔을 때 행이 완전히 사라진다.
        for (int i = orbs.Count; i < capacity; i++)
        {
            var nOrb = NOrb.Create(isLocal);
            container.AddChild(nOrb);
            nOrbsList.Add(nOrb);
            nOrb.Position = Vector2.Zero;
        }

        ReflectionCache.NOrbManagerTweenLayoutMethod?.Invoke(orbManager, null);
        ReflectionCache.NOrbManagerUpdateNavMethod?.Invoke(orbManager, null);

        // TweenLayout이 (0,0)에서 애니메이션하지 않도록 즉시 최종 위치로 스냅.
        if (ReflectionCache.NOrbManagerTweenField?.GetValue(orbManager) is Tween tweenAfter && tweenAfter.IsValid()) tweenAfter.Kill();
        if (capacity > 0)
        {
            float arcAngle = 125f;
            float angleStep = capacity > 1 ? arcAngle / (capacity - 1) : 0f;
            float radius = Mathf.Lerp(225f, 300f, (capacity - 3f) / 7f);
            if (!isLocal) radius *= 0.75f;
            float curAngle = arcAngle;
            for (int i = 0; i < nOrbsList.Count && i < capacity; i++)
            {
                float s = Mathf.DegToRad(-25f - curAngle);
                var finalPos = new Vector2(-Mathf.Cos(s), Mathf.Sin(s)) * radius;
                if (nOrbsList[i] is NOrb nOrbItem) nOrbItem.Position = finalPos;
                curAngle -= angleStep;
            }
        }

        // NOrb가 씬 트리에 진입한 후 텍스처가 바인딩되도록 UpdateVisuals를 지연 호출.
        Callable.From(() =>
        {
            try
            {
                if (ReflectionCache.NOrbManagerOrbsField!.GetValue(orbManager)
                    is not System.Collections.IList list) return;
                foreach (var item in list)
                    if (item is NOrb n) n.UpdateVisuals(false);
            }
            catch (Exception ex) { UndoLogger.Warn($"[Orbs] deferred UpdateVisuals failed: {ex.Message}"); }
        }).CallDeferred();
    }
}