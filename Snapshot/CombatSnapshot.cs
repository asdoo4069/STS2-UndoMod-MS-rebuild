using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace UndoModMS.Snapshot;

internal sealed class CombatSnapshot
{
    public static readonly Dictionary<Creature, string> IdleAnimCache =
        new(ReferenceEqualityComparer.Instance);

    public bool IsTurnBoundary { get; init; }
    public DateTime CapturedAt { get; } = DateTime.UtcNow;

    public int RoundNumber;
    public CombatSide CurrentSide;

    public int Energy;
    public int Stars;
    public int Gold;

    public Dictionary<PileType, List<CardModel>> PileRefs = new();
    public Dictionary<CardModel, CardModel> CardMutableClones = new(ReferenceEqualityComparer.Instance);
    public List<CardModel> AllCardRefs = new();

    public uint NextCreatureId;
    public List<CreatureSnapshot> Creatures = new();
    public List<uint> PetCombatIds = new();
    public List<Creature> EscapedCreatures = new();

    public List<RelicSnapshot> Relics = new();

    public List<PotionModel?> PotionSlotRefs = new();
    public Dictionary<PotionModel, PotionModel> PotionClones = new(ReferenceEqualityComparer.Instance);

    public bool HasOrbData;
    public int OrbCapacity;
    public List<OrbModel> OrbRefs = new();
    public Dictionary<OrbModel, OrbModel> OrbClones = new(ReferenceEqualityComparer.Instance);

    public Dictionary<RunRngType, (uint seed, int counter)> RunRngs = new();

    public List<object>? HistoryEntries;

    public ActionSynchronizerCombatState SyncCombatState;
    public bool CombatManagerPaused;

    public static CombatSnapshot? Capture(bool isTurnBoundary = false)
    {
        var cm = CombatManager.Instance;
        if (cm == null) return null;
        var cs = ReflectionCache.CombatManagerStateField.GetValue(cm) as CombatState;
        if (cs == null) return null;

        var runState = ReflectionCache.RunManagerStateProperty?.GetValue(RunManager.Instance) as RunState;
        if (runState == null) return null;

        var snap = new CombatSnapshot { IsTurnBoundary = isTurnBoundary };

        try
        {
            CaptureCombatLevel(snap, cs);
            CapturePlayerAndPiles(snap, cs);
            CaptureCreatures(snap, cs);
            CaptureRunRng(snap, runState);
            CaptureHistory(snap, cm);
            CaptureSyncState(snap);
        }
        catch (Exception ex)
        {
            UndoLogger.Warn($"[Snapshot] capture failed: {ex.Message}");
            return null;
        }

        return snap;
    }

    private static void CaptureCombatLevel(CombatSnapshot snap, CombatState cs)
    {
        snap.RoundNumber = cs.RoundNumber;
        snap.CurrentSide = cs.CurrentSide;
        snap.NextCreatureId = (uint?)ReflectionCache.NextCreatureIdField?.GetValue(cs) ?? 0u;

        if (ReflectionCache.AllCardsField?.GetValue(cs) is List<CardModel> all)
            snap.AllCardRefs.AddRange(all);

        try { snap.EscapedCreatures.AddRange(cs.EscapedCreatures); } catch { }
    }

    private static void CapturePlayerAndPiles(CombatSnapshot snap, CombatState cs)
    {
        foreach (var ally in cs.Allies)
        {
            var player = ally.Player;
            if (player == null) continue;

            var pcs = player.PlayerCombatState;
            if (pcs != null)
            {
                snap.Energy = (int)(ReflectionCache.PcsEnergyField.GetValue(pcs) ?? 0);
                snap.Stars = (int)(ReflectionCache.PcsStarsField.GetValue(pcs) ?? 0);

                foreach (var pile in pcs.AllPiles)
                    snap.PileRefs[pile.Type] = pile.Cards.ToList();

                foreach (var card in pcs.AllCards)
                {
                    if (!snap.CardMutableClones.ContainsKey(card))
                        snap.CardMutableClones[card] = (CardModel)card.MutableClone();
                }

                if (ReflectionCache.PcsPetsField?.GetValue(pcs) is System.Collections.IEnumerable pets)
                    foreach (var p in pets)
                        if (p is Creature c && c.CombatId.HasValue)
                            snap.PetCombatIds.Add(c.CombatId.Value);

                CaptureOrbs(snap, pcs);
            }

            foreach (var relic in player.Relics)
                snap.Relics.Add(CaptureRelic(relic));

            for (int i = 0; i < player.PotionSlots.Count; i++)
            {
                var slot = player.PotionSlots[i];
                snap.PotionSlotRefs.Add(slot);
                if (slot != null && !snap.PotionClones.ContainsKey(slot))
                    snap.PotionClones[slot] = (PotionModel)slot.MutableClone();
            }

            snap.Gold = (int)(ReflectionCache.PlayerGoldField?.GetValue(player) ?? 0);

            break;
        }
    }

    private static void CaptureOrbs(CombatSnapshot snap, PlayerCombatState pcs)
    {
        var orbQueue = pcs.OrbQueue;
        if (orbQueue == null) return;

        snap.HasOrbData = true;
        snap.OrbCapacity = orbQueue.Capacity;
        foreach (var orb in orbQueue.Orbs)
        {
            snap.OrbRefs.Add(orb);
            if (!snap.OrbClones.ContainsKey(orb))
                snap.OrbClones[orb] = (OrbModel)orb.MutableClone();
        }
    }

    private static RelicSnapshot CaptureRelic(RelicModel rm)
    {
        RelicModel? clone = null;
        try { clone = (RelicModel)rm.MutableClone(); }
        catch (Exception ex) { UndoLogger.Warn($"[Snapshot] relic MutableClone failed: {ex.Message}"); }

        return new()
        {
            Ref = rm,
            Id = rm.Id,
            StackCount = (int)(ReflectionCache.RelicStackCountField?.GetValue(rm) ?? 0),
            Status = ReflectionCache.RelicStatusProperty?.GetValue(rm),
            DynamicVarsClone = clone == null
                ? DeepCloner.CloneObject(ReflectionCache.RelicDynamicVarsField?.GetValue(rm))
                : null,
            Clone = clone,
        };
    }

    private static void CaptureCreatures(CombatSnapshot snap, CombatState cs)
    {
        foreach (var c in cs.Creatures)
            snap.Creatures.Add(CaptureCreature(c));
    }

    private static CreatureSnapshot CaptureCreature(Creature c)
    {
        var snap = new CreatureSnapshot
        {
            Ref = c,
            CombatId = c.CombatId ?? 0u,
            CurrentHp = (int)(ReflectionCache.CreatureHpField.GetValue(c) ?? 0),
            MaxHp = (int)(ReflectionCache.CreatureMaxHpField.GetValue(c) ?? 0),
            Block = (int)(ReflectionCache.CreatureBlockField.GetValue(c) ?? 0),
            IsDead = c.IsDead,
            HpDisplay = c.HpDisplay,
        };

        foreach (var pm in c.Powers)
        {
            PowerModel? pmClone = null;
            try { pmClone = (PowerModel)pm.MutableClone(); }
            catch (Exception ex) { UndoLogger.Warn($"[Snapshot] power MutableClone failed ({pm.Id.Entry}): {ex.Message}"); }

            snap.Powers.Add(new PowerSnapshot
            {
                Id = pm.Id,
                Amount = (int)(ReflectionCache.PowerAmountField.GetValue(pm) ?? 0),
                AmountOnTurnStart = (int)(ReflectionCache.PowerAmountOnTurnStartField.GetValue(pm) ?? 0),
                SkipNextDurationTick = (bool)(ReflectionCache.PowerSkipField.GetValue(pm) ?? false),
                InternalDataClone = DeepCloner.CloneObject(
                    ReflectionCache.PowerInternalDataField?.GetValue(pm)),
                Ref = pm,
                Clone = pmClone,
            });
        }

        if (c.Monster is { } monster)
        {
            if (ReflectionCache.MonsterRngField?.GetValue(monster) is Rng rng)
                snap.MonsterRng = (rng.Seed, rng.Counter);
            snap.MonsterMove = CaptureMonsterMove(monster);
            snap.MonsterFields = CaptureMonsterFields(monster);
        }

        var nCombatRoom = NCombatRoom.Instance;
        if (nCombatRoom != null)
        {
            var nc = nCombatRoom.GetCreatureNode(c);
            if (nc == null && ReflectionCache.NcrRemovingNodesField?.GetValue(nCombatRoom)
                is System.Collections.IEnumerable removing)
            {
                foreach (var item in removing)
                {
                    if (item is NCreature ncreature
                        && ReflectionCache.NCreatureEntityProp?.GetValue(ncreature) is Creature ent
                        && ReferenceEquals(ent, c))
                    {
                        nc = ncreature;
                        break;
                    }
                }
            }
            if (nc != null)
            {
                snap.HadVisualNode = true;
                try { snap.VisualPosition = nc.GlobalPosition; } catch { }
                try { snap.VisualBodyScale = nc.Body?.Scale ?? Vector2.One; } catch { }
                try { snap.VisualBodyPosition = nc.Body?.Position ?? Vector2.Zero; } catch { }
                try { snap.VisualBodyRotation = nc.Body?.Rotation ?? 0f; } catch { }
                try
                {
                    if (nc.Body is CanvasItem bodyCi)
                        snap.VisualBodyModulate = bodyCi.Modulate;
                }
                catch { }

                try
                {
                    var visuals = nc.Visuals;
                    if (visuals != null)
                    {
                        if (ReflectionCache.NCVHueField?.GetValue(visuals) is float h)
                            snap.Hue = h;
                        snap.DefaultScale = visuals.DefaultScale;
                        if (ReflectionCache.NCVLiquidOverlayTimerField?.GetValue(visuals) is double t)
                            snap.LiquidOverlayTimer = t;

                        try
                        {
                            var spineBodyProp = AccessTools.Property(visuals.GetType(), "SpineBody");
                            var spineBody = spineBodyProp?.GetValue(visuals);
                            var current = spineBody != null
                                ? ReflectionCache.MegaSpriteGetNormalMaterialMethod?.Invoke(spineBody, null) as Material
                                : null;
                            var saved = ReflectionCache.NCVSavedNormalMaterialField?.GetValue(visuals) as Material;
                            var overlay = ReflectionCache.NCVCurrentLiquidOverlayMaterialField?.GetValue(visuals) as Material;
                            snap.LiquidOverlayWasActive = overlay != null;
                            snap.BodyNormalMaterial = snap.LiquidOverlayWasActive ? saved : current;
                        }
                        catch (Exception ex) { UndoLogger.Warn($"[Snapshot] body material capture: {ex.Message}"); }

                        var bodyField = AccessTools.Field(visuals.GetType(), "_body");
                        var body = bodyField?.GetValue(visuals) as Node;
                        if (body != null)
                        {
                            snap.BodyRef = body;
                            try { snap.BodyParentRef = body.GetParent(); } catch { }
                        }
                    }
                }
                catch { }

                if (ReflectionCache.NCreatureTempScaleField?.GetValue(nc) is float ts)
                    snap.TempScale = ts;

                bool deathAnimActive = Patches.DeathAnimDelayPatch.DeathAnimActive.Contains(c);
                if (deathAnimActive)
                {
                    snap.SpineAnimNameTrack0 = "die_loop";
                }
                else
                {
                    var observed = TryReadSpineAnim(nc);
                    if (observed != null && IsLoopShaped(observed))
                    {
                        snap.SpineAnimNameTrack0 = observed;
                        if (IsTrueIdleLoop(observed))
                            IdleAnimCache[c!] = observed;
                    }
                    else if (observed == null && IdleAnimCache.TryGetValue(c!, out var stable))
                    {
                        snap.SpineAnimNameTrack0 = stable;
                    }
                    else
                    {
                        snap.SpineAnimNameTrack0 = null;
                    }
                }

                // track 1~3: 있는 그대로 캡처 (null = 트랙 비어있었음)
                snap.SpineAnimNameTrack1 = TryReadSpineAnim(nc, 1);
                snap.SpineAnimNameTrack2 = TryReadSpineAnim(nc, 2);
                snap.SpineAnimNameTrack3 = TryReadSpineAnim(nc, 3);
            }
        }

        return snap;
    }

    private static bool IsLoopShaped(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.IndexOf("loop", StringComparison.Ordinal) >= 0
            || lower.IndexOf("idle", StringComparison.Ordinal) >= 0;
    }

    private static bool IsTrueIdleLoop(string name)
    {
        if (!IsLoopShaped(name)) return false;
        var lower = name.ToLowerInvariant();
        return !lower.Contains("stun") && !lower.Contains("knock") && !lower.Contains("freeze")
            && !lower.Contains("sleep") && !lower.Contains("daze") && !lower.Contains("die");
    }

    public static bool AnyCreatureMidTransient()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return false;
            var cs = ReflectionCache.CombatManagerStateField.GetValue(cm) as CombatState;
            if (cs == null) return false;

            foreach (var c in cs.Creatures)
            {
                if (c == null) continue;
                var room = NCombatRoom.Instance;
                var nc = room?.GetCreatureNode(c);
                if (nc == null) continue;
                var observed = TryReadSpineAnim(nc);
                if (observed != null && IsTransientName(observed, c.IsDead)) return true;
            }
        }
        catch { }
        return false;
    }

    private static readonly string[] TransientPatterns =
        { "attack", "cast", "hurt", "hit", "damage", "die", "death", "spawn" };

    private static bool IsTransientName(string name, bool isDead = true)
    {
        if (IsLoopShaped(name)) return false;

        foreach (var s in TransientPatterns)
        {
            if (name.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!isDead && (s == "die" || s == "death")) continue;
            return true;
        }
        return false;
    }

    public static void RefreshIdleCacheFromLiveCreatures()
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null) return;
            var cs = ReflectionCache.CombatManagerStateField.GetValue(cm) as CombatState;
            if (cs == null) return;

            foreach (var c in cs.Creatures)
            {
                if (c == null) continue;
                var room = NCombatRoom.Instance;
                var nc = room?.GetCreatureNode(c);
                if (nc == null) continue;
                var observed = TryReadSpineAnim(nc);
                if (observed != null && IsTrueIdleLoop(observed))
                    IdleAnimCache[c] = observed;
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Snapshot] cache refresh failed: {ex.Message}"); }
    }

    private static string? TryReadSpineAnim(NCreature nc) => TryReadSpineAnim(nc, 0);

    private static string? TryReadSpineAnim(NCreature nc, int trackIndex)
    {
        try
        {
            var visualsType = ReflectionCache.NCreatureVisualsType;
            if (visualsType == null) return null;
            object? visuals = null;
            foreach (var n in EnumerateTree(nc))
            {
                if (visualsType.IsInstanceOfType(n)) { visuals = n; break; }
            }
            if (visuals == null) return null;

            var spine = ReflectionCache.NCVSpineAnimationProp?.GetValue(visuals);
            if (spine == null) return null;

            var track = ReflectionCache.SpineGetCurrentTrackMethod?.Invoke(spine, new object[] { trackIndex });
            if (track == null) return null;

            var anim = ReflectionCache.TrackGetAnimationMethod?.Invoke(track, null);
            if (anim == null) return null;

            var getName = AccessTools.Method(anim.GetType(), "GetName");
            if (getName?.Invoke(anim, null) is string s && !string.IsNullOrEmpty(s)) return s;
            return null;
        }
        catch { return null; }
    }

    internal static IEnumerable<Node> EnumerateTree(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (string key, FieldInfo field)[]> _monsterFieldCache = new();

    private static Dictionary<string, object?>? CaptureMonsterFields(MonsterModel monster)
    {
        var dict = new Dictionary<string, object?>();
        try
        {
            var entries = _monsterFieldCache.GetOrAdd(monster.GetType(), BuildMonsterFields);
            foreach (var (key, f) in entries)
            {
                try { dict[key] = f.GetValue(monster); }
                catch { }
            }
        }
        catch (Exception ex) { UndoLogger.Warn($"[Snapshot] CaptureMonsterFields: {ex.Message}"); }
        return dict.Count > 0 ? dict : null;
    }

    private static (string key, FieldInfo field)[] BuildMonsterFields(Type type)
    {
        var list = new List<(string, FieldInfo)>();
        for (var t = type; t != null && t != typeof(object) && t != typeof(MonsterModel); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                var ft = f.FieldType;
                if (!ft.IsPrimitive && !ft.IsEnum) continue;
                list.Add(((t.FullName ?? t.Name) + "::" + f.Name, f));
            }
        }
        return list.ToArray();
    }

    private static MonsterMoveSnapshot? CaptureMonsterMove(MonsterModel monster)
    {
        var sm = monster.MoveStateMachine;
        if (sm == null) return null;

        var stateLog = new List<string>();
        if (ReflectionCache.SmStateLogProp?.GetValue(sm) is System.Collections.IEnumerable log)
        {
            foreach (var state in log)
            {
                var id = TryGetStateId(state);
                if (id != null) stateLog.Add(id);
            }
        }

        var performed = new Dictionary<string, bool>();
        if (ReflectionCache.SmStatesProp?.GetValue(sm) is System.Collections.IDictionary states
            && ReflectionCache.MoveStatePerformedField != null
            && ReflectionCache.MonsterStateType != null)
        {
            foreach (System.Collections.DictionaryEntry e in states)
            {
                if (e.Key is string key && e.Value != null)
                {
                    try
                    {
                        var v = ReflectionCache.MoveStatePerformedField.GetValue(e.Value);
                        if (v is bool b) performed[key] = b;
                    }
                    catch { }
                }
            }
        }

        return new MonsterMoveSnapshot
        {
            PerformedFirstMove = (bool)(ReflectionCache.SmPerformedFirstMoveField?.GetValue(sm) ?? false),
            SpawnedThisTurn = (bool)(ReflectionCache.MonsterSpawnedField?.GetValue(monster) ?? false),
            CurrentStateId = TryGetStateId(ReflectionCache.SmCurrentStateField?.GetValue(sm)),
            NextMoveStateId = TryGetStateId(monster.NextMove),
            StateLogIds = stateLog,
            MovePerformedAtLeastOnce = performed,
            CurrentStateRef = ReflectionCache.SmCurrentStateField?.GetValue(sm),
            NextMoveRef = monster.NextMove,
        };
    }

    private static string? TryGetStateId(object? state)
    {
        if (state == null || ReflectionCache.MonsterStateIdProperty == null) return null;
        try { return ReflectionCache.MonsterStateIdProperty.GetValue(state) as string; }
        catch { return null; }
    }

    private static void CaptureRunRng(CombatSnapshot snap, RunState runState)
    {
        var rngSet = runState.Rng;
        if (rngSet == null) return;
        if (ReflectionCache.RunRngDictField.GetValue(rngSet)
            is not Dictionary<RunRngType, Rng> dict) return;

        foreach (var kv in dict)
            snap.RunRngs[kv.Key] = (kv.Value.Seed, kv.Value.Counter);
    }

    private static void CaptureHistory(CombatSnapshot snap, CombatManager cm)
    {
        var history = ReflectionCache.CmHistoryProperty?.GetValue(cm);
        if (history == null) return;
        if (ReflectionCache.HistoryEntriesField?.GetValue(history)
            is not System.Collections.IList entries) return;

        snap.HistoryEntries = new List<object>(entries.Count);
        foreach (var e in entries)
            if (e != null) snap.HistoryEntries.Add(e);
    }

    private static void CaptureSyncState(CombatSnapshot snap)
    {
        try
        {
            var syncr = RunManager.Instance?.ActionQueueSynchronizer;
            if (syncr != null) snap.SyncCombatState = syncr.CombatState;
            snap.CombatManagerPaused = CombatManager.Instance?.IsPaused ?? false;
        }
        catch { }
    }
}

internal struct CreatureSnapshot
{
    public Creature Ref;
    public uint CombatId;
    public int CurrentHp;
    public int MaxHp;
    public int Block;
    public bool IsDead;
    public HpDisplay HpDisplay;
    public List<PowerSnapshot> Powers;

    public bool HadVisualNode;
    public Vector2 VisualPosition;
    public Vector2 VisualBodyScale;
    public Vector2 VisualBodyPosition;
    public float VisualBodyRotation;
    public Color VisualBodyModulate = Colors.White;

    public string? SpineAnimNameTrack0;
    public string? SpineAnimNameTrack1;
    public string? SpineAnimNameTrack2;
    public string? SpineAnimNameTrack3;

    public float? Hue;
    public float? DefaultScale;
    public float? TempScale;
    public double? LiquidOverlayTimer;
    public Material? BodyNormalMaterial;
    public bool LiquidOverlayWasActive;

    public Node? BodyRef;
    public Node? BodyParentRef;

    public (uint seed, int counter)? MonsterRng;
    public MonsterMoveSnapshot? MonsterMove;
    public Dictionary<string, object?>? MonsterFields;

    public CreatureSnapshot()
    {
        Ref = null!;
        Powers = new();
    }
}

internal struct PowerSnapshot
{
    public ModelId Id;
    public int Amount;
    public int AmountOnTurnStart;
    public bool SkipNextDurationTick;
    public object? InternalDataClone;
    public PowerModel? Ref;
    public PowerModel? Clone;
}

internal struct MonsterMoveSnapshot
{
    public string? CurrentStateId;
    public string? NextMoveStateId;
    public object? CurrentStateRef;
    public object? NextMoveRef;
    public bool PerformedFirstMove;
    public bool SpawnedThisTurn;
    public List<string> StateLogIds;
    public Dictionary<string, bool> MovePerformedAtLeastOnce;
}

internal struct RelicSnapshot
{
    public RelicModel Ref;
    public ModelId Id;
    public int StackCount;
    public object? Status;
    public object? DynamicVarsClone;
    public RelicModel? Clone;
}