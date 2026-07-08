using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace UndoModMS.Patches;

// 원인 미확정 상태에서, 확인된 안티패턴(Duplicate 없이 공유 Material에 SetShaderParameter)을 차단하기 위한 방어 패치.
// 매 프레임이 아니라 원래도 실행되던 이벤트 시점(pulse 토글, 텍스처 갱신)에만 적용되어 추가 성능 비용이 사실상 없음.
internal static class IconMaterialIsolationPatch
{
    private static readonly FieldInfo? NPowerIconField = AccessTools.Field(typeof(NPower), "_icon");

    public static void InstallAll(Harmony harmony)
    {
        var pulseStarted = AccessTools.Method(typeof(NPower), "OnPulsingStarted");
        var pulseStopped = AccessTools.Method(typeof(NPower), "OnPulsingStopped");
        var updateTexture = AccessTools.Method(typeof(RelicModel), "UpdateTexture");

        var powerPrefix = new HarmonyMethod(typeof(IconMaterialIsolationPatch), nameof(EnsureIsolatedForPower));
        if (pulseStarted != null) harmony.Patch(pulseStarted, prefix: powerPrefix);
        else UndoLogger.Warn("[Patch] NPower.OnPulsingStarted not found");

        if (pulseStopped != null) harmony.Patch(pulseStopped, prefix: powerPrefix);
        else UndoLogger.Warn("[Patch] NPower.OnPulsingStopped not found");

        if (updateTexture != null)
            harmony.Patch(updateTexture, prefix: new HarmonyMethod(typeof(IconMaterialIsolationPatch), nameof(EnsureIsolatedForRelic)));
        else UndoLogger.Warn("[Patch] RelicModel.UpdateTexture not found");
    }

    private static void EnsureIsolatedForPower(NPower __instance)
    {
        if (NPowerIconField?.GetValue(__instance) is not TextureRect icon) return;
        EnsureIsolated(icon);
    }

    private static void EnsureIsolatedForRelic(TextureRect texture)
    {
        EnsureIsolated(texture);
    }

    private static void EnsureIsolated(TextureRect icon)
    {
        if (icon.Material is ShaderMaterial material)
            icon.Material = (ShaderMaterial)material.Duplicate(true);
    }
}