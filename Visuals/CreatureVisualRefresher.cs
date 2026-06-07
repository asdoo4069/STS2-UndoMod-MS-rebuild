using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using UndoModMS.Snapshot;

namespace UndoModMS.Visuals;

internal static class CreatureVisualRefresher
{
    public static void Refresh(CombatSnapshot snap)
    {
        var room = NCombatRoom.Instance;
        if (room == null) { UndoLogger.Warn("[CreatureVisual] NCombatRoom.Instance null"); return; }

        foreach (var saved in snap.Creatures)
        {
            var live = saved.Ref;
            if (live == null) continue;

            var node = FindNode(room, live);
            bool wasAliveInSnap = !saved.IsDead;
            bool inRemovingList = node != null && IsInRemovingList(room, node);

            bool isReviveLikeCreature = node != null
                && Patches.AnimDiePatch.FindReviveLikePower(node) != null;
            bool isLiveReviveAnim = node != null
                && Patches.AnimDiePatch.FindLiveReviveAnimPower(node) != null;

            if (isReviveLikeCreature)
            {
                UndoLogger.Warn($"[CreatureVisual] id={saved.CombatId} has revive-like power — skipping body manipulation");
            }

            bool zombieDegraded = node != null && SnapshotRestorer.IsZombieDegraded(node);
            if (zombieDegraded && node != null)
            {
                try { SnapshotRestorer.TryRestoreBodyFromSavedRef(node, saved); }
                catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] TryRestoreBodyFromSavedRef failed id={saved.CombatId}: {ex.Message}"); }
                zombieDegraded = SnapshotRestorer.IsZombieDegraded(node);
            }

            if (wasAliveInSnap)
            {
                if (node == null)
                {
                    try { room.AddCreature(live); }
                    catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] AddCreature failed id={saved.CombatId}: {ex.Message}"); }
                    node = FindNode(room, live);
                    if (node != null) try { node.StartReviveAnim(); } catch { }
                }
                else if (!zombieDegraded && inRemovingList)
                {
                    try { node.StartReviveAnim(); } catch { }
                }
            }

            if (zombieDegraded) continue;

            if (!isLiveReviveAnim && node != null && saved.HadVisualNode)
            {
                if (wasAliveInSnap)
                    TryCancelHurtAnim(node);

                try { node.GlobalPosition = saved.VisualPosition; } catch { }
                try
                {
                    var body = node.Body;
                    if (body != null)
                    {
                        body.Scale = saved.VisualBodyScale;
                        body.Position = saved.VisualBodyPosition;
                        body.Rotation = saved.VisualBodyRotation;
                        if (wasAliveInSnap) body.Visible = true;
                        if (body is Godot.CanvasItem bodyCi)
                            bodyCi.Modulate = saved.VisualBodyModulate;
                    }
                }
                catch { }

                if (wasAliveInSnap)
                {
                    try
                    {
                        if (node.Visuals is Godot.Node2D visualsN2D)
                            visualsN2D.Visible = true;
                    }
                    catch { }
                }

                try
                {
                    var visuals = node.Visuals;
                    if (visuals != null && saved.Hue.HasValue
                        && ReflectionCache.NCVHueField is var hf && hf != null)
                        hf.SetValue(visuals, saved.Hue.Value);

                    if (visuals != null && saved.LiquidOverlayTimer.HasValue
                        && ReflectionCache.NCVLiquidOverlayTimerField is var tf && tf != null)
                        tf.SetValue(visuals, saved.LiquidOverlayTimer.Value);

                    if (visuals != null)
                    {
                        try
                        {
                            ReflectionCache.NCVLiquidOverlayTimerField?.SetValue(visuals, 0.0);

                            var spineBodyProp = HarmonyLib.AccessTools.Property(visuals.GetType(), "SpineBody");
                            var spineBody = spineBodyProp?.GetValue(visuals);
                            if (spineBody != null && saved.BodyNormalMaterial != null
                                && ReflectionCache.MegaSpriteSetNormalMaterialMethod != null)
                            {
                                ReflectionCache.MegaSpriteSetNormalMaterialMethod
                                    .Invoke(spineBody, [saved.BodyNormalMaterial]);
                            }

                            ReflectionCache.NCVCurrentLiquidOverlayMaterialField?.SetValue(visuals, null);
                            ReflectionCache.NCVSavedNormalMaterialField?.SetValue(visuals, saved.BodyNormalMaterial);
                        }
                        catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] overlay clear failed: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] hue restore failed: {ex.Message}"); }

                string? desired = saved.SpineAnimNameTrack0;
                if (string.IsNullOrEmpty(desired) && live != null
                    && CombatSnapshot.IdleAnimCache.TryGetValue(live, out var cached))
                    desired = cached;
                if (!string.IsNullOrEmpty(desired))
                    TryRestoreSpineAnim(node, desired!, saved);
            }

            try { _ = node?.RefreshIntents(); } catch { }

            if (!saved.IsDead && node != null)
                ResetTargetingState(node, saved.CombatId, fullRestore: isReviveLikeCreature);
        }

        foreach (var saved in snap.Creatures)
        {
            var creature = saved.Ref;
            if (creature == null) continue;
            var node = room.GetCreatureNode(creature);
            if (node == null) continue;

            bool isLiveReviveAnim = Patches.AnimDiePatch.FindLiveReviveAnimPower(node) != null;

            foreach (var n in SnapshotRestorer.WalkNodeTree(node))
            {
                if (n is not NCreatureStateDisplay sd) continue;
                try
                {
                    KillStateDisplayTween(sd, ReflectionCache.NCreatureStateDisplayShowHideTweenField);
                    KillStateDisplayTween(sd, ReflectionCache.NCreatureStateDisplayHoverTweenField);

                    var mod = sd.Modulate;
                    if (!saved.IsDead)
                    {
                        sd.Visible = true;
                        mod.A = 1f;
                        sd.Modulate = mod;
                        if (ReflectionCache.NCreatureStateDisplayOriginalPositionField?.GetValue(sd)
                            is Godot.Vector2 origPos)
                            sd.Position = origPos;
                    }
                    else if (!isLiveReviveAnim)
                    {
                        sd.Visible = false;
                        mod.A = 0f;
                        sd.Modulate = mod;
                    }

                    ReflectionCache.NCreatureStateDisplayRefreshValuesMethod?.Invoke(sd, null);
                }
                catch (Exception ex)
                { UndoLogger.Warn($"[CreatureVisual] StateDisplay.RefreshValues: {ex.Message}"); }
            }
        }
    }

    private static void ResetTargetingState(NCreature node, uint combatId, bool fullRestore = false)
    {
        try
        {
            bool resetSomething = false;
            foreach (var fieldName in new[] { "<IsFocused>k__BackingField", "_isFocused", "isFocused" })
            {
                var f = HarmonyLib.AccessTools.Field(typeof(NCreature), fieldName);
                if (f != null) { f.SetValue(node, false); resetSomething = true; }
            }
            if (!resetSomething)
                UndoLogger.Warn("[Targeting] could not find IsFocused backing field on NCreature");

            if (fullRestore)
            {
                try
                {
                    if (node.Hitbox != null)
                    {
                        var prevMf = node.Hitbox.MouseFilter;
                        var prevFm = node.Hitbox.FocusMode;
                        var prevVis = node.Hitbox.Visible;
                        node.Hitbox.MouseFilter = Godot.Control.MouseFilterEnum.Stop;
                        node.Hitbox.FocusMode = Godot.Control.FocusModeEnum.All;
                        node.Hitbox.Visible = true;
                        if (prevMf != Godot.Control.MouseFilterEnum.Stop
                            || prevFm != Godot.Control.FocusModeEnum.All
                            || !prevVis)
                        {
                            UndoLogger.Warn($"[Targeting] hitbox restore id={combatId} mf {prevMf}->Stop fm {prevFm}->All vis {prevVis}->True");
                        }
                    }
                }
                catch (Exception ex) { UndoLogger.Warn($"[Targeting] hitbox restore failed id={combatId}: {ex.Message}"); }

                try { node.ToggleIsInteractable(true); }
                catch (Exception ex) { UndoLogger.Warn($"[Targeting] ToggleIsInteractable failed id={combatId}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Targeting] reset failed id={combatId}: {ex.Message}"); }
    }

    private static NCreature? FindNode(NCombatRoom room, Creature live)
    {
        var node = room.GetCreatureNode(live);
        if (node != null) return node;

        if (ReflectionCache.NcrRemovingNodesField?.GetValue(room)
            is System.Collections.IEnumerable removing)
        {
            foreach (var item in removing)
            {
                if (item is NCreature nc
                    && ReflectionCache.NCreatureEntityProp?.GetValue(nc) is Creature ent
                    && ReferenceEquals(ent, live))
                    return nc;
            }
        }
        return null;
    }

    private static readonly string[] TransientAnimSubstrings =
        ["attack", "cast", "hurt", "hit", "damage", "die", "death", "spawn"];

    private static void TryRestoreSpineAnim(NCreature node, string animHint, in CreatureSnapshot saved)
    {
        try
        {
            var setAnim = ReflectionCache.SpineSetAnimationMethod;
            if (setAnim == null) return;

            var spine = ReflectionCache.NCreatureSpineAnimationProp?.GetValue(node);
            if (spine == null) return;

            string? currentName = null;
            try
            {
                var track = ReflectionCache.SpineGetCurrentTrackMethod?.Invoke(spine, [0]);
                if (track != null)
                {
                    var anim = ReflectionCache.TrackGetAnimationMethod?.Invoke(track, null);
                    if (anim != null)
                    {
                        var getName = HarmonyLib.AccessTools.Method(anim.GetType(), "GetName");
                        if (getName?.Invoke(anim, null) is string s) currentName = s;
                    }
                }
            }
            catch { }

            string? target = null;
            if (!string.IsNullOrEmpty(animHint)
                && (animHint.Contains("loop", StringComparison.OrdinalIgnoreCase)
                    || animHint.Contains("idle", StringComparison.OrdinalIgnoreCase)))
                target = animHint;
            else if (currentName != null && IsTransient(currentName))
                target = saved.IsDead ? "die_loop" : "idle_loop";

            if (target == null) return;

            try
            {
                var visualsType = ReflectionCache.NCreatureVisualsType;
                object? visuals = null;
                if (visualsType != null)
                    foreach (var n in SnapshotRestorer.WalkNodeTree(node))
                        if (visualsType.IsInstanceOfType(n)) { visuals = n; break; }

                if (visuals != null)
                {
                    var spineBodyProp = HarmonyLib.AccessTools.Property(visualsType!, "SpineBody");
                    var megaSprite = spineBodyProp?.GetValue(visuals);
                    var skel = ReflectionCache.MegaSpriteGetSkeletonMethod?.Invoke(megaSprite, null);
                    if (skel != null && !saved.IsDead)
                    {
                        ReflectionCache.SkeletonSetSlotsToSetupPoseMethod?.Invoke(skel, null);
                        HarmonyLib.AccessTools.Method(skel.GetType(), "SetBonesToSetupPose")?.Invoke(skel, null);
                        HarmonyLib.AccessTools.Method(skel.GetType(), "SetToSetupPose")?.Invoke(skel, null);
                    }

                    try { HarmonyLib.AccessTools.Method(megaSprite?.GetType(), "SetTimeScale")?.Invoke(megaSprite, [1f]); } catch { }
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] setup-pose reset failed: {ex.Message}"); }

            var entry = setAnim.Invoke(spine, [target, true, 0]);
            if (entry != null)
            {
                try { HarmonyLib.AccessTools.Method(entry.GetType(), "SetMixDuration")?.Invoke(entry, [0f]); } catch { }
                try { HarmonyLib.AccessTools.Method(entry.GetType(), "SetTimeScale")?.Invoke(entry, [1f]); } catch { }
            }

            try { HarmonyLib.AccessTools.Method(spine.GetType(), "SetTimeScale")?.Invoke(spine, [1f]); } catch { }

            // track 1~3: 스냅샷에 저장된 값으로 복원. null이었으면 현재 트랙 클리어.
            var trackSnaps = new[] { saved.SpineAnimNameTrack1, saved.SpineAnimNameTrack2, saved.SpineAnimNameTrack3 };
            for (int ti = 1; ti <= 3; ti++)
            {
                try
                {
                    var snapAnim = trackSnaps[ti - 1];
                    if (!string.IsNullOrEmpty(snapAnim)) setAnim.Invoke(spine, [snapAnim, true, ti]);
                    else
                    {
                        var t = ReflectionCache.SpineGetCurrentTrackMethod?.Invoke(spine, [ti]);
                        if (t == null) continue;
                        var a = ReflectionCache.TrackGetAnimationMethod?.Invoke(t, null);
                        if (a == null) continue;
                        setAnim.Invoke(spine, [target, true, ti]);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] spine restore failed: {ex.Message}"); }
    }

    private static bool IsTransient(string name)
    {
        foreach (var s in TransientAnimSubstrings)
            if (name.Contains(s, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void TryCancelHurtAnim(NCreature node)
    {
        foreach (var n in SnapshotRestorer.WalkNodeTree(node))
        {
            if (n is Godot.AnimationPlayer ap)
            {
                try { if (ap.IsPlaying()) ap.Stop(keepState: false); } catch { }
            }
        }

        try
        {
            var body = node.Body;
            if (body != null)
            {
                body.Position = Godot.Vector2.Zero;
                body.RotationDegrees = 0f;
                body.Modulate = Godot.Colors.White;
                body.SelfModulate = Godot.Colors.White;
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[CreatureVisual] body reset failed: {ex.Message}"); }
    }

    private static void KillStateDisplayTween(NCreatureStateDisplay sd, System.Reflection.FieldInfo? field)
    {
        if (field == null) return;
        try
        {
            if (field.GetValue(sd) is Godot.Tween tw
                && Godot.GodotObject.IsInstanceValid(tw)
                && tw.IsValid())
                tw.Kill();
        }
        catch { }
    }

    private static bool IsInRemovingList(NCombatRoom room, NCreature node)
    {
        if (ReflectionCache.NcrRemovingNodesField?.GetValue(room)
            is not System.Collections.IEnumerable removing) return false;
        foreach (var item in removing)
            if (ReferenceEquals(item, node)) return true;
        return false;
    }
}