using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace UndoModMS.Patches;

[HarmonyPatch(typeof(NCreature), "StartDeathAnim")]
public static class DeathAnimDelayPatch
{
    public const float DeferSeconds = 0.2f;

    private static readonly Dictionary<ulong, (Godot.Timer timer, NCreature creature, bool arg)> _pending = new();
    private static bool _bypass;

    [HarmonyPrefix]
    public static bool Prefix(NCreature __instance, bool __0)
    {
        if (_bypass) return true;

        try
        {
            if (!Godot.GodotObject.IsInstanceValid(__instance)) return true;

            if (TryGetMonsterTypeName(__instance) is { } typeName
                && AnimDiePatch.SkipReplacementMonsterTypes.Contains(typeName))
            {
                UndoLogger.Warn($"[DeathDefer] monster={typeName} instId={__instance.GetInstanceId()} on AnimDie skiplist — running vanilla StartDeathAnim immediately");
                return true;
            }

            if (HasReviveLikePower(__instance) is { } powerName)
            {
                UndoLogger.Warn($"[DeathDefer] creature has revive-like power '{powerName}' instId={__instance.GetInstanceId()} — running vanilla StartDeathAnim immediately");
                return true;
            }

            ulong id = __instance.GetInstanceId();

            if (_pending.ContainsKey(id)) return false;

            var timer = new Godot.Timer
            {
                OneShot = true,
                WaitTime = DeferSeconds,
            };
            __instance.AddChild(timer);

            timer.Timeout += () => OnDeferredFire(id);

            _pending[id] = (timer, __instance, __0);
            timer.Start();

            UndoLogger.Info($"[DeathDefer] deferred StartDeathAnim on creature instId={id} arg={__0} for {DeferSeconds:F2}s");
            return false;
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[DeathDefer] defer setup failed: {ex.Message} — letting original run");
            return true;
        }
    }

    private static void OnDeferredFire(ulong id)
    {
        if (!_pending.TryGetValue(id, out var entry)) return;
        _pending.Remove(id);

        try { entry.timer.QueueFree(); } catch { }

        if (!Godot.GodotObject.IsInstanceValid(entry.creature))
        {
            UndoLogger.Info($"[DeathDefer] timer fired but creature instId={id} freed — skipping");
            return;
        }

        UndoLogger.Info($"[DeathDefer] timer fired — running real StartDeathAnim on instId={id}");
        _bypass = true;
        try { entry.creature.StartDeathAnim(entry.arg); }
        catch (Exception ex) { UndoLogger.Warn($"[DeathDefer] deferred StartDeathAnim threw: {ex.Message}"); }
        finally { _bypass = false; }
    }

    public static int AbortAllPending()
    {
        if (_pending.Count == 0) return 0;
        int aborted = 0;
        foreach (var kv in _pending.ToList())
        {
            try
            {
                if (Godot.GodotObject.IsInstanceValid(kv.Value.timer))
                {
                    kv.Value.timer.Stop();
                    kv.Value.timer.QueueFree();
                }
                aborted++;
            }
            catch { }
        }
        _pending.Clear();
        UndoLogger.Info($"[DeathDefer] aborted {aborted} pending death anim(s) for undo");
        return aborted;
    }

    public static int FlushForCombatEnd()
    {
        if (_pending.Count == 0) return 0;
        int flushed = 0;
        foreach (var kv in _pending.ToList())
        {
            try
            {
                if (Godot.GodotObject.IsInstanceValid(kv.Value.timer))
                {
                    kv.Value.timer.Stop();
                    kv.Value.timer.QueueFree();
                }
                if (Godot.GodotObject.IsInstanceValid(kv.Value.creature))
                {
                    _bypass = true;
                    try { kv.Value.creature.StartDeathAnim(kv.Value.arg); }
                    catch (Exception ex) { UndoLogger.Warn($"[DeathDefer] flush StartDeathAnim threw on instId={kv.Key}: {ex.Message}"); }
                    finally { _bypass = false; }
                }
                flushed++;
            }
            catch { }
        }
        _pending.Clear();
        UndoLogger.Info($"[DeathDefer] flushed {flushed} pending death(s) for combat-end (anim allowed to run)");
        return flushed;
    }

    public static void ClearAll() => AbortAllPending();

    private static string? TryGetMonsterTypeName(NCreature creature)
    {
        try
        {
            var monster = creature?.Entity?.Monster;
            return monster?.GetType().Name;
        }
        catch { return null; }
    }

    private static readonly string[] _reviveLikeSubstrings =
        { "Revive", "Reborn", "Reincarn", "PreventDeath", "InvincibleOnDeath", "Illusion", "DieForYou" };

    private static string? HasReviveLikePower(NCreature creature)
    {
        try
        {
            var entity = creature?.Entity;
            if (entity == null) return null;
            foreach (var pm in entity.Powers)
            {
                if (pm == null) continue;
                var name = pm.GetType().Name;
                foreach (var sub in _reviveLikeSubstrings)
                {
                    if (name.IndexOf(sub, StringComparison.Ordinal) >= 0) return name;
                }
            }
            return null;
        }
        catch { return null; }
    }
}