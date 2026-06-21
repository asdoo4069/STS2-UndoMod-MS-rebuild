using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

internal static class SpecialCreatureVisualRefresher
{
    public static void Refresh(Creature creature, NCreature? node)
    {
        if (node == null) return;

        switch (creature.Monster)
        {
            case WaterfallGiant waterfallGiant:
                NormalizeWaterfallGiant(waterfallGiant, node);
                break;

            case TestSubject testSubject:
                NormalizeTestSubject(testSubject, node);
                break;
        }
    }

    // ── 실험체 ────────────────────────────────────────────────

    private static void NormalizeTestSubject(TestSubject monster, NCreature creatureNode)
    {
        int respawns = ReadInt(monster, "_respawns");
        int phaseIndex = Math.Clamp(respawns + 1, 1, 3);
        creatureNode.SetDefaultScaleTo(1f + respawns * 0.1f, 0f);

        bool isIntangible = monster.Creature.HasPower<IntangiblePower>();
        var canvasGroup = creatureNode.GetSpecialNode<CanvasGroup>("%CanvasGroup");
        if (canvasGroup != null)
            canvasGroup.SelfModulate = isIntangible ? StsColors.halfTransparentWhite : Colors.White;

        if (IsPendingRevive(monster.Creature))
        {
            creatureNode.SetAnimationTrigger("DeadTrigger");
            EnsureBaseAnimation(creatureNode, $"knocked_out_loop{phaseIndex}", loop: true);
            return;
        }

        if (monster.Creature.IsDead)
        {
            creatureNode.SetAnimationTrigger("Dead");
            EnsureBaseAnimation(creatureNode, "die", loop: false);
            return;
        }

        EnsureBaseAnimation(creatureNode, $"idle_loop{phaseIndex}", loop: true);
    }

    // ── 폭포거인 ────────────────────────────────────────────────

    private static void NormalizeWaterfallGiant(WaterfallGiant monster, NCreature creatureNode)
    {
        SyncWaterfallGiantBuildUpTrack(monster, creatureNode);
        ReconcileWaterfallGiantVfx(monster, creatureNode);

        bool aboutToBlow = ReadBool(monster, "_isAboutToBlow")
            || monster.Creature.HpDisplay.IsInfinite()
            || string.Equals(monster.NextMove?.Id, "ABOUT_TO_BLOW_MOVE", StringComparison.Ordinal);

        if (aboutToBlow)
        {
            EnsureBaseAnimation(creatureNode, "die_loop", loop: true);
            return;
        }

        if (monster.Creature.IsDead)
        {
            creatureNode.SetAnimationTrigger("Dead");
            EnsureBaseAnimation(creatureNode, "die_loop", loop: true);
            return;
        }

        EnsureBaseAnimation(creatureNode, "idle_loop", loop: true);
    }

    private static void SyncWaterfallGiantBuildUpTrack(WaterfallGiant monster, NCreature creatureNode)
    {
        var animState = GetAnimationState(creatureNode);
        if (animState == null) return;

        int idx = ReadInt(monster, "_pressureBuildupIdx");
        string? expected = idx <= 0
            ? null
            : $"_tracks/buildup{Math.Clamp((int)MathF.Floor(idx * 0.5f), 1, 3)}";
        string? current = TryGetTrackAnimName(animState, 1);

        if (string.Equals(current, expected, StringComparison.Ordinal)) return;

        if (expected == null)
        {
            try { AccessTools.Method(animState.GetType(), "AddEmptyAnimation")?.Invoke(animState, [1]); } catch { }
            return;
        }

        try { AccessTools.Method(animState.GetType(), "SetAnimation")?.Invoke(animState, [expected, true, 1]); } catch { }
    }

    private static void ReconcileWaterfallGiantVfx(WaterfallGiant monster, NCreature creatureNode)
    {
        var vfxNode = FindDescendantByTypeName(creatureNode.Visuals, "NWaterfallGiantVfx");
        if (vfxNode == null) return;

        bool aboutToBlow = ReadBool(monster, "_isAboutToBlow")
            || monster.Creature.HpDisplay.IsInfinite()
            || string.Equals(monster.NextMove?.Id, "ABOUT_TO_BLOW_MOVE", StringComparison.Ordinal);

        int idx = ReadInt(monster, "_pressureBuildupIdx");
        int buildupLevel = idx <= 0 ? 0 : Math.Clamp((int)MathF.Floor(idx * 0.5f), 1, 3);

        SetEmitter(vfxNode, "_mistParticles", emitting: true, visible: true);
        SetEmitter(vfxNode, "_dropletParticles", emitting: true, visible: true);
        SetEmitter(vfxNode, "_mouthParticles", emitting: true, visible: true);

        bool leakActive = aboutToBlow || buildupLevel > 0;
        SetEmitter(vfxNode, "_steam1Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steam2Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steam3Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steam4Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steam5Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steam6Particles", aboutToBlow, aboutToBlow);
        SetEmitter(vfxNode, "_steamLeakParticles1", leakActive, leakActive);
        SetEmitter(vfxNode, "_steamLeakParticles2", leakActive, leakActive);
        SetEmitter(vfxNode, "_steamLeakParticles3", leakActive, leakActive);
        ApplyLeakIntensity(vfxNode, buildupLevel);

        try { AccessTools.Field(vfxNode.GetType(), "_isDead")?.SetValue(vfxNode, false); } catch { }
    }

    private static void SetEmitter(Node vfxNode, string fieldName, bool emitting, bool visible)
    {
        if (AccessTools.Field(vfxNode.GetType(), fieldName)?.GetValue(vfxNode) is not GpuParticles2D emitter)
            return;

        bool wasVisible = emitter.Visible;
        bool wasEmitting = emitter.Emitting;

        emitter.Visible = visible;
        emitter.Emitting = emitting;
        if (!visible) { emitter.Restart(); return; }
        if (emitting && (!wasVisible || !wasEmitting)) emitter.Restart();
    }

    private static void ApplyLeakIntensity(Node vfxNode, int buildupLevel)
    {
        (int amount, float lifetime) = buildupLevel switch
        {
            1 => (8, 0.37f),
            2 => (15, 0.45f),
            3 => (20, 0.6f),
            _ => (0, 0f)
        };

        foreach (var fieldName in new[] { "_steamLeakParticles1", "_steamLeakParticles2", "_steamLeakParticles3" })
        {
            if (AccessTools.Field(vfxNode.GetType(), fieldName)?.GetValue(vfxNode) is not GpuParticles2D emitter)
                continue;

            if (buildupLevel <= 0) { emitter.Amount = 0; continue; }
            emitter.Amount = amount;
            emitter.Lifetime = lifetime;
        }
    }

    // ── 공통 헬퍼 ────────────────────────────────────────────────

    private static void EnsureBaseAnimation(NCreature creatureNode, string animName, bool loop)
    {
        var animState = GetAnimationState(creatureNode);
        if (animState == null) return;

        string? current = TryGetTrackAnimName(animState, 0);
        if (string.Equals(current, animName, StringComparison.Ordinal)) return;

        try { AccessTools.Method(animState.GetType(), "SetAnimation")?.Invoke(animState, [animName, loop, 0]); } catch { }
    }

    private static object? GetAnimationState(NCreature creatureNode)
    {
        try
        {
            var visuals = creatureNode.Visuals;
            if (visuals == null) return null;
            var spineBody = ReflectionCache.NCVSpineBodyProp?.GetValue(visuals);
            if (spineBody == null) return null;
            return AccessTools.Method(spineBody.GetType(), "GetAnimationState")?.Invoke(spineBody, null);
        }
        catch { return null; }
    }

    private static string? TryGetTrackAnimName(object animState, int trackIndex)
    {
        try
        {
            var track = AccessTools.Method(animState.GetType(), "GetCurrent")?.Invoke(animState, [trackIndex]);
            if (track == null) return null;
            var anim = AccessTools.Method(track.GetType(), "GetAnimation")?.Invoke(track, null);
            if (anim == null) return null;
            return AccessTools.Method(anim.GetType(), "GetName")?.Invoke(anim, null) as string;
        }
        catch { return null; }
    }

    private static Node? FindDescendantByTypeName(Node? root, string typeName)
    {
        if (root == null) return null;
        foreach (Node child in root.GetChildren().OfType<Node>())
        {
            if (string.Equals(child.GetType().Name, typeName, StringComparison.Ordinal)) return child;
            var nested = FindDescendantByTypeName(child, typeName);
            if (nested != null) return nested;
        }
        return null;
    }

    private static bool ReadBool(object monster, string fieldName)
    {
        try { return AccessTools.Field(monster.GetType(), fieldName)?.GetValue(monster) is true; }
        catch { return false; }
    }

    private static int ReadInt(object monster, string fieldName)
    {
        try { return AccessTools.Field(monster.GetType(), fieldName)?.GetValue(monster) is int v ? v : 0; }
        catch { return 0; }
    }

    private static bool IsPendingRevive(Creature creature)
    {
        return creature.Powers.Any(p =>
            AccessTools.Property(p.GetType(), "IsReviving")?.GetValue(p) is true ||
            AccessTools.Property(p.GetType(), "IsHalfDead")?.GetValue(p) is true);
    }
}