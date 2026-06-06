using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

internal static class PowerRefresher
{
    public static void Refresh(CombatSnapshot snap)
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;

        foreach (var saved in snap.Creatures)
        {
            var creature = saved.Ref;
            if (creature == null) continue;
            var node = room.GetCreatureNode(creature);
            if (node == null) continue;

            NCreatureStateDisplay? stateDisplay = null;
            foreach (var child in SnapshotRestorer.WalkNodeTree(node))
                if (child is NCreatureStateDisplay sd) { stateDisplay = sd; break; }
            if (stateDisplay == null) continue;

            if (ReflectionCache.NCreatureStateDisplayPowerContainerField?.GetValue(stateDisplay) is not NPowerContainer container) continue;

            // _ExitTree 시 signal handler가 해제됨 — _creature 재연결
            try
            {
                var prevCreature = ReflectionCache.NPowerContainerCreatureField?.GetValue(container);
                if (!ReferenceEquals(prevCreature, creature))
                {
                    ReflectionCache.NPowerContainerCreatureField?.SetValue(container, creature);
                    ReflectionCache.NPowerContainerConnectSignalsMethod?.Invoke(container, null);
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[Power] _creature rebind failed: {ex.Message}"); }

            if (ReflectionCache.NPowerContainerNodesField?.GetValue(container) is not System.Collections.IList powerNodes) continue;

            // QueueFree 전에 RemoveChild — 동일 프레임 내 AddChild 중복 방지
            foreach (var p in powerNodes)
            {
                if (p is Node n)
                {
                    try { n.GetParent()?.RemoveChild(n); } catch { }
                    try { n.QueueFree(); } catch { }
                }
            }
            powerNodes.Clear();

            foreach (var pm in creature.Powers)
            {
                if (pm == null || !pm.IsVisible) continue;
                try { ReflectionCache.NPowerContainerAddMethod?.Invoke(container, [pm]); }
                catch (Exception ex) { UndoLogger.Warn($"[Power] re-add failed for {pm.Id.Entry}: {ex.Message}"); }
            }

            // NPower._Ready가 modulate.a=0으로 시작하는 tween을 걸기 때문에
            // undo 직후 즉시 visible로 강제 설정 (재호출은 signal 중복 에러 유발)
            for (int i = 0; i < powerNodes.Count; i++)
            {
                if (powerNodes[i] is not CanvasItem ci) continue;
                try
                {
                    var m = ci.Modulate;
                    if (m.A < 0.99f || !ci.Visible)
                    {
                        ci.Visible = true;
                        ci.Modulate = new Color(m.R, m.G, m.B, 1f);
                    }
                }
                catch { }
            }
        }
    }
}