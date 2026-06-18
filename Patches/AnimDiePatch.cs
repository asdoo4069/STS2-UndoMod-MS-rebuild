using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;

namespace UndoModMS.Patches;

[HarmonyPatch(typeof(NCreature), "AnimDie")]
public static class AnimDiePatch
{
    public const float DieVisibleFallbackSeconds = 1.5f;
    public const float SpineDieCapSeconds = 0.5f;

    // entity → NCreature 맵핑: 씬트리에서 분리된 좀비를 revive 시 찾기 위해 사용
    public static readonly Dictionary<Creature, NCreature> DetachedZombies = new();
    // 진행 중인 fade-out 트윈: undo revive 시 kill 후 modulate 리셋
    public static readonly Dictionary<NCreature, Tween> ActiveFadeTweens = new();
    // 진행 중인 death anim 수: undo는 0이 될 때까지 대기
    public static int InFlightCount;

    public static readonly HashSet<string> SkipReplacementMonsterTypes = new()
    {
        "TestSubject", // 실험체
        "TheObscura", // 영사자
        "WaterfallGiant", // 폭포 거인
    };

    public static readonly HashSet<string> SpineDieCappedMonsterTypes = new()
    {
        "Toadpole",
    };

    private static readonly string[] ReviveLikePowerNameSubstrings =
    {
        "Revive", "Reborn", "Reincarn", "PreventDeath", "InvincibleOnDeath", "Illusion", "DieForYou", "Adaptable",
    };

    private static readonly string[] LiveReviveAnimPowerNameSubstrings =
    {
        "Revive", "Reborn", "Reincarn", "PreventDeath", "InvincibleOnDeath", "Illusion",
    };

    public static int PruneStaleDetached()
    {
        if (DetachedZombies.Count == 0) return 0;
        var stale = new List<Creature>();
        foreach (var kv in DetachedZombies)
        {
            try
            {
                if (kv.Value == null || !GodotObject.IsInstanceValid(kv.Value))
                    stale.Add(kv.Key);
            }
            catch { stale.Add(kv.Key); }
        }
        foreach (var k in stale) DetachedZombies.Remove(k);
        return stale.Count;
    }

    public static void ClearDetached()
    {
        if (DetachedZombies.Count == 0 && ActiveFadeTweens.Count == 0) return;
        foreach (var kv in DetachedZombies)
        {
            try
            {
                if (GodotObject.IsInstanceValid(kv.Value)) kv.Value.QueueFree();
            }
            catch { }
        }
        DetachedZombies.Clear();

        foreach (var kv in ActiveFadeTweens)
        {
            try
            {
                var tw = kv.Value;
                if (tw != null && GodotObject.IsInstanceValid(tw) && tw.IsValid()) tw.Kill();
            }
            catch { }
        }
        ActiveFadeTweens.Clear();

        // 전투 종료 시 카운터 리셋 — 다음 전투의 첫 undo가 불필요하게 블록되지 않도록
        InFlightCount = 0;
    }

    [HarmonyPrefix]
    public static bool Prefix(NCreature __instance, bool __0, ref Task __result)
    {
        try
        {
            if (!HasDieAnimation(__instance))
                return true;

            var monsterTypeName = GetMonsterTypeName(__instance);
            if (monsterTypeName != null && SkipReplacementMonsterTypes.Contains(monsterTypeName))
                return true;

            var reviveLikePower = FindReviveLikePower(__instance);
            if (reviveLikePower != null)
                return true;

            __result = RunReplacementDeathAnim(__instance, __0);
            return false;
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[AnimDie] replacement setup failed, falling back to original: {ex.Message}");
            return true;
        }
    }

    private static string? GetMonsterTypeName(NCreature creature)
    {
        try { return creature?.Entity?.Monster?.GetType().Name; }
        catch { return null; }
    }

    internal static string? FindReviveLikePower(NCreature creature)
        => FindMatchingPower(creature, ReviveLikePowerNameSubstrings);

    internal static string? FindLiveReviveAnimPower(NCreature creature)
        => FindMatchingPower(creature, LiveReviveAnimPowerNameSubstrings);

    private static string? FindMatchingPower(NCreature creature, string[] substrings)
    {
        try
        {
            var entity = creature?.Entity;
            if (entity == null) return null;
            foreach (var pm in entity.Powers)
            {
                if (pm == null) continue;
                var name = pm.GetType().Name;
                foreach (var sub in substrings)
                    if (name.IndexOf(sub, StringComparison.Ordinal) >= 0) return name;
            }
            return null;
        }
        catch { return null; }
    }

    private static bool HasDieAnimation(NCreature creature)
    {
        try
        {
            var visualsType = ReflectionCache.NCreatureVisualsType;
            if (visualsType == null) return true;
            var visuals = creature.Visuals;
            if (visuals == null) return true;
            var megaSprite = AccessTools.Property(visualsType, "SpineBody")?.GetValue(visuals);
            if (megaSprite == null) return true;
            var hasAnim = AccessTools.Method(megaSprite.GetType(), "HasAnimation", [typeof(string)]);
            if (hasAnim == null) return true;
            return hasAnim.Invoke(megaSprite, ["die"]) is not bool b || b;
        }
        catch { return true; }
    }

    private static async Task RunReplacementDeathAnim(NCreature creature, bool argZero, bool skipDetach = false)
    {
        if (creature == null || !GodotObject.IsInstanceValid(creature)) return;
        InFlightCount++;
        try { await RunReplacementDeathAnimInner(creature, argZero, skipDetach); }
        finally { InFlightCount--; }
    }

    private static async Task RunReplacementDeathAnimInner(NCreature creature, bool argZero, bool skipDetach)
    {
        float dieDuration = TryPlaySpineDie(creature);

        float spineWait = dieDuration > 0f ? dieDuration : DieVisibleFallbackSeconds;
        var monsterTypeNameForCap = GetMonsterTypeName(creature);
        if (monsterTypeNameForCap != null
            && SpineDieCappedMonsterTypes.Contains(monsterTypeNameForCap)
            && spineWait > SpineDieCapSeconds)
        {
            spineWait = SpineDieCapSeconds;
        }

        try
        {
            var tree = creature.GetTree();
            if (tree != null)
            {
                var timer = tree.CreateTimer(spineWait);
                await creature.ToSignal(timer, "timeout");
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[AnimDie] spine wait failed: {ex.Message}"); }

        if (!GodotObject.IsInstanceValid(creature)) return;

        TriggerDeathFadeVfxFireAndForget(creature);

        if (!GodotObject.IsInstanceValid(creature)) return;

        // undo revive가 대기 중에 발생했으면 detach 스킵
        try
        {
            var entityCheck = creature.Entity;
            if (entityCheck != null && !entityCheck.IsDead) return;
        }
        catch { }

        try
        {
            try
            {
                var entity = creature.Entity;
                if (entity != null) DetachedZombies[entity] = creature;
            }
            catch (Exception ex) { UndoLogger.Warn($"[AnimDie] register detached failed: {ex.Message}"); }

            var parent = creature.GetParent();
            if (parent != null) parent.RemoveChild(creature);
        }
        catch (Exception ex) { UndoLogger.Warn($"[AnimDie] detach NCreature failed: {ex.Message}"); }
    }

    private static void TriggerDeathFadeVfxFireAndForget(NCreature creature)
    {
        try
        {
            var entity = creature.Entity;
            var monster = entity?.Monster;
            if (monster == null) return;

            var shouldFadeProp = AccessTools.Property(monster.GetType(), "ShouldFadeAfterDeath");
            bool shouldFade = shouldFadeProp?.GetValue(monster) is bool b && b;
            if (!shouldFade) return;

            if (!(creature.Body?.IsVisibleInTree() ?? false)) return;

            var vfxType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Vfx.NMonsterDeathVfx");
            if (vfxType == null) return;

            var createMethod = AccessTools.Method(vfxType, "Create",
                new[] { typeof(NCreature), typeof(CancellationToken) });
            if (createMethod == null) return;

            var token = new CancellationTokenSource().Token;
            var vfx = createMethod.Invoke(null, new object[] { creature, token });
            if (vfx is not Node vfxNode) return;

            var parent = creature.GetParent();
            if (parent == null) return;

            var addSafely = AccessTools.Method(parent.GetType(), "AddChildSafely");
            try
            {
                if (addSafely != null) addSafely.Invoke(null, new object[] { parent, vfxNode });
                else parent.AddChild(vfxNode);
            }
            catch
            {
                try { parent.AddChild(vfxNode); } catch { return; }
            }
            try { parent.MoveChild(vfxNode, creature.GetIndex()); } catch { }

            var playMethod = AccessTools.Method(vfxType, "PlayVfx");
            if (playMethod == null) return;
            try { playMethod.Invoke(vfx, null); } catch { }
        }
        catch (Exception ex) { UndoLogger.Warn($"[AnimDie] death fade vfx failed: {ex.Message}"); }
    }

    private static float TryPlaySpineDie(NCreature creature)
    {
        try
        {
            var visualsType = ReflectionCache.NCreatureVisualsType;
            if (visualsType == null) return 0f;
            var visuals = creature.Visuals;
            if (visuals == null) return 0f;
            var spine = ReflectionCache.NCVSpineAnimationProp?.GetValue(visuals);
            if (spine == null) return 0f;
            var trackEntry = ReflectionCache.SpineSetAnimationMethod?.Invoke(spine, ["die", false, 0]);
            if (trackEntry == null) return 0f;
            var anim = ReflectionCache.TrackGetAnimationMethod?.Invoke(trackEntry, null);
            if (anim == null) return 0f;
            var durObj = ReflectionCache.AnimationGetDurationMethod?.Invoke(anim, null);
            return durObj is float f && f > 0f ? f : 0f;
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[AnimDie] spine die track failed: {ex.Message}");
            return 0f;
        }
    }
}