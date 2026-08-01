using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace UndoModMS.Patches;

// 방패 아이콘 위치 버그 수정.
// 언두 복원 중 BlockChanged가 강제 발동되며 방패 등장 트윈(0.5초)이 트리거되는데,
// 그 재생 도중 게임 자체의 _spineAnimator.BoundsUpdated 이벤트가 발동해
// UpdateLayoutForCreatureBounds가 호출되면, 트윈 중간(점프된) 위치를 최종
// 위치로 착각해 _originalBlockPosition을 덮어씀. 호출 전 값을 저장해뒀다가
// 트윈 재생 중이었으면 원래 값으로 되돌린다.
internal static class BlockPositionFixPatch
{
    private static readonly FieldInfo? OriginalBlockPositionField =
        AccessTools.Field(typeof(NHealthBar), "_originalBlockPosition");

    private static readonly FieldInfo? BlockTweenField =
        AccessTools.Field(typeof(NHealthBar), "_blockTween");

    private static Vector2 GetOriginalPos(NHealthBar hb) =>
        OriginalBlockPositionField?.GetValue(hb) is Vector2 v ? v : Vector2.Zero;

    [HarmonyPatch(typeof(NHealthBar), "UpdateLayoutForCreatureBounds")]
    private static class UpdateLayoutForCreatureBoundsPatch
    {
        private static void Prefix(NHealthBar __instance, out Vector2? __state)
        {
            __state = null;
            try
            {
                if (BlockTweenField?.GetValue(__instance) is Tween tw
                    && GodotObject.IsInstanceValid(tw) && tw.IsRunning())
                    __state = GetOriginalPos(__instance);
            }
            catch { }
        }

        private static void Postfix(NHealthBar __instance, Vector2? __state)
        {
            if (__state == null) return;
            try
            {
                var after = GetOriginalPos(__instance);
                if (!after.IsEqualApprox(__state.Value))
                {
                    OriginalBlockPositionField?.SetValue(__instance, __state.Value);
                    UndoLogger.Info($"[BlockPosFix] originalBlockPosition {after} -> {__state.Value}");
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[BlockPosFix] failed: {ex.Message}"); }
        }
    }
}