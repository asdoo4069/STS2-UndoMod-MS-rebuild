using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using System.Reflection;

namespace UndoModMS.Visuals;

internal static class HandPositionSnapper
{
    private static Type? _holderType;
    private static FieldInfo? _targetPosField;
    private static FieldInfo? _targetAngleField;
    private static FieldInfo? _targetScaleField;
    private static FieldInfo? _posCancelField;
    private static MethodInfo? _setAngleInstantlyMethod;
    private static MethodInfo? _setScaleInstantlyMethod;

    public static void Snap()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || hand.ActiveHolders.Count == 0) return;

        InitReflection(hand.ActiveHolders[0].GetType());
        if (_holderType == null) return;

        foreach (var holder in hand.ActiveHolders)
        {
            try
            {
                var cancel = _posCancelField?.GetValue(holder) as CancellationTokenSource;
                cancel?.Cancel();
            }
            catch { }

            try
            {
                if (_targetPosField?.GetValue(holder) is Vector2 t && holder is Control ctrl)
                    ctrl.Position = t;
            }
            catch { }

            try
            {
                if (_targetAngleField?.GetValue(holder) is float angle && _setAngleInstantlyMethod != null)
                    _setAngleInstantlyMethod.Invoke(holder, [angle]);
            }
            catch { }

            try
            {
                if (_targetScaleField?.GetValue(holder) is Vector2 scale && _setScaleInstantlyMethod != null)
                    _setScaleInstantlyMethod.Invoke(holder, [scale]);
            }
            catch { }
        }
    }

    private static void InitReflection(Type holderType)
    {
        if (_holderType == holderType) return;
        _holderType = holderType;
        _targetPosField = AccessTools.Field(holderType, "_targetPosition");
        _targetAngleField = AccessTools.Field(holderType, "_targetAngle");
        _targetScaleField = AccessTools.Field(holderType, "_targetScale");
        _posCancelField = AccessTools.Field(holderType, "_positionCancelToken");
        _setAngleInstantlyMethod = AccessTools.Method(holderType, "SetAngleInstantly");
        _setScaleInstantlyMethod = AccessTools.Method(holderType, "SetScaleInstantly");
    }
}