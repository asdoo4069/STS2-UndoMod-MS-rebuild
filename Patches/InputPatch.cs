using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using UndoModMS.Undo;

namespace UndoModMS.Patches;

[HarmonyPatch(typeof(NGame), "_Input")]
public static class PatchNGameInput
{
    public static bool RmbHeld { get; private set; }
    public static long RmbReleasedAtMs { get; private set; }

    // SnapshotPatches에서 RMB 업그레이드 프리뷰 중 스냅샷 억제에 사용
    public const long RmbGraceMs = 250;

    [HarmonyPrefix]
    public static void Prefix(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed) RmbHeld = true;
            else
            {
                RmbHeld = false;
                RmbReleasedAtMs = System.Environment.TickCount64;
            }
        }

        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode != Key.Z) return;

        if (key.ShiftPressed)
            UndoController.UndoTurn();
        else
            UndoController.Undo();
    }

    // RMB 이벤트가 카드 GUI input에 소비되는 경우를 대비해 정적 폴링도 함께 확인
    public static bool IsInRmbWindow()
        => RmbHeld
           || Input.IsMouseButtonPressed(MouseButton.Right)
           || (System.Environment.TickCount64 - RmbReleasedAtMs) <= RmbGraceMs;
}