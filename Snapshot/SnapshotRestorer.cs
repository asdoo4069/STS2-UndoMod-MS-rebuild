using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;
using UndoModMS.Patches;
using UndoModMS.Visuals;

namespace UndoModMS.Snapshot;

internal static class SnapshotRestorer
{
    public static void Restore(CombatSnapshot snap)
    {
        var cm = CombatManager.Instance;
        if (cm == null) { UndoLogger.Warn("[Restore] CombatManager null"); return; }
        if (ReflectionCache.CombatManagerStateField.GetValue(cm) is not CombatState cs) { UndoLogger.Warn("[Restore] CombatState null"); return; }
        if (ReflectionCache.RunManagerStateProperty?.GetValue(RunManager.Instance) is not RunState runState) { UndoLogger.Warn("[Restore] RunState null"); return; }

        UndoLogger.Info($"[Restore] start → round={snap.RoundNumber} side={snap.CurrentSide} " +
            $"creatures={snap.Creatures.Count} (live cs.Creatures.Count={cs.Creatures.Count})");

        try { DeathAnimDelayPatch.AbortAllPending(); }
        catch (Exception ex) { UndoLogger.Warn($"[Restore] DeathDefer abort failed: {ex.Message}"); }

        Try("CombatLevel", () => RestoreCombatLevel(snap, cs));
        Try("Roster", () => RestoreCreatureRoster(snap, cs));
        Try("Player", () => RestorePlayerAndPiles(snap, cs));
        Try("Creatures", () => RestoreCreatures(snap));
        Try("Relics", () => RestoreRelics(snap));
        Try("Orbs", () => RestoreOrbs(snap, cs));
        Try("Potions", () => RestorePotions(snap, cs));
        Try("RNG", () => RestoreRunRng(snap, runState));
        Try("History", () => RestoreHistory(snap, cm));
        Try("SyncState", () => RestoreSyncState(snap));

        Try("CombatVfx", CleanCombatVfxContainers);

        Try("DeselectReticles", ClearTargetingReticles);
        Try("CreatureVisuals", () => CreatureVisualRefresher.Refresh(snap));

        Try("OrphanCards", OrphanCardCleaner.Clean);
        Try("HandVisuals", () => HandRefresher.Refresh(snap));

        Try("SnapHand", HandPositionSnapper.Snap);
        Try("PowerVisuals", () => PowerRefresher.Refresh(snap));
        Try("OrbVisuals", () => OrbRefresher.Refresh(snap));
        Try("PotionVisuals", PotionRefresher.Refresh);
        Try("PileCounts", PileCountRefresher.Refresh);
        Try("EndTurnState", EndTurnStateRefresher.Reset);

        Try("StateTracker", () =>
        {
            ReflectionCache.NotifyCombatStateChangedMethod?.Invoke(
                cm.StateTracker, ["UndoMod-MS"]);
        });

        Try("FireTurnStarted", () =>
        {
            var ev = AccessTools.Field(typeof(CombatManager), "TurnStarted");
            if (ev?.GetValue(cm) is Delegate d) d.DynamicInvoke(cs);
        });

        UndoLogger.Info("[Restore] complete");
    }

    private static void CleanCombatVfxContainers()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        foreach (var name in new[] { "CombatVfxContainer", "BackCombatVfxContainer" })
        {
            try
            {
                var prop = AccessTools.Property(typeof(NCombatRoom), name);
                if (prop?.GetValue(room) is not Node container) continue;
                foreach (Node ch in container.GetChildren())
                {
                    try { if (GodotObject.IsInstanceValid(ch)) ch.QueueFree(); } catch { }
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[CombatVfx] sweep {name}: {ex.Message}"); }
        }

        try
        {
            foreach (var ncreature in NCombatRoom.Instance!.CreatureNodes)
            {
                if (ncreature == null || !GodotObject.IsInstanceValid(ncreature)) continue;
                foreach (var node in CombatSnapshot.EnumerateTree(ncreature))
                {
                    if (node == null || node == ncreature) continue;
                    try { if (ncreature.Visuals != null && ncreature.Visuals.IsAncestorOf(node)) continue; } catch { }
                    var t = node.GetType().Name;
                    if (!t.Contains("Vfx") && !t.EndsWith("Effect")) continue;
                    try { node.QueueFree(); } catch { }
                }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[CreatureVfx] sweep failed: {ex.Message}"); }
    }

    private static void ResetReticleCancelTokens(Node ncreature, uint combatId)
    {
        try
        {
            var reticleType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle");
            if (reticleType == null) return;
            var ctsField = AccessTools.Field(reticleType, "_cancelToken");
            if (ctsField == null) return;
            foreach (var node in CombatSnapshot.EnumerateTree(ncreature))
            {
                if (node == null || !reticleType.IsInstanceOfType(node)) continue;
                try { ctsField.SetValue(node, new CancellationTokenSource()); } catch { }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Revive] id={combatId} reticle CTS reset: {ex.Message}"); }
    }

    private static void ClearTargetingReticles()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        var reticleType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle");
        var onDeselect = reticleType != null ? AccessTools.Method(reticleType, "OnDeselect") : null;
        var isSelectedSetter = reticleType != null ? AccessTools.PropertySetter(reticleType, "IsSelected") : null;
        foreach (var ncreature in room.CreatureNodes)
        {
            if (ncreature == null || !GodotObject.IsInstanceValid(ncreature)) continue;
            foreach (var node in CombatSnapshot.EnumerateTree(ncreature))
            {
                if (node == null || reticleType == null || !reticleType.IsInstanceOfType(node)) continue;
                try
                {
                    onDeselect?.Invoke(node, null);
                    if (node is CanvasItem ci) ci.Modulate = Colors.Transparent;
                    isSelectedSetter?.Invoke(node, [false]);
                }
                catch (Exception ex) { UndoLogger.Warn($"[Reticle] clear: {ex.Message}"); }
            }
        }
    }

    private static void Try(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { UndoLogger.Warn($"[Restore] {name} failed: {ex.Message}"); }
    }

    private static void RestoreCombatLevel(CombatSnapshot snap, CombatState cs)
    {
        TrySetProperty(cs, nameof(CombatState.RoundNumber), snap.RoundNumber);
        TrySetProperty(cs, nameof(CombatState.CurrentSide), snap.CurrentSide);
        ReflectionCache.NextCreatureIdField?.SetValue(cs, snap.NextCreatureId);
    }

    private static void RestorePlayerAndPiles(CombatSnapshot snap, CombatState cs)
    {
        foreach (var ally in cs.Allies)
        {
            var player = ally.Player;
            if (player == null) continue;
            var pcs = player.PlayerCombatState;
            if (pcs != null)
            {
                ReflectionCache.PcsEnergyField.SetValue(pcs, snap.Energy);
                ReflectionCache.PcsStarsField.SetValue(pcs, snap.Stars);
                RestoreCardPiles(snap, pcs);
                RestoreCardMutableState(snap);
            }
            ReflectionCache.PlayerGoldField?.SetValue(player, snap.Gold);
            break;
        }
    }

    private static void RestoreCardPiles(CombatSnapshot snap, PlayerCombatState pcs)
    {
        foreach (var pile in pcs.AllPiles)
        {
            if (!snap.PileRefs.TryGetValue(pile.Type, out var savedCards)) continue;
            var liveCards = ReflectionCache.CardPileCardsField.GetValue(pile) as System.Collections.IList;
            if (liveCards == null) continue;
            liveCards.Clear();
            foreach (var c in savedCards) liveCards.Add(c);
            var contentsChanged = AccessTools.Field(typeof(CardPile), "ContentsChanged");
            (contentsChanged?.GetValue(pile) as Delegate)?.DynamicInvoke();
        }
    }

    private static void RestoreCardMutableState(CombatSnapshot snap)
    {
        foreach (var (live, clone) in snap.CardMutableClones)
        {
            foreach (var field in ReflectionCache.CardMutableFields)
            {
                try { field.SetValue(live, field.GetValue(clone)); } catch { }
            }
            FixCardBackReferences(live);
        }
    }

    private static void FixCardBackReferences(CardModel live)
    {
        try
        {
            var energyCost = ReflectionCache.CardEnergyCostProp?.GetValue(live);
            if (energyCost != null && ReflectionCache.EnergyCostCardField != null)
                ReflectionCache.EnergyCostCardField.SetValue(energyCost, live);
        }
        catch (Exception ex) { UndoLogger.Warn($"[Card] EnergyCost back-ref fix: {ex.Message}"); }

        try
        {
            var dyn = ReflectionCache.CardDynamicVarsProp?.GetValue(live);
            if (dyn != null && ReflectionCache.DynamicVarsInitializeWithOwnerMethod != null)
                ReflectionCache.DynamicVarsInitializeWithOwnerMethod.Invoke(dyn, [live]);
        }
        catch (Exception ex) { UndoLogger.Warn($"[Card] DynamicVars back-ref fix: {ex.Message}"); }

        try
        {
            var ench = ReflectionCache.CardEnchantmentProp?.GetValue(live);
            if (ench != null && ReflectionCache.EnchantmentCardField != null)
                ReflectionCache.EnchantmentCardField.SetValue(ench, live);
        }
        catch (Exception ex) { UndoLogger.Warn($"[Card] Enchantment back-ref fix: {ex.Message}"); }

        try
        {
            var afflict = ReflectionCache.CardAfflictionProp?.GetValue(live);
            if (afflict != null && ReflectionCache.AfflictionCardField != null)
                ReflectionCache.AfflictionCardField.SetValue(afflict, live);
        }
        catch (Exception ex) { UndoLogger.Warn($"[Card] Affliction back-ref fix: {ex.Message}"); }
    }

    private static void RestoreCreatureRoster(CombatSnapshot snap, CombatState cs)
    {
        if (ReflectionCache.CsAlliesField.GetValue(cs) is not System.Collections.IList alliesList) return;
        if (ReflectionCache.CsEnemiesField.GetValue(cs) is not System.Collections.IList enemiesList) return;

        var snapIds = new HashSet<uint>();
        foreach (var s in snap.Creatures) snapIds.Add(s.CombatId);

        int removed = RemoveCreaturesNotIn(snapIds, alliesList);
        removed += RemoveCreaturesNotIn(snapIds, enemiesList);

        var liveIds = new HashSet<uint>();
        foreach (var c in cs.Creatures)
            if (c.CombatId.HasValue) liveIds.Add(c.CombatId.Value);

        int revived = 0;
        foreach (var saved in snap.Creatures)
        {
            if (saved.Ref == null) continue;
            if (liveIds.Contains(saved.CombatId)) continue;
            if (saved.IsDead) continue;
            if (ReviveCreature(saved, cs)) revived++;
        }

        if (revived + removed > 0)
        {
            UndoLogger.Info($"[Roster] revived={revived} removed={removed}");
            var changed = AccessTools.Field(typeof(CombatState), "CreaturesChanged");
            try { (changed?.GetValue(cs) as Delegate)?.DynamicInvoke(cs); } catch { }
        }
    }

    private static bool ReviveCreature(CreatureSnapshot saved, CombatState cs)
    {
        var creature = saved.Ref;
        var cm = CombatManager.Instance;
        if (cm == null) return false;

        try
        {
            int beforeCount = cs.Creatures.Count;

            SetCombatStateOnCreature(creature, cs);

            int oldHpRev = (int)(ReflectionCache.CreatureHpField.GetValue(creature) ?? 0);
            int oldMaxHpRev = (int)(ReflectionCache.CreatureMaxHpField.GetValue(creature) ?? 0);
            int oldBlockRev = (int)(ReflectionCache.CreatureBlockField.GetValue(creature) ?? 0);
            ReflectionCache.CreatureHpField.SetValue(creature, saved.CurrentHp);
            ReflectionCache.CreatureMaxHpField.SetValue(creature, saved.MaxHp);
            ReflectionCache.CreatureBlockField.SetValue(creature, saved.Block);

            ResetIsDeadIfPresent(creature, saved.IsDead);
            creature.ShowsInfiniteHp = saved.ShowsInfiniteHp;

            FireDelegateField(creature, ReflectionCache.CreatureCurrentHpChangedField, oldHpRev, saved.CurrentHp);
            FireDelegateField(creature, ReflectionCache.CreatureMaxHpChangedField, oldMaxHpRev, saved.MaxHp);
            FireDelegateField(creature, ReflectionCache.CreatureBlockChangedField, oldBlockRev, saved.Block);
            FireDelegateField(creature, ReflectionCache.CreatureRevivedField, creature);

            var targetList = creature.Side == CombatSide.Enemy
                ? ReflectionCache.CsEnemiesField.GetValue(cs) as System.Collections.IList
                : ReflectionCache.CsAlliesField.GetValue(cs) as System.Collections.IList;
            if (targetList != null && !targetList.Contains(creature))
                targetList.Add(creature);

            if (creature.Monster != null)
                ReflectionCache.MonsterMoveStateMachineField?.SetValue(creature.Monster, null);

            cm.AddCreature(creature);

            int afterCount = cs.Creatures.Count;
            UndoLogger.Info($"[Revive] id={saved.CombatId} side={creature.Side} hp={creature.CurrentHp}/{creature.MaxHp} " +
                $"creature.IsDead={creature.IsDead} CombatId.HasValue={creature.CombatId.HasValue} " +
                $"cs.Creatures: {beforeCount}→{afterCount}");

            RestoreCreaturePowers(creature, saved);

            if (creature.Monster != null)
            {
                if (saved.MonsterRng.HasValue)
                {
                    var (seed, counter) = saved.MonsterRng.Value;
                    ReflectionCache.MonsterRngField?.SetValue(creature.Monster, new Rng(seed, counter));
                }
                if (saved.MonsterMove.HasValue)
                    RestoreMonsterMove(creature.Monster, saved.MonsterMove.Value);
                if (saved.MonsterFields != null)
                    RestoreMonsterFields(creature.Monster, saved.MonsterFields);
            }

            var room = NCombatRoom.Instance;
            if (room != null)
            {
                bool inPlaceRevived = TryInPlaceRevive(room, creature, saved.CombatId);

                bool degraded = false;
                if (inPlaceRevived)
                {
                    var probe = room.GetCreatureNode(creature);
                    if (probe == null || IsZombieDegraded(probe))
                    {
                        degraded = true;
                        UndoLogger.Warn($"[Revive] id={saved.CombatId} in-place succeeded but " +
                            $"body=null after cancel — race lost, recreating fresh");
                    }
                }

                if (!inPlaceRevived || degraded)
                {
                    UndoLogger.Info($"[Revive] id={saved.CombatId} " +
                        $"{(degraded ? "post-revive degraded" : "no zombie found")}, " +
                        $"falling back to AddCreature path");
                    DestroyZombieNCreatures(room, creature, saved.CombatId);
                    try { room.AddCreature(creature); } catch (Exception vex) { UndoLogger.Warn($"[Revive] room.AddCreature failed: {vex.Message}"); }
                }

                var node = room.GetCreatureNode(creature);
                if (node != null)
                {
                    TryRestoreBodyFromSavedRef(node, saved);

                    try
                    {
                        var setInteractable = AccessTools.Method(
                            typeof(NCombatRoom), "SetCreatureIsInteractable",
                            [typeof(Creature), typeof(bool)]);
                        setInteractable?.Invoke(room, [creature, true]);
                    }
                    catch (Exception ex) { UndoLogger.Warn($"[Revive] SetCreatureIsInteractable failed: {ex.GetType().Name}:{ex.Message}"); }

                    try
                    {
                        node.ToggleIsInteractable(true);
                        if (node.Hitbox != null)
                        {
                            node.Hitbox.MouseFilter = Control.MouseFilterEnum.Stop;
                            node.Hitbox.FocusMode = Control.FocusModeEnum.All;
                            node.Hitbox.Visible = true;
                        }
                        foreach (var fieldName in new[] { "<IsFocused>k__BackingField", "_isFocused", "isFocused" })
                        {
                            var f = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature), fieldName);
                            if (f != null) { f.SetValue(node, false); break; }
                        }
                    }
                    catch (Exception ex) { UndoLogger.Warn($"[Revive] direct hitbox restore failed: {ex.Message}"); }

                    try
                    {
                        var updateNav = AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation");
                        updateNav?.Invoke(room, null);
                    }
                    catch (Exception ex) { UndoLogger.Warn($"[Revive] UpdateCreatureNavigation failed: {ex.GetType().Name}:{ex.Message}"); }

                    try
                    {
                        var adjust = AccessTools.Method(typeof(NCombatRoom), "AdjustCreatureScaleForAspectRatio");
                        adjust?.Invoke(room, null);
                    }
                    catch (Exception ex) { UndoLogger.Warn($"[Revive] AdjustCreatureScaleForAspectRatio failed: {ex.GetType().Name}:{ex.Message}"); }

                    bool runSkinInit = !inPlaceRevived || degraded;
                    if (creature.Monster != null && runSkinInit)
                    {
                        foreach (var n in WalkNodeTree(node))
                        {
                            if (ReflectionCache.NCreatureVisualsType?.IsInstanceOfType(n) != true) continue;
                            try
                            {
                                var setUpSkin = AccessTools.Method(
                                    ReflectionCache.NCreatureVisualsType!, "SetUpSkin",
                                    [typeof(MonsterModel)]);
                                setUpSkin?.Invoke(n, [creature.Monster]);
                            }
                            catch (Exception ex) { UndoLogger.Warn($"[Revive] SetUpSkin failed: {ex.GetType().Name}:{ex.Message}"); }

                            try
                            {
                                var ready = AccessTools.Method(n.GetType(), "_Ready");
                                ready?.Invoke(n, null);
                            }
                            catch (Exception ex) { UndoLogger.Warn($"[Revive] NCreatureVisuals._Ready failed: {ex.GetType().Name}:{ex.Message}"); }
                        }
                    }

                    TryReparentVisualsUnderCreature(node, saved.CombatId);
                    TryReattachDeathDetachedNodes(node, saved.CombatId);

                    if (saved.HadVisualNode)
                    {
                        try { node.GlobalPosition = saved.VisualPosition; } catch { }
                        try
                        {
                            var body = node.Body;
                            if (body != null)
                            {
                                body.Scale = saved.VisualBodyScale;
                                body.Position = saved.VisualBodyPosition;
                                body.Rotation = saved.VisualBodyRotation;
                                body.Visible = true;
                                if (body is CanvasItem bodyCi)
                                    bodyCi.Modulate = saved.VisualBodyModulate;
                            }
                            if (node.Visuals is Node2D visualsN2D)
                                visualsN2D.Visible = true;
                        }
                        catch { }
                    }
                }
                else
                {
                    UndoLogger.Warn($"[Revive] no visual node found after AddCreature for id={saved.CombatId}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[Revive] id={saved.CombatId} failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    internal static void TryRestoreBodyFromSavedRef(
        MegaCrit.Sts2.Core.Nodes.Combat.NCreature node, CreatureSnapshot saved)
    {
        try
        {
            var visuals = node.Visuals;
            if (visuals == null) return;

            var bodyField = AccessTools.Field(visuals.GetType(), "_body");
            var liveBody = bodyField?.GetValue(visuals) as Node;

            bool liveOk = liveBody != null && GodotObject.IsInstanceValid(liveBody);
            try { if (liveOk) liveOk = liveBody!.IsInsideTree(); } catch { liveOk = false; }

            bool parentOk = false;
            if (liveOk)
            {
                try { parentOk = ReferenceEquals(liveBody!.GetParent(), visuals); } catch { }
            }

            if (liveOk && parentOk) return;

            if (liveOk && !parentOk)
            {
                try
                {
                    var oldParent = liveBody!.GetParent();
                    if (oldParent != null) oldParent.RemoveChild(liveBody);
                    if (visuals is Node visualsNode2)
                    {
                        visualsNode2.AddChild(liveBody);
                        bodyField?.SetValue(visuals, liveBody);
                        if (liveBody is Node2D body2d)
                        {
                            try { body2d.Position = saved.VisualBodyPosition; } catch { }
                            try { body2d.Scale = saved.VisualBodyScale; } catch { }
                            try { body2d.Rotation = saved.VisualBodyRotation; } catch { }
                            try { body2d.Visible = true; } catch { }
                            try { if (body2d is CanvasItem bodyCi) bodyCi.Modulate = saved.VisualBodyModulate; } catch { }
                        }
                    }
                    return;
                }
                catch (Exception ex) { UndoLogger.Warn($"[BodyRestore] vfx-reparent rescue failed: {ex.Message}"); }
            }

            if (saved.BodyRef == null) return;

            bool savedValid = GodotObject.IsInstanceValid(saved.BodyRef);
            if (!savedValid) return;

            try
            {
                var curParent = saved.BodyRef.GetParent();
                if (curParent != null) curParent.RemoveChild(saved.BodyRef);
            }
            catch (Exception ex) { UndoLogger.Warn($"[BodyRestore] detach old parent failed: {ex.Message}"); }

            try
            {
                if (visuals is Node visualsNode)
                {
                    visualsNode.AddChild(saved.BodyRef);
                    bodyField?.SetValue(visuals, saved.BodyRef);
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[BodyRestore] re-attach failed: {ex.GetType().Name}:{ex.Message}"); }
        }
        catch (Exception ex) { UndoLogger.Warn($"[BodyRestore] id={saved.CombatId} failed: {ex.Message}"); }
    }

    private static bool TryInPlaceRevive(NCombatRoom room, Creature creature, uint combatId)
    {
        try
        {
            MegaCrit.Sts2.Core.Nodes.Combat.NCreature? zombie = null;

            if (AnimDiePatch.DetachedZombies.TryGetValue(creature, out var detached)
                && detached != null && GodotObject.IsInstanceValid(detached))
            {
                zombie = detached;
                UndoLogger.Info($"[Revive] id={combatId} in-place: zombie found in DetachedZombies registry");
            }

            if (zombie == null && ReflectionCache.NcrRemovingNodesField?.GetValue(room)
                is System.Collections.IList removingList)
            {
                foreach (var item in removingList)
                {
                    if (item is not MegaCrit.Sts2.Core.Nodes.Combat.NCreature nc) continue;
                    var ent = ReflectionCache.NCreatureEntityProp?.GetValue(nc) as Creature;
                    if (ReferenceEquals(ent, creature)) { zombie = nc; break; }
                }
            }

            if (zombie == null)
            {
                foreach (var nc in EnumerateAllNCreatureNodes(room))
                {
                    var ent = ReflectionCache.NCreatureEntityProp?.GetValue(nc) as Creature;
                    if (ReferenceEquals(ent, creature)) { zombie = nc; break; }
                }
            }

            if (zombie == null)
            {
                UndoLogger.Info($"[Revive] id={combatId} in-place: no zombie found");
                return false;
            }

            if (ReflectionCache.NcrRemovingNodesField?.GetValue(room) is System.Collections.IList removingMutable)
                while (removingMutable.Contains(zombie)) removingMutable.Remove(zombie);

            var activeField = AccessTools.Field(typeof(NCombatRoom), "_creatureNodes");
            if (activeField?.GetValue(room) is System.Collections.IList activeList)
                if (!activeList.Contains(zombie)) activeList.Add(zombie);

            try
            {
                if (zombie.GetParent() == null)
                {
                    var containerField = creature.Side == CombatSide.Enemy
                        ? AccessTools.Field(typeof(NCombatRoom), "_enemyContainer")
                        : AccessTools.Field(typeof(NCombatRoom), "_allyContainer");
                    if (containerField?.GetValue(room) is Node container)
                        container.AddChild(zombie);
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[Revive] in-place reparent failed: {ex.Message}"); }

            CancelDeathAnimAndKillTweens(zombie, combatId);

            try
            {
                if (AnimDiePatch.ActiveFadeTweens.TryGetValue(zombie, out var fadeTw))
                {
                    if (fadeTw != null && GodotObject.IsInstanceValid(fadeTw) && fadeTw.IsValid())
                        fadeTw.Kill();
                    AnimDiePatch.ActiveFadeTweens.Remove(zombie);
                }

                var m = zombie.Modulate;
                zombie.Modulate = new Color(m.R, m.G, m.B, 1f);

                try
                {
                    if (zombie.Body is CanvasItem bodyCi)
                    {
                        var bm = bodyCi.Modulate;
                        bodyCi.Modulate = new Color(bm.R, bm.G, bm.B, 1f);
                    }
                }
                catch { }
            }
            catch (Exception ex) { UndoLogger.Warn($"[Revive] fade-tween kill: {ex.Message}"); }

            ResetReticleCancelTokens(zombie, combatId);

            try { zombie.StartReviveAnim(); }
            catch (Exception ex) { UndoLogger.Warn($"[Revive] in-place StartReviveAnim failed: {ex.Message}"); }

            try { AnimDiePatch.DetachedZombies.Remove(creature); } catch { }

            return true;
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[Revive] id={combatId} in-place failed: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
    }

    private static void TryReattachDeathDetachedNodes(
        MegaCrit.Sts2.Core.Nodes.Combat.NCreature node, uint combatId)
    {
        try
        {
            if (node.Visuals is not Node visualsNode) return;

            try
            {
                var body = node.Body;
                if (body != null && GodotObject.IsInstanceValid(body) && body.GetParent() == null)
                    visualsNode.AddChild(body);
            }
            catch (Exception ex) { UndoLogger.Warn($"[Reattach] id={combatId} body re-attach: {ex.Message}"); }

            try
            {
                var visualsType = ReflectionCache.NCreatureVisualsType;
                if (visualsType != null)
                {
                    var spineBodyProp = AccessTools.Property(visualsType, "SpineBody");
                    var spineBody = spineBodyProp?.GetValue(node.Visuals);
                    if (spineBody is Node spineN && GodotObject.IsInstanceValid(spineN))
                    {
                        if (spineN.GetParent() == null)
                        {
                            var body = node.Body;
                            if (body != null) body.AddChild(spineN);
                            else visualsNode.AddChild(spineN);
                        }
                        if (spineBody is CanvasItem spineCi) spineCi.Visible = true;
                    }
                }
            }
            catch (Exception ex) { UndoLogger.Warn($"[Reattach] id={combatId} SpineBody: {ex.Message}"); }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Reattach] id={combatId} failed: {ex.GetType().Name}:{ex.Message}"); }
    }

    private static void TryReparentVisualsUnderCreature(
        MegaCrit.Sts2.Core.Nodes.Combat.NCreature node, uint combatId)
    {
        try
        {
            if (node.Visuals is not Node visualsNode) return;
            if (!GodotObject.IsInstanceValid(visualsNode)) return;

            Node? currentParent = null;
            try { currentParent = visualsNode.GetParent(); } catch { }
            if (ReferenceEquals(currentParent, node)) return;

            Vector2 globalPosBefore = Vector2.Zero;
            float globalRotBefore = 0f;
            Vector2 globalScaleBefore = Vector2.One;
            if (visualsNode is Node2D vn2dBefore)
            {
                try { globalPosBefore = vn2dBefore.GlobalPosition; } catch { }
                try { globalRotBefore = vn2dBefore.GlobalRotation; } catch { }
                try { globalScaleBefore = vn2dBefore.GlobalScale; } catch { }
            }

            try { currentParent?.RemoveChild(visualsNode); }
            catch (Exception ex) { UndoLogger.Warn($"[Reparent] id={combatId} RemoveChild failed: {ex.Message}"); }

            try { node.AddChild(visualsNode); }
            catch (Exception ex) { UndoLogger.Warn($"[Reparent] id={combatId} AddChild failed: {ex.Message}"); return; }

            if (visualsNode is Node2D vn2dAfter)
            {
                try { vn2dAfter.GlobalPosition = globalPosBefore; } catch { }
                try { vn2dAfter.GlobalRotation = globalRotBefore; } catch { }
                try { vn2dAfter.GlobalScale = globalScaleBefore; } catch { }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Reparent] id={combatId} failed: {ex.GetType().Name}:{ex.Message}"); }
    }

    private static void CancelDeathAnimAndKillTweens(
        MegaCrit.Sts2.Core.Nodes.Combat.NCreature zombie, uint combatId)
    {
        try
        {
            if (ReflectionCache.NCreatureDeathAnimCancelTokenProp?.GetValue(zombie) is CancellationTokenSource cts)
            {
                bool alreadyCancelled = false;
                try { alreadyCancelled = cts.IsCancellationRequested; } catch { }
                if (!alreadyCancelled)
                    try { cts.Cancel(); } catch (Exception ex) { UndoLogger.Warn($"[Revive] id={combatId} cts.Cancel: {ex.Message}"); }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Revive] id={combatId} death cancel failed: {ex.Message}"); }

        TryKillTween(zombie, ReflectionCache.NCreatureIntentFadeTweenField, "_intentFadeTween", combatId);
        TryKillTween(zombie, ReflectionCache.NCreatureShakeTweenField, "_shakeTween", combatId);
        TryKillTween(zombie, ReflectionCache.NCreatureScaleTweenField, "_scaleTween", combatId);
    }

    private static void TryKillTween(object owner, FieldInfo? field, string label, uint combatId)
    {
        if (field == null) return;
        try
        {
            if (field.GetValue(owner) is Tween t && t.IsValid())
                t.Kill();
        }
        catch (Exception ex) { UndoLogger.Warn($"[Revive] id={combatId} kill {label}: {ex.Message}"); }
    }

    internal static bool IsZombieDegraded(MegaCrit.Sts2.Core.Nodes.Combat.NCreature node)
    {
        try
        {
            object? visuals = null;
            try { visuals = node.Visuals; } catch { return true; }
            if (visuals == null) return true;

            var visualsType = ReflectionCache.NCreatureVisualsType;
            if (visualsType != null)
            {
                var bodyField = AccessTools.Field(visualsType, "_body");
                if (bodyField != null)
                {
                    if (bodyField.GetValue(visuals) is not Node body || !GodotObject.IsInstanceValid(body)) return true;
                }

                var spineBodyProp = AccessTools.Property(visualsType, "SpineBody");
                if (spineBodyProp != null)
                {
                    object? spineBody;
                    try { spineBody = spineBodyProp.GetValue(visuals); } catch { return true; }
                    if (spineBody == null) return true;
                    if (spineBody is GodotObject go && !GodotObject.IsInstanceValid(go)) return true;
                }
            }
        }
        catch { return true; }
        return false;
    }

    internal static IEnumerable<Node> WalkNodeTree(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            try { foreach (var c in n.GetChildren()) stack.Push(c); } catch { }
        }
    }

    internal static void DestroyZombieNCreatures(NCombatRoom room, Creature creature, uint combatId)
    {
        var zombies = new List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature>();

        try { if (room.GetCreatureNode(creature) is { } active) zombies.Add(active); } catch { }

        try
        {
            if (ReflectionCache.NcrRemovingNodesField?.GetValue(room) is System.Collections.IEnumerable removing)
            {
                foreach (var item in removing)
                {
                    if (item is not MegaCrit.Sts2.Core.Nodes.Combat.NCreature nc) continue;
                    try
                    {
                        if (ReflectionCache.NCreatureEntityProp?.GetValue(nc) is Creature ent
                            && ReferenceEquals(ent, creature)
                            && !zombies.Contains(nc))
                            zombies.Add(nc);
                    }
                    catch { }
                }
            }
        }
        catch { }

        try
        {
            foreach (var child in EnumerateAllNCreatureNodes(room))
            {
                if (zombies.Contains(child)) continue;
                if (ReflectionCache.NCreatureEntityProp?.GetValue(child) is Creature ent
                    && ReferenceEquals(ent, creature))
                    zombies.Add(child);
            }
        }
        catch { }

        UndoLogger.Info($"[Revive] id={combatId} found {zombies.Count} pre-existing NCreature reference(s) before re-add");

        foreach (var z in zombies)
        {
            try { room.RemoveCreatureNode(z); } catch { }
            try { z.GetParent()?.RemoveChild(z); } catch { }
            try
            {
                if (ReflectionCache.NcrRemovingNodesField?.GetValue(room) is System.Collections.IList removingList)
                    while (removingList.Contains(z)) removingList.Remove(z);
            }
            catch { }
            try { z.Free(); }
            catch { try { z.QueueFree(); } catch { } }
        }

        EvictCreatureFromAllRoomCaches(room, creature, zombies, combatId);
    }

    private static IEnumerable<MegaCrit.Sts2.Core.Nodes.Combat.NCreature> EnumerateAllNCreatureNodes(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MegaCrit.Sts2.Core.Nodes.Combat.NCreature nc) yield return nc;
            try { foreach (var c in n.GetChildren()) stack.Push(c); } catch { }
        }
    }

    private static void EvictCreatureFromAllRoomCaches(
        NCombatRoom room, Creature creature,
        List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature> zombies, uint combatId)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic
                              | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        int evicted = 0;
        var zombieSet = new HashSet<object>(zombies);

        bool ShouldEvict(object? key, object? value)
        {
            if (key != null && (ReferenceEquals(key, creature) || zombieSet.Contains(key))) return true;
            if (value != null && (ReferenceEquals(value, creature) || zombieSet.Contains(value))) return true;
            return false;
        }

        try
        {
            for (var ct = room.GetType(); ct != null && ct != typeof(object); ct = ct.BaseType)
            {
                foreach (var f in ct.GetFields(F))
                {
                    object? raw;
                    try { raw = f.GetValue(room); } catch { continue; }
                    if (raw == null) continue;

                    if (raw is System.Collections.IDictionary dict)
                    {
                        var keysToRemove = new List<object>();
                        try { foreach (System.Collections.DictionaryEntry e in dict) if (ShouldEvict(e.Key, e.Value)) keysToRemove.Add(e.Key); } catch { }
                        foreach (var k in keysToRemove)
                            try { dict.Remove(k); evicted++; } catch { }
                    }
                    else if (raw is System.Collections.IList list && raw is not Array)
                    {
                        try
                        {
                            for (int i = list.Count - 1; i >= 0; i--)
                                if (ShouldEvict(null, list[i]))
                                    try { list.RemoveAt(i); evicted++; } catch { }
                        }
                        catch { }
                    }
                    else if (raw is MegaCrit.Sts2.Core.Nodes.Combat.NCreature directNc && zombieSet.Contains(directNc))
                        try { f.SetValue(room, null); evicted++; } catch { }
                    else if (raw is Creature directCreature && ReferenceEquals(directCreature, creature))
                        try { f.SetValue(room, null); evicted++; } catch { }
                }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Revive] cache eviction failed: {ex.Message}"); }

        UndoLogger.Info($"[Revive] id={combatId} cache eviction total={evicted}");
    }

    private static void SetCombatStateOnCreature(Creature creature, CombatState cs)
    {
        var prop = AccessTools.Property(typeof(Creature), "CombatState");
        if (prop?.CanWrite == true)
        {
            try { prop.SetValue(creature, cs); return; } catch { }
        }
        var f = AccessTools.Field(typeof(Creature), "<CombatState>k__BackingField")
            ?? AccessTools.Field(typeof(Creature), "_combatState");
        f?.SetValue(creature, cs);
    }

    private static int RemoveCreaturesNotIn(HashSet<uint> snapIds, System.Collections.IList list)
    {
        int removed = 0;
        var room = NCombatRoom.Instance;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not Creature c) continue;
            if (!c.CombatId.HasValue) continue;
            if (snapIds.Contains(c.CombatId.Value)) continue;

            UndoLogger.Info($"[Roster] remove summoned id={c.CombatId.Value}");
            if (room != null)
            {
                var node = room.GetCreatureNode(c);
                if (node != null)
                {
                    try { node.Visible = false; room.RemoveCreatureNode(node); node.QueueFree(); }
                    catch (Exception ex) { UndoLogger.Warn($"[Roster] visual removal failed: {ex.Message}"); }
                }
            }
            list.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    private static void RestoreCreatures(CombatSnapshot snap)
    {
        foreach (var saved in snap.Creatures)
        {
            var live = saved.Ref;
            if (live == null) continue;

            int oldHp = (int)(ReflectionCache.CreatureHpField.GetValue(live) ?? 0);
            int oldMaxHp = (int)(ReflectionCache.CreatureMaxHpField.GetValue(live) ?? 0);
            int oldBlock = (int)(ReflectionCache.CreatureBlockField.GetValue(live) ?? 0);

            ReflectionCache.CreatureHpField.SetValue(live, saved.CurrentHp);
            ReflectionCache.CreatureMaxHpField.SetValue(live, saved.MaxHp);
            ReflectionCache.CreatureBlockField.SetValue(live, saved.Block);

            ResetIsDeadIfPresent(live, saved.IsDead);
            live.ShowsInfiniteHp = saved.ShowsInfiniteHp;

            FireDelegateField(live, ReflectionCache.CreatureCurrentHpChangedField, oldHp, saved.CurrentHp);
            FireDelegateField(live, ReflectionCache.CreatureMaxHpChangedField, oldMaxHp, saved.MaxHp);
            FireDelegateField(live, ReflectionCache.CreatureBlockChangedField, oldBlock, saved.Block);

            RestoreCreaturePowers(live, saved);

            if (live.Monster != null && saved.MonsterRng.HasValue)
            {
                var (seed, counter) = saved.MonsterRng.Value;
                ReflectionCache.MonsterRngField?.SetValue(live.Monster, new Rng(seed, counter));
            }
            if (live.Monster != null && saved.MonsterMove.HasValue)
                RestoreMonsterMove(live.Monster, saved.MonsterMove.Value);
            if (live.Monster != null && saved.MonsterFields != null)
                RestoreMonsterFields(live.Monster, saved.MonsterFields);
        }
    }

    private static void RestoreMonsterFields(MonsterModel monster, Dictionary<string, object?> snap)
    {
        int set = 0;
        try
        {
            for (var t = monster.GetType(); t != null && t != typeof(object) && t != typeof(MonsterModel); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                              | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue;
                    var ft = f.FieldType;
                    if (!(ft.IsPrimitive || ft.IsEnum)) continue;
                    var key = (t.FullName ?? t.Name) + "::" + f.Name;
                    if (!snap.TryGetValue(key, out var val)) continue;
                    try { f.SetValue(monster, val); set++; } catch { }
                }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Monster] field restore: {ex.Message}"); }
    }

    private static void RestoreMonsterMove(MonsterModel monster, MonsterMoveSnapshot saved)
    {
        var sm = monster.MoveStateMachine;
        if (sm == null) return;

        ReflectionCache.SmPerformedFirstMoveField?.SetValue(sm, saved.PerformedFirstMove);
        ReflectionCache.MonsterSpawnedField?.SetValue(monster, saved.SpawnedThisTurn);

        if (ReflectionCache.SmStatesProp?.GetValue(sm) is not System.Collections.IDictionary states) return;

        object? current = null;
        if (saved.CurrentStateId != null && states.Contains(saved.CurrentStateId))
            current = states[saved.CurrentStateId];
        else if (saved.CurrentStateRef != null
                 && ReflectionCache.MonsterStateType?.IsInstanceOfType(saved.CurrentStateRef) == true)
            current = saved.CurrentStateRef;
        if (current != null)
            try { ReflectionCache.SmForceCurrentStateMethod?.Invoke(sm, [current]); }
            catch (Exception ex) { UndoLogger.Warn($"[Monster] ForceCurrentState failed: {ex.Message}"); }

        if (ReflectionCache.SmStateLogProp?.GetValue(sm) is System.Collections.IList stateLog
            && saved.StateLogIds != null)
        {
            stateLog.Clear();
            foreach (var id in saved.StateLogIds)
                if (states.Contains(id)) stateLog.Add(states[id]!);
        }

        if (ReflectionCache.MoveStatePerformedField != null && saved.MovePerformedAtLeastOnce != null)
        {
            foreach (System.Collections.DictionaryEntry e in states)
            {
                if (e.Key is string key
                    && saved.MovePerformedAtLeastOnce.TryGetValue(key, out var p)
                    && e.Value != null)
                    try { ReflectionCache.MoveStatePerformedField.SetValue(e.Value, p); } catch { }
            }
        }

        object? nextState = null;
        if (saved.NextMoveStateId != null && states.Contains(saved.NextMoveStateId))
            nextState = states[saved.NextMoveStateId];
        else if (saved.NextMoveRef != null
                 && ReflectionCache.MonsterStateType?.IsInstanceOfType(saved.NextMoveRef) == true)
            nextState = saved.NextMoveRef;
        if (nextState != null && ReflectionCache.MonsterStateType?.IsInstanceOfType(nextState) == true)
            try { ReflectionCache.NextMoveProp?.SetValue(monster, nextState); }
            catch (Exception ex) { UndoLogger.Warn($"[Monster] NextMove set failed: {ex.Message}"); }
    }

    private static void FireDelegateField(object target, FieldInfo? field, params object?[] args)
    {
        if (field == null) return;
        try { if (field.GetValue(target) is Delegate d) d.DynamicInvoke(args); }
        catch (Exception ex) { UndoLogger.Warn($"[Restore] event fire ({field.Name}) failed: {ex.Message}"); }
    }

    private static void ResetIsDeadIfPresent(Creature live, bool wasDeadInSnapshot)
    {
        foreach (var name in new[] { "<IsDead>k__BackingField", "_isDead", "_dead", "_isDying" })
        {
            var f = AccessTools.Field(typeof(Creature), name);
            if (f != null) { try { f.SetValue(live, wasDeadInSnapshot); } catch { } }
        }
    }

    private static void RestoreSyncState(CombatSnapshot snap)
    {
        var rm = RunManager.Instance;
        if (rm == null) return;

        var syncr = rm.ActionQueueSynchronizer;
        if (syncr != null)
        {
            var value = MegaCrit.Sts2.Core.Entities.Multiplayer.ActionSynchronizerCombatState.PlayPhase;
            var prop = AccessTools.Property(syncr.GetType(), "CombatState");
            if (prop?.CanWrite == true)
                try { prop.SetValue(syncr, value); } catch { }
            else
            {
                var f = AccessTools.Field(syncr.GetType(), "<CombatState>k__BackingField")
                    ?? AccessTools.Field(syncr.GetType(), "_combatState");
                f?.SetValue(syncr, value);
            }
        }

        var cm = CombatManager.Instance;
        if (cm != null)
        {
            var pausedProp = AccessTools.Property(typeof(CombatManager), "IsPaused");
            if (pausedProp?.CanWrite == true)
                try { pausedProp.SetValue(cm, false); } catch { }
        }

        var aqSet = rm.ActionQueueSet;
        if (aqSet != null)
        {
            var queuesField = AccessTools.Field(aqSet.GetType(), "_actionQueues");
            if (queuesField?.GetValue(aqSet) is System.Collections.IEnumerable queues)
            {
                foreach (var q in queues)
                {
                    if (q == null) continue;
                    var pausedField = AccessTools.Field(q.GetType(), "isPaused");
                    try { pausedField?.SetValue(q, false); } catch { }
                }
            }
        }

        var executor = rm.ActionExecutor;
        if (executor != null)
        {
            var prop = AccessTools.Property(executor.GetType(), "IsPaused");
            if (prop?.CanWrite == true)
                try { prop.SetValue(executor, false); } catch { }
            else
            {
                var f = AccessTools.Field(executor.GetType(), "<IsPaused>k__BackingField")
                    ?? AccessTools.Field(executor.GetType(), "_isPaused");
                try { f?.SetValue(executor, false); } catch { }
            }
        }
    }

    private static void RestoreCreaturePowers(Creature creature, CreatureSnapshot saved)
    {
        if (ReflectionCache.CreaturePowersField.GetValue(creature) is not System.Collections.IList liveList) return;

        var liveSet = new HashSet<PowerModel>();
        foreach (var item in liveList)
            if (item is PowerModel pm) liveSet.Add(pm);

        liveList.Clear();
        int reattached = 0;
        var consumed = new HashSet<PowerModel>();

        foreach (var snapPower in saved.Powers)
        {
            PowerModel? live = null;

            if (snapPower.Ref != null && liveSet.Contains(snapPower.Ref) && !consumed.Contains(snapPower.Ref))
                live = snapPower.Ref;

            if (live == null && snapPower.Ref != null && !consumed.Contains(snapPower.Ref))
            {
                try
                {
                    ReflectionCache.PowerOwnerField?.SetValue(snapPower.Ref, creature);
                    live = snapPower.Ref;
                    reattached++;
                }
                catch (Exception ex) { UndoLogger.Warn($"[Powers] reattach owner failed for {snapPower.Id.Entry}: {ex.Message}"); }
            }
            if (live == null) continue;

            consumed.Add(live);
            ReflectionCache.PowerAmountField.SetValue(live, snapPower.Amount);
            ReflectionCache.PowerAmountOnTurnStartField.SetValue(live, snapPower.AmountOnTurnStart);
            ReflectionCache.PowerSkipField.SetValue(live, snapPower.SkipNextDurationTick);

            ReflectionCache.PowerInternalDataField?.SetValue(live, DeepCloner.CloneObject(snapPower.InternalDataClone));

            if (snapPower.Clone != null && live.GetType() == snapPower.Clone.GetType())
            {
                foreach (var f in GetPowerCopyFields(live.GetType()))
                    try { f.SetValue(live, f.GetValue(snapPower.Clone)); } catch { }
            }
            liveList.Add(live);
        }

        if (reattached > 0)
            UndoLogger.Info($"[Powers] hook lifecycle: reattached={reattached}");

        foreach (var item in liveList)
            if (item is PowerModel pm)
                ReflectionCache.PowerInvokeAmountChangedMethod?.Invoke(pm, null);
    }

    private static void RestoreRelics(CombatSnapshot snap)
    {
        foreach (var rs in snap.Relics)
        {
            var live = rs.Ref;
            if (live == null) continue;

            ReflectionCache.RelicStackCountField?.SetValue(live, rs.StackCount);
            if (ReflectionCache.RelicStatusProperty?.CanWrite == true && rs.Status != null)
                ReflectionCache.RelicStatusProperty.SetValue(live, rs.Status);

            if (rs.Clone != null && live.GetType() == rs.Clone.GetType())
            {
                foreach (var f in GetRelicCopyFields(live.GetType()))
                    try { f.SetValue(live, f.GetValue(rs.Clone)); } catch { }
            }
            else if (rs.DynamicVarsClone != null && ReflectionCache.RelicDynamicVarsField != null)
                ReflectionCache.RelicDynamicVarsField.SetValue(live, DeepCloner.CloneObject(rs.DynamicVarsClone));
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> _relicCopyFieldCache = new();

    private static FieldInfo[] GetRelicCopyFields(Type type)
        => _relicCopyFieldCache.GetOrAdd(type, BuildRelicCopyFields);

    private static FieldInfo[] BuildRelicCopyFields(Type type)
    {
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (f.Name is "_canonicalInstance" or "_owner") continue;
                if (f.Name is "<Id>k__BackingField" or "<IsMutable>k__BackingField"
                    or "<Category>k__BackingField" or "<Entry>k__BackingField") continue;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                list.Add(f);
            }
        }
        return [.. list];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> _powerCopyFieldCache = new();

    private static FieldInfo[] GetPowerCopyFields(Type type)
        => _powerCopyFieldCache.GetOrAdd(type, BuildPowerCopyFields);

    private static FieldInfo[] BuildPowerCopyFields(Type type)
    {
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (f.Name is "_canonicalInstance" or "_owner" or "_internalData") continue;
                if (f.Name is "<Id>k__BackingField" or "<IsMutable>k__BackingField"
                    or "<Category>k__BackingField" or "<Entry>k__BackingField") continue;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                list.Add(f);
            }
        }
        return [.. list];
    }

    private static void RestoreOrbs(CombatSnapshot snap, CombatState cs)
    {
        if (!snap.HasOrbData) return;
        foreach (var ally in cs.Allies)
        {
            var player = ally.Player;
            if (player == null) continue;
            var pcs = player.PlayerCombatState;
            if (pcs == null) continue;
            var orbQueue = pcs.OrbQueue;
            if (orbQueue == null) return;

            if (ReflectionCache.OrbQueueOrbsField?.GetValue(orbQueue) is System.Collections.IList orbsList)
            {
                orbsList.Clear();
                foreach (var orb in snap.OrbRefs)
                {
                    if (snap.OrbClones.TryGetValue(orb, out var clone))
                        CopyOrbMutableFields(clone, orb);
                    orbsList.Add(orb);
                }
            }
            ReflectionCache.OrbQueueCapacityField?.SetValue(orbQueue, snap.OrbCapacity);
            return;
        }
    }

    private static void CopyOrbMutableFields(OrbModel from, OrbModel to)
    {
        var skip = new HashSet<string>
        {
            "_canonicalInstance", "_owner",
            "<Id>k__BackingField", "<IsMutable>k__BackingField",
            "<CategorySortingId>k__BackingField", "<EntrySortingId>k__BackingField",
            "_dynamicVars",
        };
        for (var t = from.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (skip.Contains(f.Name)) continue;
                try { f.SetValue(to, f.GetValue(from)); } catch { }
            }
        }
    }

    private static void RestorePotions(CombatSnapshot snap, CombatState cs)
    {
        foreach (var ally in cs.Allies)
        {
            var player = ally.Player;
            if (player == null) continue;

            var slotsObj = ReflectionCache.PlayerPotionSlotsField.GetValue(player);
            if (slotsObj == null) return;

            if (slotsObj is Array arr)
            {
                int n = Math.Min(arr.Length, snap.PotionSlotRefs.Count);
                for (int i = 0; i < n; i++)
                {
                    var savedRef = snap.PotionSlotRefs[i];
                    arr.SetValue(savedRef, i);
                    if (savedRef != null && snap.PotionClones.TryGetValue(savedRef, out var clone))
                        CopyPotionMutableFields(clone, savedRef, player);
                }
            }
            else if (slotsObj is System.Collections.IList list)
            {
                int n = Math.Min(list.Count, snap.PotionSlotRefs.Count);
                for (int i = 0; i < n; i++)
                {
                    var savedRef = snap.PotionSlotRefs[i];
                    list[i] = savedRef;
                    if (savedRef != null && snap.PotionClones.TryGetValue(savedRef, out var clone))
                        CopyPotionMutableFields(clone, savedRef, player);
                }
            }
            else
            {
                UndoLogger.Warn($"[Potions] _potionSlots is unexpected type {slotsObj.GetType().FullName}");
                return;
            }
            return;
        }
    }

    private static void CopyPotionMutableFields(PotionModel from, PotionModel to, Player owner)
    {
        var skip = new HashSet<string>
        {
            "_canonicalInstance", "_owner",
            "<Id>k__BackingField", "<IsMutable>k__BackingField",
            "<CategorySortingId>k__BackingField", "<EntrySortingId>k__BackingField",
            "_dynamicVars",
        };
        for (var t = typeof(PotionModel); t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (skip.Contains(f.Name)) continue;
                try { f.SetValue(to, f.GetValue(from)); } catch { }
            }
        }
        ReflectionCache.PotionOwnerField?.SetValue(to, owner);
    }

    private static void RestoreRunRng(CombatSnapshot snap, RunState runState)
    {
        var rngSet = runState.Rng;
        if (rngSet == null) return;
        if (ReflectionCache.RunRngDictField.GetValue(rngSet) is not Dictionary<RunRngType, Rng> dict) return;
        foreach (var (key, (seed, counter)) in snap.RunRngs)
            dict[key] = new Rng(seed, counter);
    }

    private static void RestoreHistory(CombatSnapshot snap, CombatManager cm)
    {
        if (snap.HistoryEntries == null) return;
        var history = ReflectionCache.CmHistoryProperty?.GetValue(cm);
        if (history == null) return;
        if (ReflectionCache.HistoryEntriesField?.GetValue(history) is not System.Collections.IList live) return;
        live.Clear();
        foreach (var e in snap.HistoryEntries) live.Add(e);
    }

    private static void TrySetProperty(object target, string name, object value)
    {
        var prop = AccessTools.Property(target.GetType(), name);
        if (prop?.CanWrite == true) { prop.SetValue(target, value); return; }
        var field = AccessTools.Field(target.GetType(), $"<{name}>k__BackingField");
        field?.SetValue(target, value);
    }
}