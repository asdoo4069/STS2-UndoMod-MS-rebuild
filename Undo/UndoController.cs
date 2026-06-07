using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using UndoModMS.Snapshot;

namespace UndoModMS.Undo;

internal static class UndoController
{
    private const int MaxStackSize = 30;
    // 액션 생성자 직후 한 틱 동안 VFX가 스폰되는 구간을 막기 위한 최소 쿨다운.
    private const int ActionCooldownMs = 50;

    private static readonly List<CombatSnapshot> Stack = new();
    public static bool IsRestoring { get; private set; }
    private static bool _turnBoundaryArmed;
    private static long _lastActionTimestampMs;

    public static int StackCount => Stack.Count;

    // 턴 시작 패치에서 호출. 실제 스냅샷은 플레이어가 첫 액션을 취할 때 찍힌다.
    public static void ArmTurnBoundary() => _turnBoundaryArmed = true;

    public static void TakeSnapshot(bool isTurnBoundary = false)
    {
        if (IsRestoring) return;
        if (!CanCaptureNow()) return;

        Patches.AnimDiePatch.PruneStaleDetached();

        bool boundary = isTurnBoundary || _turnBoundaryArmed;
        long startMs = Environment.TickCount64;
        var snap = CombatSnapshot.Capture(boundary);
        long elapsedMs = Environment.TickCount64 - startMs;
        if (snap == null) return;

        Stack.Add(snap);
        if (Stack.Count > MaxStackSize) Stack.RemoveAt(0);
        if (boundary) _turnBoundaryArmed = false;

        _lastActionTimestampMs = Environment.TickCount64;

        if (elapsedMs >= 30)
            UndoLogger.Warn($"[Snapshot] slow capture {elapsedMs}ms stack={Stack.Count} idleCache={CombatSnapshot.IdleAnimCache.Count} detachedZombies={Patches.AnimDiePatch.DetachedZombies.Count}");
    }

    public static void Undo() => UndoSteps(1);

    // 현재 턴 시작 시점으로 되돌리기.
    public static void UndoTurn()
    {
        if (Stack.Count == 0) return;
        int targetIndex = -1;
        for (int i = Stack.Count - 1; i >= 0; i--)
        {
            if (Stack[i].IsTurnBoundary) { targetIndex = i; break; }
        }
        if (targetIndex < 0) targetIndex = 0;
        UndoSteps(Stack.Count - targetIndex);
    }

    private static void UndoSteps(int n)
    {
        if (n <= 0 || Stack.Count == 0) return;
        if (!CanRestoreNow()) return;

        int target = Math.Max(0, Stack.Count - n);
        var snap = Stack[target];
        Stack.RemoveRange(target, Stack.Count - target);

        Patches.DeathAnimDelayPatch.DeathAnimActive.Clear();
        IsRestoring = true;
        try { SnapshotRestorer.Restore(snap); }
        catch (Exception ex) { UndoLogger.Warn($"[Undo] restore threw: {ex.Message}"); }
        finally { IsRestoring = false; }
    }

    public static void ClearStacks()
    {
        Stack.Clear();
        _turnBoundaryArmed = false;
    }

    private static bool CanCaptureNow()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) { TruncateOutOfCombat("cm-null"); return false; }
            var inProgressProp = HarmonyLib.AccessTools.Property(typeof(CombatManager), "IsInProgress");
            if (inProgressProp?.GetValue(cm) is false)
            {
                TruncateOutOfCombat("not-in-progress");
                return false;
            }
        }
        catch { }

        var cs = CurrentCombatState();
        if (cs == null) { TruncateOutOfCombat("cs-null"); return false; }
        return cs.CurrentSide == CombatSide.Player;
    }

    // EndCombatInternal/Reset 패치가 누락된 예외 경로에서의 안전망.
    private static void TruncateOutOfCombat(string reason)
    {
        if (Stack.Count == 0) return;
        UndoLogger.Warn($"[Undo] truncating {Stack.Count} stale snapshot(s) — out of combat ({reason})");
        Stack.Clear();
    }

    public static bool CanRestoreNowPublic() => Stack.Count > 0 && CanRestoreNow();

    private static bool CanRestoreNow()
    {
        if (IsRestoring) return false;

        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return false;
            var inProgressProp = HarmonyLib.AccessTools.Property(typeof(CombatManager), "IsInProgress");
            if (inProgressProp?.GetValue(cm) is false) return false;
        }
        catch { }

        if (CombatSnapshot.AnyCreatureMidTransient()) return false;

        if (Patches.AnimDiePatch.InFlightCount > 0) return false;

        long elapsed = Environment.TickCount64 - _lastActionTimestampMs;
        if (elapsed < ActionCooldownMs) return false;

        var cs = CurrentCombatState();
        if (cs == null) return false;

        try
        {
            var cm = CombatManager.Instance;
            if (cm != null)
            {
                var isPlayPhase = HarmonyLib.AccessTools.Property(typeof(CombatManager), "IsPlayPhase")?.GetValue(cm);
                var endingP1 = HarmonyLib.AccessTools.Property(typeof(CombatManager), "EndingPlayerTurnPhaseOne")?.GetValue(cm);
                var endingP2 = HarmonyLib.AccessTools.Property(typeof(CombatManager), "EndingPlayerTurnPhaseTwo")?.GetValue(cm);
                var enemyTurnStarted = HarmonyLib.AccessTools.Property(typeof(CombatManager), "IsEnemyTurnStarted")?.GetValue(cm);

                if (isPlayPhase is false) return false;
                if (endingP1 is true || endingP2 is true) return false;
                if (enemyTurnStarted is true) return false;
            }
        }
        catch { }

        if (cs.CurrentSide != CombatSide.Player) return false;

        try
        {
            if (NGame.Instance?.Transition?.InTransition == true) return false;
        }
        catch { }

        try
        {
            var aqs = RunManager.Instance?.ActionQueueSet;
            if (aqs?.IsEmpty == false) return false;
        }
        catch { }

        try
        {
            var pq = NCardPlayQueue.Instance;
            if (pq != null)
            {
                var queueField = HarmonyLib.AccessTools.Field(typeof(NCardPlayQueue), "_playQueue");
                if (queueField?.GetValue(pq) is System.Collections.ICollection col && col.Count > 0)
                    return false;
            }
        }
        catch { }

        try
        {
            var hand = NPlayerHand.Instance;
            var currentPlay = hand != null
                ? ReflectionCache.HandCurrentCardPlayField?.GetValue(hand)
                : null;
            if (currentPlay != null)
            {
                var tryingField = HarmonyLib.AccessTools.Field(currentPlay.GetType(), "_isTryingToPlayCard");
                if (tryingField?.GetValue(currentPlay) is true) return false;
            }
        }
        catch { }

        try
        {
            var hand = NPlayerHand.Instance;
            if (hand != null && IsAnyHolderAnimating(hand)) return false;
        }
        catch { }

        try
        {
            if (HasInFlightExecutorAction()) return false;
        }
        catch { }

        return true;
    }

    private static System.Reflection.FieldInfo? _holderTargetPosField;
    private static bool _holderFieldInitialized;
    private const float HolderRestEpsilonSq = 4f;

    private static bool IsAnyHolderAnimating(NPlayerHand hand)
    {
        if (!_holderFieldInitialized && hand.ActiveHolders.Count > 0)
        {
            var holderType = hand.ActiveHolders[0].GetType();
            _holderTargetPosField = HarmonyLib.AccessTools.Field(holderType, "_targetPosition");
            _holderFieldInitialized = true;
        }
        if (_holderTargetPosField == null) return false;

        foreach (var holder in hand.ActiveHolders)
        {
            object? holderObj = holder;
            var posProp = HarmonyLib.AccessTools.Property(holderObj.GetType(), "Position");
            if (posProp?.GetValue(holderObj) is not Godot.Vector2 current) continue;
            if (_holderTargetPosField.GetValue(holderObj) is not Godot.Vector2 target) continue;

            if (current.DistanceSquaredTo(target) > HolderRestEpsilonSq)
                return true;
        }
        return false;
    }

    private static bool HasInFlightExecutorAction()
    {
        var executor = RunManager.Instance?.ActionExecutor;
        if (executor == null) return false;

        var prop = HarmonyLib.AccessTools.Property(executor.GetType(), "CurrentlyRunningAction");
        if (prop?.GetValue(executor) != null) return true;

        // 향후 구현 변경에 대비한 fallback
        foreach (var name in new[] { "_currentAction", "_executingAction", "_action" })
        {
            var f = HarmonyLib.AccessTools.Field(executor.GetType(), name);
            if (f?.GetValue(executor) != null) return true;
        }

        return false;
    }

    private static CombatState? CurrentCombatState()
    {
        var cm = CombatManager.Instance;
        if (cm == null) return null;
        return ReflectionCache.CombatManagerStateField.GetValue(cm) as CombatState;
    }
}