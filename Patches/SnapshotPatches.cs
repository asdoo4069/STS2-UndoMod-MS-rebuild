using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using System.Reflection;
using UndoModMS.Snapshot;
using UndoModMS.Ui;
using UndoModMS.Undo;

namespace UndoModMS.Patches;

// 생성자 전체를 패치하는 이유:
// 게임 업데이트로 시그니처가 바뀌어도 조용히 누락되지 않도록.
public static class SnapshotPatches
{
    public static void InstallAll(Harmony harmony)
    {
        var prefix = AccessTools.Method(typeof(SnapshotPatches), nameof(SnapshotPrefix));
        if (prefix == null) { UndoLogger.Warn("[Patch] SnapshotPrefix method not found"); return; }

        PatchAllCtors(harmony, typeof(PlayCardAction), prefix);
        PatchAllCtors(harmony, typeof(EndPlayerTurnAction), prefix);
        PatchAllCtors(harmony, typeof(UsePotionAction), prefix);
        PatchAllCtors(harmony, typeof(DiscardPotionGameAction), prefix);
    }

    private static void PatchAllCtors(Harmony harmony, Type type, MethodInfo prefix)
    {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (ctors.Length == 0)
        {
            UndoLogger.Warn($"[Patch] {type.Name}: no constructors found — snapshot won't fire on this action");
            return;
        }
        int patched = 0;
        foreach (var c in ctors)
        {
            try
            {
                harmony.Patch(c, prefix: new HarmonyMethod(prefix));
                patched++;
            }
            catch (Exception ex)
            {
                UndoLogger.Warn($"[Patch] {type.Name}.ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))}) failed: {ex.Message}");
            }
        }
    }

    public static void SnapshotPrefix(MethodBase __originalMethod)
    {
        if (MultiplayerGate.IsDormant()) return;

        // 우클릭 업그레이드 미리보기 중에는 가짜 액션이 생성된다.
        // 이 구간에 스냅샷을 찍으면 프레임 드롭 및 우클릭 해제 불량 발생.
        if (PatchNGameInput.IsInRmbWindow()) return;

        UndoController.TakeSnapshot();
    }
}

[HarmonyPatch(typeof(CombatManager), "Reset", new[] { typeof(bool) })]
public static class PatchCombatReset
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        UndoController.ClearStacks();
        UndoButtonUi.Uninstall();
        CombatSnapshot.IdleAnimCache.Clear();
        DeathAnimDelayPatch.ClearAll();
        AnimDiePatch.ClearDetached();
    }
}

// Reset보다 먼저 호출되므로, 전투 종료 즉시 스택을 비워
// 전환 중에 단축키가 오래된 스냅샷에 접근하지 못하게 한다.
[HarmonyPatch(typeof(CombatManager), "EndCombatInternal")]
public static class PatchEndCombatInternal
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        UndoController.ClearStacks();
        UndoButtonUi.Uninstall();
        CombatSnapshot.IdleAnimCache.Clear();
        DeathAnimDelayPatch.FlushForCombatEnd();
        AnimDiePatch.ClearDetached();
    }
}

// 핫 리로드 등 예외 경로로 EndCombatInternal/Reset이 누락된 경우를 대비한
// 이중 안전장치. 이전 전투의 해제된 노드 참조가 새 전투에 남지 않도록.
[HarmonyPatch(typeof(CombatManager), "StartCombatInternal")]
public static class PatchStartCombatInternal
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        UndoController.ClearStacks();
        CombatSnapshot.IdleAnimCache.Clear();
        AnimDiePatch.ClearDetached();
        DeathAnimDelayPatch.ClearAll();
        MultiplayerGate.ResetForNewCombat();
    }
}

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class PatchStartTurn
{
    [HarmonyPostfix]
    public static void Postfix(CombatManager __instance)
    {
        try
        {
            if (MultiplayerGate.IsDormant()) return;

            var cs = ReflectionCache.CombatManagerStateField.GetValue(__instance) as CombatState;
            if (cs?.CurrentSide == CombatSide.Player)
            {
                UndoController.ArmTurnBoundary();
                UndoButtonUi.Install();
            }
            // 턴 시작은 모든 Creature가 안정된 애니메이션 상태에 진입한 시점.
            // 여기서 갱신하지 않으면 첫 스냅샷의 관측값이 캐시에 계속 남는다.
            CombatSnapshot.RefreshIdleCacheFromLiveCreatures();
        }
        catch (Exception ex) { UndoLogger.Warn($"[Patch] StartTurn: {ex.Message}"); }
    }
}