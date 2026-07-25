using System.Diagnostics;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;

namespace UndoModMS.Diagnostics;

// 진단 전용 임시 코드. 원인 확정되면 통째로 제거 예정.
internal static class IconStateLogger
{
    private const int Capacity = 300;

    private class Entry
    {
        public string Site = "";
        public ulong TargetId;
        public string Detail = "";
        public string Caller = "";
        public string FirstSeen = "";
        public string LastSeen = "";
        public int RepeatCount;
    }

    private static readonly Entry?[] Slots = new Entry?[Capacity];
    private static int _nextSlot;
    private static readonly Lock Lock = new();

    public static void InstallAll(Harmony harmony)
    {
        var setShaderParam = AccessTools.Method(typeof(ShaderMaterial), "SetShaderParameter",
            [typeof(StringName), typeof(Variant)]);
        if (setShaderParam != null)
            harmony.Patch(setShaderParam, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnSetShaderParameter)));

        var materialSetter = AccessTools.PropertySetter(typeof(CanvasItem), "Material");
        if (materialSetter != null)
            harmony.Patch(materialSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnMaterialSet)));

        var modulateSetter = AccessTools.PropertySetter(typeof(CanvasItem), "Modulate");
        if (modulateSetter != null)
            harmony.Patch(modulateSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnModulateSet)));

        // --- 텍스처 무효화 감시 ---
        var spriteTextureSetter = AccessTools.PropertySetter(typeof(Sprite2D), "Texture");
        if (spriteTextureSetter != null)
            harmony.Patch(spriteTextureSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnSpriteTextureSet)));

        var textureRectSetter = AccessTools.PropertySetter(typeof(TextureRect), "Texture");
        if (textureRectSetter != null)
            harmony.Patch(textureRectSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnTextureRectSet)));

        // --- 카드 재생 타임라인 마커 ---
        var executeAction = AccessTools.Method(typeof(PlayCardAction), "ExecuteAction");
        if (executeAction != null)
            harmony.Patch(executeAction, postfix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnCardPlayed)));

        // --- 파워 아이콘 생성 구간 진입/이탈 ---
        if (ReflectionCache.NPowerContainerAddMethod != null)
        {
            harmony.Patch(ReflectionCache.NPowerContainerAddMethod,
                prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnPowerAddEnter)),
                postfix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnPowerAddExit)));
        }

        // --- Tween을 통한 modulate 변경 감시 (Modulate setter 훅을 우회할 가능성 검증용) ---
        var tweenProperty = AccessTools.Method(typeof(Tween), "TweenProperty",
            [typeof(GodotObject), typeof(NodePath), typeof(Variant), typeof(double)]);
        if (tweenProperty != null)
            harmony.Patch(tweenProperty, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnTweenProperty)));

        // --- NPower._Ready 직후 상태 스냅샷 (동시성/초기화 실패 감지용) ---
        var powerReady = AccessTools.Method(typeof(NPower), "_Ready");
        if (powerReady != null)
            harmony.Patch(powerReady, postfix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnPowerReadySnapshot)));
    }

    private static void OnSpriteTextureSet(Sprite2D __instance, Texture2D __0)
    {
        Record("SPRITE_TEXTURE_SET", __instance.GetInstanceId(), __0?.GetType().Name ?? "null");
    }

    private static void OnTextureRectSet(TextureRect __instance, Texture2D __0)
    {
        Record("TEXTURE_RECT_SET", __instance.GetInstanceId(), __0?.GetType().Name ?? "null");
    }

    private static void OnCardPlayed(CardModel ____card)
    {
        if (____card == null) return;
        Record("CARD_PLAYED", 0, ____card.Id.Entry);
    }

    private static void OnPowerAddEnter(NPowerContainer __instance, PowerModel power)
    {
        Record("POWER_ADD_ENTER", __instance.GetInstanceId(), power.Id.Entry);
    }

    private static void OnPowerAddExit(NPowerContainer __instance, PowerModel power)
    {
        Record("POWER_ADD_EXIT", __instance.GetInstanceId(), power.Id.Entry);
    }

    private static void OnTweenProperty(GodotObject __0, NodePath __1, Variant __2)
    {
        var path = __1.ToString();
        if (!path.Contains("modulate", StringComparison.OrdinalIgnoreCase)) return;
        Record("TWEEN_MODULATE", __0?.GetInstanceId() ?? 0, $"{path} -> {__2}");
    }

    private static void OnPowerReadySnapshot(NPower __instance, TextureRect ____icon)
    {
        if (____icon == null) return;

        var modulate = ____icon.Modulate;
        var selfModulate = ____icon.SelfModulate;
        var material = ____icon.Material;
        var texture = ____icon.Texture;

        var detail = $"modulate=({modulate.R:F2},{modulate.G:F2},{modulate.B:F2},{modulate.A:F2}) " +
                     $"self_modulate=({selfModulate.R:F2},{selfModulate.G:F2},{selfModulate.B:F2},{selfModulate.A:F2}) " +
                     $"material={material?.GetType().Name ?? "null"} " +
                     $"texture={texture?.GetType().Name ?? "null"}";

        Record("POWER_READY_SNAPSHOT", __instance.GetInstanceId(), detail);
    }

    private static void OnSetShaderParameter(ShaderMaterial __instance, StringName __0)
    {
        var paramName = __0.ToString();
        if (IgnoredParams.Contains(paramName)) return;
        Record("SHADER_PARAM", __instance.GetInstanceId(), paramName);
    }

    private static void OnMaterialSet(CanvasItem __instance, Material __0)
    {
        Record("MATERIAL_SET", __instance.GetInstanceId(), __0?.GetType().Name ?? "null");
    }

    private static void OnModulateSet(CanvasItem __instance, Color __0)
    {
        // 완전한 검정(알파 제외 RGB가 0에 가까움)만 기록 — 노이즈 억제
        if (__0.R > 0.05f || __0.G > 0.05f || __0.B > 0.05f) return;
        Record("MODULATE_BLACK", __instance.GetInstanceId(), $"({__0.R:F2},{__0.G:F2},{__0.B:F2},{__0.A:F2})");
    }

    private static readonly HashSet<string> IgnoredParams = ["width"];

    private static readonly HashSet<string> IgnoredCallers =
    [
        "MegaCrit.Sts2.Core.Nodes.Combat.NEnergyCounter.RefreshLabel",
        "MegaCrit.Sts2.Core.Nodes.Cards.NCard.OnReturnedFromPool",
        "MegaCrit.Sts2.Core.Nodes.Vfx.NCardFlyVfx+<PlayAnim>d__25.MoveNext",

        // UI 버튼 호버/포커스 셰이더 애니메이션 — 카드/파워 아이콘과 무관
        "MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton.UpdateShaderV",
        "MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton.UpdateShaderS",
        "MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton.OnEnable",
        "MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton.OnFocus",
        "MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton.UpdateShaderParam",
        "MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton.OnFocus",
        "MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton.UpdateShaderParam",
        "MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton.UpdateShaderV",

        // 몬스터 비주얼 색조 갱신 — 아이콘과 무관
        "MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals.SetScaleAndHue",

        // 의도된 화면 전환(페이드/맵 열기) — 정상 동작으로 이미 확인됨
        "MegaCrit.Sts2.Core.Nodes.NTransition+<RoomFadeOut>d__18.MoveNext",
        "MegaCrit.Sts2.Core.Nodes.NTransition+<RoomFadeIn>d__19.MoveNext",
        "MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Open",

        // 카드 조준/선택 UI, 이름표 호버, 카드 궤적 이펙트, 툴팁 — 아이콘과 무관, 대량 노이즈
        "MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle.OnSelect",
        "MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle.OnDeselect",
        "MegaCrit.Sts2.Core.Nodes.Combat.NCreatureStateDisplay.ShowNameplate",
        "MegaCrit.Sts2.Core.Nodes.Combat.NCreatureStateDisplay.HideNameplate",
        "MegaCrit.Sts2.Core.Nodes.Combat.NCreatureStateDisplay.OnHovered",
        "MegaCrit.Sts2.Core.Nodes.Combat.NCreatureStateDisplay.OnUnhovered",
        "MegaCrit.Sts2.Core.Nodes.Vfx.NCardTrailVfx._Ready",
        "MegaCrit.Sts2.Core.Nodes.Vfx.NCardTrailVfx+<FadeOut>d__7.MoveNext",
        "MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Init",
    ];

    private static void Record(string site, ulong targetId, string detail)
    {
        var caller = FindCallerFrame();
        if (IgnoredCallers.Contains(caller)) return;
        var now = DateTime.Now.ToString("HH:mm:ss.fff");

        lock (Lock)
        {
            // 직전 기록과 동일하면(같은 site+target+detail+caller) 압축
            var last = Slots[(_nextSlot - 1 + Capacity) % Capacity];
            if (last != null && last.Site == site && last.TargetId == targetId
                && last.Detail == detail && last.Caller == caller)
            {
                last.RepeatCount++;
                last.LastSeen = now;
                return;
            }

            Slots[_nextSlot] = new Entry
            {
                Site = site,
                TargetId = targetId,
                Detail = detail,
                Caller = caller,
                FirstSeen = now,
                LastSeen = now,
                RepeatCount = 1,
            };
            _nextSlot = (_nextSlot + 1) % Capacity;
        }
    }

    private static string FindCallerFrame()
    {
        try
        {
            var trace = new StackTrace(0, false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                var m = trace.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                var name = m.Name;
                var declType = m.DeclaringType?.FullName;

                if (string.IsNullOrEmpty(declType)) continue;
                if (name.Contains("_Patch")) continue;
                if (declType.StartsWith("HarmonyLib")) continue;
                if (declType.Contains("IconStateLogger")) continue;
                if (declType.StartsWith("System.")) continue;
                if (declType == "Godot.ShaderMaterial" && name == "SetShaderParameter") continue;
                if (declType == "Godot.CanvasItem") continue;
                if (declType.StartsWith("Godot.Callable")) continue;

                return $"{declType}.{name}";
            }
        }
        catch { }
        return "unknown";
    }

    public static void Dump()
    {
        Entry?[] snapshot;
        lock (Lock)
        {
            snapshot = new Entry?[Capacity];
            Array.Copy(Slots, snapshot, Capacity);
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== IconStateLogger dump ===");
        for (int i = 0; i < Capacity; i++)
        {
            int idx = (_nextSlot + i) % Capacity; // 오래된 순서대로
            var e = snapshot[idx];
            if (e == null) continue;
            sb.AppendLine($"{e.FirstSeen}~{e.LastSeen} (x{e.RepeatCount}) | {e.Site} | target#{e.TargetId} | {e.Detail} | caller={e.Caller}");
        }

        var path = "user://ModConfig/icon-state-dump.txt";
        try
        {
            DirAccess.MakeDirRecursiveAbsolute("user://ModConfig/");
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(sb.ToString());
            UndoLogger.Warn($"[IconStateLogger] dumped to {path}");
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[IconStateLogger] dump failed: {ex.Message}");
        }
    }
}