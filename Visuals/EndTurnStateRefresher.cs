using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;
using System.Reflection;

namespace UndoModMS.Visuals;

internal static class EndTurnStateRefresher
{
    public static void Reset()
    {
        ResetCombatManagerState();
        ResetHandState();
        ResetCardPlayQueue();
    }

    private static void ResetCombatManagerState()
    {
        var cm = CombatManager.Instance;
        if (cm == null) return;

        ClearCollection(ReflectionCache.CmPlayersReadyToEndTurnField?.GetValue(cm));
        ClearCollection(ReflectionCache.CmPlayersReadyToBeginEnemyTurnField?.GetValue(cm));

        TrySetBoolProp(cm, ReflectionCache.CmPlayerActionsDisabledProp, false);
        TrySetBoolProp(cm, AccessTools.Property(typeof(CombatManager), "IsPlayPhase"), true);
        TrySetBoolProp(cm, AccessTools.Property(typeof(CombatManager), "EndingPlayerTurnPhaseOne"), false);
        TrySetBoolProp(cm, AccessTools.Property(typeof(CombatManager), "EndingPlayerTurnPhaseTwo"), false);
        TrySetBoolProp(cm, AccessTools.Property(typeof(CombatManager), "IsEnemyTurnStarted"), false);
    }

    private static void ResetHandState()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null) return;

        var currentPlayField = ReflectionCache.HandCurrentCardPlayField;
        var currentPlay = currentPlayField?.GetValue(hand);

        // Godot 쪽이 해제된 좀비 래퍼는 null로 명시 처리
        if (currentPlay is GodotObject zombieGo && !GodotObject.IsInstanceValid(zombieGo))
        {
            try { currentPlayField?.SetValue(hand, null); }
            catch (Exception ex) { UndoLogger.Warn($"[EndTurn] null disposed _currentCardPlay failed: {ex.Message}"); }
            currentPlay = null;
        }

        if (currentPlay != null)
        {
            var tweenField = AccessTools.Field(currentPlay.GetType(), "_tween");
            if (tweenField?.GetValue(currentPlay) is Tween tween && tween.IsValid())
                try { tween.Kill(); } catch { }

            // CancelPlayCard은 베이스 클래스(NCardPlay)에 있으므로 타입 계층 탐색
            MethodInfo? cancelMethod = null;
            for (var t = currentPlay.GetType(); t != null && cancelMethod == null; t = t.BaseType)
                cancelMethod = AccessTools.Method(t, "CancelPlayCard");
            if (cancelMethod != null)
            {
                try { cancelMethod.Invoke(currentPlay, null); }
                catch (Exception ex) { UndoLogger.Warn($"[EndTurn] CancelPlayCard failed: {ex.Message}"); }
            }

            var tryingField = AccessTools.Field(currentPlay.GetType(), "_isTryingToPlayCard");
            try { tryingField?.SetValue(currentPlay, false); } catch { }
        }

        // _currentMode = Mode.Play (내부 중첩 enum이므로 이름으로 탐색)
        var modeField = ReflectionCache.HandCurrentModeField;
        if (modeField != null)
        {
            var modeType = modeField.FieldType;
            if (modeType.IsEnum)
            {
                try { modeField.SetValue(hand, Enum.Parse(modeType, "Play")); }
                catch (Exception ex) { UndoLogger.Warn($"[EndTurn] could not set Mode to Play: {ex.Message}"); }
            }
        }

        var draggedField = AccessTools.Field(typeof(NPlayerHand), "_draggedHolderIndex");
        try { draggedField?.SetValue(hand, -1); } catch { }

        ClearCollection(AccessTools.Field(typeof(NPlayerHand), "_holdersAwaitingQueue")?.GetValue(hand));

        try
        {
            AccessTools.Field(typeof(NPlayerHand), "_isDisabled")?.SetValue(hand, false);
            if (hand is Control control) control.Modulate = Colors.White;
        }
        catch { }
    }

    private static void ResetCardPlayQueue()
    {
        var pq = NCardPlayQueue.Instance;
        if (pq == null) return;
        ClearCollection(AccessTools.Field(typeof(NCardPlayQueue), "_playQueue")?.GetValue(pq));
    }

    private static void ClearCollection(object? collection)
    {
        if (collection == null) return;
        try { AccessTools.Method(collection.GetType(), "Clear")?.Invoke(collection, null); } catch { }
    }

    private static void TrySetBoolProp(object target, PropertyInfo? prop, bool value)
    {
        if (prop == null || !prop.CanWrite) return;
        try { prop.SetValue(target, value); }
        catch (Exception ex) { UndoLogger.Warn($"[EndTurn] {prop.Name} = {value} failed: {ex.Message}"); }
    }
}