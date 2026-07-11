using System;
using System.Diagnostics;
using System.Text;
using Godot;
using HarmonyLib;

namespace UndoModMS.Diagnostics;

// 진단 전용 임시 코드. 원인 확정되면 통째로 제거 예정.
internal static class IconStateLogger
{
    private const int Capacity = 500;

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
    private static readonly object Lock = new();

    public static void InstallAll(Harmony harmony)
    {
        var setShaderParam = AccessTools.Method(typeof(ShaderMaterial), "SetShaderParameter",
            new[] { typeof(StringName), typeof(Variant) });
        if (setShaderParam != null)
            harmony.Patch(setShaderParam, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnSetShaderParameter)));

        var materialSetter = AccessTools.PropertySetter(typeof(CanvasItem), "Material");
        if (materialSetter != null)
            harmony.Patch(materialSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnMaterialSet)));

        var modulateSetter = AccessTools.PropertySetter(typeof(CanvasItem), "Modulate");
        if (modulateSetter != null)
            harmony.Patch(modulateSetter, prefix: new HarmonyMethod(typeof(IconStateLogger), nameof(OnModulateSet)));
    }

    private static readonly HashSet<string> IgnoredParams = ["width"];

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

    private static readonly HashSet<string> IgnoredCallers =
    [
        "MegaCrit.Sts2.Core.Nodes.Combat.NEnergyCounter.RefreshLabel",
        "MegaCrit.Sts2.Core.Nodes.Cards.NCard.OnReturnedFromPool",
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