using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace UndoModMS.Snapshot;

internal static class ReflectionCache
{
    // CombatManager
    private static readonly FieldInfo? CombatManagerTurnStateField =
       AccessTools.Field(typeof(CombatManager), "_turnState");
    private static PropertyInfo? _turnStateStateProp;

    public static CombatState? GetCombatState(CombatManager cm)
    {
        var turnState = CombatManagerTurnStateField?.GetValue(cm);
        if (turnState == null) return null;
        _turnStateStateProp ??= AccessTools.Property(turnState.GetType(), "State");
        return _turnStateStateProp?.GetValue(turnState) as CombatState;
    }

    // CombatState
    public static readonly FieldInfo CsAlliesField =
        AccessTools.Field(typeof(CombatState), "_allies");
    public static readonly FieldInfo CsEnemiesField =
        AccessTools.Field(typeof(CombatState), "_enemies");
    public static readonly FieldInfo? AllCardsField =
        AccessTools.Field(typeof(CombatState), "_allCards");
    public static readonly FieldInfo? NextCreatureIdField =
        AccessTools.Field(typeof(CombatState), "_nextCreatureId");

    // Creature
    public static readonly FieldInfo CreatureHpField =
        AccessTools.Field(typeof(Creature), "_currentHp");
    public static readonly FieldInfo CreatureMaxHpField =
        AccessTools.Field(typeof(Creature), "_maxHp");
    public static readonly FieldInfo CreatureBlockField =
        AccessTools.Field(typeof(Creature), "_block");
    public static readonly FieldInfo CreaturePowersField =
        AccessTools.Field(typeof(Creature), "_powers");

    public static readonly FieldInfo? CreatureCurrentHpChangedField =
        AccessTools.Field(typeof(Creature), "CurrentHpChanged");
    public static readonly FieldInfo? CreatureMaxHpChangedField =
        AccessTools.Field(typeof(Creature), "MaxHpChanged");
    public static readonly FieldInfo? CreatureBlockChangedField =
        AccessTools.Field(typeof(Creature), "BlockChanged");
    public static readonly FieldInfo? CreatureRevivedField =
        AccessTools.Field(typeof(Creature), "Revived");

    // PlayerCombatState
    public static readonly FieldInfo PcsEnergyField =
        AccessTools.Field(typeof(PlayerCombatState), "_energy");
    public static readonly FieldInfo PcsStarsField =
        AccessTools.Field(typeof(PlayerCombatState), "_stars");
    public static readonly FieldInfo? PcsPetsField =
        AccessTools.Field(typeof(PlayerCombatState), "_pets");
    public static readonly FieldInfo? PcsPilesField =
        AccessTools.Field(typeof(PlayerCombatState), "_piles");

    // CardPile
    public static readonly FieldInfo CardPileCardsField =
        AccessTools.Field(typeof(CardPile), "_cards");

    // PowerModel
    public static readonly FieldInfo PowerAmountField =
        AccessTools.Field(typeof(PowerModel), "_amount");
    public static readonly FieldInfo PowerAmountOnTurnStartField =
        AccessTools.Field(typeof(PowerModel), "_amountOnTurnStart");
    public static readonly FieldInfo PowerSkipField =
        AccessTools.Field(typeof(PowerModel), "_skipNextDurationTick");
    public static readonly FieldInfo? PowerInternalDataField =
        AccessTools.Field(typeof(PowerModel), "_internalData");

    // MonsterModel + move state machine
    public static readonly FieldInfo? MonsterRngField =
        AccessTools.Field(typeof(MonsterModel), "_rng");
    public static readonly FieldInfo? MonsterSpawnedField =
        AccessTools.Field(typeof(MonsterModel), "_spawnedThisTurn");
    public static readonly FieldInfo? MonsterMoveStateMachineField =
        AccessTools.Field(typeof(MonsterModel), "_moveStateMachine");
    public static readonly PropertyInfo? NextMoveProp =
        AccessTools.Property(typeof(MonsterModel), "NextMove");

    public static readonly Type? MonsterMoveStateMachineType =
        AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MonsterMoveStateMachine");
    public static readonly Type? MonsterStateType =
        AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MonsterState");
    public static readonly FieldInfo? SmCurrentStateField =
        MonsterMoveStateMachineType != null
            ? AccessTools.Field(MonsterMoveStateMachineType, "_currentState") : null;
    public static readonly FieldInfo? SmPerformedFirstMoveField =
        MonsterMoveStateMachineType != null
            ? AccessTools.Field(MonsterMoveStateMachineType, "_performedFirstMove") : null;
    public static readonly PropertyInfo? MonsterStateIdProperty =
        MonsterStateType != null
            ? AccessTools.Property(MonsterStateType, "Id") : null;

    public static readonly PropertyInfo? SmStatesProp =
        MonsterMoveStateMachineType != null
            ? AccessTools.Property(MonsterMoveStateMachineType, "States") : null;
    public static readonly PropertyInfo? SmStateLogProp =
        MonsterMoveStateMachineType != null
            ? AccessTools.Property(MonsterMoveStateMachineType, "StateLog") : null;
    public static readonly MethodInfo? SmForceCurrentStateMethod =
        MonsterMoveStateMachineType != null
            ? AccessTools.Method(MonsterMoveStateMachineType, "ForceCurrentState") : null;
    public static readonly FieldInfo? MoveStatePerformedField =
        MonsterStateType != null
            ? AccessTools.Field(MonsterStateType, "_performedAtLeastOnce") : null;

    // RelicModel
    public static readonly FieldInfo? RelicDynamicVarsField =
        AccessTools.Field(typeof(RelicModel), "_dynamicVars");
    public static readonly FieldInfo? RelicStackCountField =
        AccessTools.Field(typeof(RelicModel), "<StackCount>k__BackingField");
    public static readonly PropertyInfo? RelicStatusProperty =
        AccessTools.Property(typeof(RelicModel), "Status");

    // Player
    public static readonly FieldInfo PlayerPotionSlotsField =
        AccessTools.Field(typeof(Player), "_potionSlots");
    public static readonly FieldInfo? PlayerGoldField =
        AccessTools.Field(typeof(Player), "_gold");

    // PotionModel
    public static readonly FieldInfo? PotionRemovedField =
        AccessTools.Field(typeof(PotionModel), "<HasBeenRemovedFromState>k__BackingField");
    public static readonly FieldInfo? PotionOwnerField =
        AccessTools.Field(typeof(PotionModel), "_owner");

    // RNG
    public static readonly FieldInfo RunRngDictField =
        AccessTools.Field(typeof(RunRngSet), "_rngs");
    public static readonly PropertyInfo? RunManagerStateProperty =
        AccessTools.Property(typeof(RunManager), "State");

    // CombatHistory
    public static readonly PropertyInfo? CmHistoryProperty =
        AccessTools.Property(typeof(CombatManager), "History");
    public static readonly FieldInfo? HistoryEntriesField =
        CmHistoryProperty?.PropertyType != null
            ? AccessTools.Field(CmHistoryProperty.PropertyType, "_entries") : null;

    // PowerModel restoration helpers
    public static readonly MethodInfo? PowerInvokeAmountChangedMethod =
        AccessTools.Method(typeof(PowerModel), "InvokeAmountChanged");
    public static readonly FieldInfo? PowerOwnerField =
        AccessTools.Field(typeof(PowerModel), "_owner");

    // Card back-reference fixups
    public static readonly Type? CardEnergyCostType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost");
    public static readonly FieldInfo? EnergyCostCardField =
        CardEnergyCostType != null ? AccessTools.Field(CardEnergyCostType, "_card") : null;
    public static readonly FieldInfo? CardEnergyCostLocalModifiersField =
        CardEnergyCostType != null
            ? AccessTools.Field(CardEnergyCostType, "_localModifiers")
            : null;
    public static readonly PropertyInfo? CardEnergyCostProp =
        AccessTools.Property(typeof(CardModel), "EnergyCost");
    public static readonly PropertyInfo? CardDynamicVarsProp =
        AccessTools.Property(typeof(CardModel), "DynamicVars");
    public static readonly MethodInfo? DynamicVarsInitializeWithOwnerMethod =
        CardDynamicVarsProp?.PropertyType != null
            ? AccessTools.Method(CardDynamicVarsProp.PropertyType, "InitializeWithOwner")
            : null;

    // Enchantment / Affliction back-references
    public static readonly PropertyInfo? CardEnchantmentProp =
        AccessTools.Property(typeof(CardModel), "Enchantment");
    public static readonly PropertyInfo? CardAfflictionProp =
        AccessTools.Property(typeof(CardModel), "Affliction");
    public static readonly Type? EnchantmentModelType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.EnchantmentModel");
    public static readonly Type? AfflictionModelType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.AfflictionModel");
    public static readonly FieldInfo? EnchantmentCardField =
        EnchantmentModelType != null ? AccessTools.Field(EnchantmentModelType, "_card") : null;
    public static readonly FieldInfo? AfflictionCardField =
        AfflictionModelType != null ? AccessTools.Field(AfflictionModelType, "_card") : null;

    // Card mutable fields
    public static readonly FieldInfo[] CardMutableFields = InitCardMutableFields();
    private static FieldInfo[] InitCardMutableFields()
    {
        var skip = new HashSet<string>
        {
            "_cloneOf", "_canonicalInstance", "_deckVersion", "_owner",
            "_isDupe", "_currentTarget", "_isEnchantmentPreview",
            "<Id>k__BackingField", "<IsMutable>k__BackingField",
            "<CategorySortingId>k__BackingField", "<EntrySortingId>k__BackingField",
        };
        var list = new List<FieldInfo>();
        for (var t = typeof(CardModel); t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                if (skip.Contains(f.Name)) continue;
                list.Add(f);
            }
        }
        return [.. list];
    }

    // OrbQueue
    public static readonly FieldInfo? OrbQueueOrbsField =
        AccessTools.Field(typeof(OrbQueue), "_orbs");
    public static readonly FieldInfo? OrbQueueCapacityField =
        AccessTools.Field(typeof(OrbQueue), "<Capacity>k__BackingField");

    // NOrbManager
    public static readonly FieldInfo? NOrbManagerOrbsField =
        AccessTools.Field(typeof(NOrbManager), "_orbs");
    public static readonly FieldInfo? NOrbManagerContainerField =
        AccessTools.Field(typeof(NOrbManager), "_orbContainer");
    public static readonly FieldInfo? NOrbManagerTweenField =
        AccessTools.Field(typeof(NOrbManager), "_curTween");
    public static readonly MethodInfo? NOrbManagerTweenLayoutMethod =
        AccessTools.Method(typeof(NOrbManager), "TweenLayout");
    public static readonly MethodInfo? NOrbManagerUpdateNavMethod =
        AccessTools.Method(typeof(NOrbManager), "UpdateControllerNavigation");

    // NCombatRoom
    public static readonly FieldInfo? NcrRemovingNodesField =
        AccessTools.Field(typeof(NCombatRoom), "_removingCreatureNodes");
    public static readonly PropertyInfo? NCreatureEntityProp =
        AccessTools.Property(typeof(NCreature), "Entity");

    // Death-anim cancellation
    public static readonly PropertyInfo? NCreatureDeathAnimCancelTokenProp =
        AccessTools.Property(typeof(NCreature), "DeathAnimCancelToken");
    public static readonly PropertyInfo? NCreatureIsPlayingDeathAnimProp =
        AccessTools.Property(typeof(NCreature), "IsPlayingDeathAnimation");
    public static readonly FieldInfo? NCreatureIntentFadeTweenField =
        AccessTools.Field(typeof(NCreature), "_intentFadeTween");
    public static readonly FieldInfo? NCreatureShakeTweenField =
        AccessTools.Field(typeof(NCreature), "_shakeTween");
    public static readonly FieldInfo? NCreatureScaleTweenField =
        AccessTools.Field(typeof(NCreature), "_scaleTween");
    public static readonly FieldInfo? NCreatureTempScaleField =
        AccessTools.Field(typeof(NCreature), "_tempScale");

    // Spine animation handles
    public static readonly Type? NCreatureVisualsType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals");
    public static readonly PropertyInfo? NCreatureSpineAnimationProp =
        AccessTools.Property(typeof(NCreature), "SpineAnimation");
    public static readonly PropertyInfo? NCVSpineAnimationProp =
        NCreatureVisualsType != null ? AccessTools.Property(NCreatureVisualsType, "SpineAnimation") : null;
    public static readonly PropertyInfo? NCVSpineBodyProp =
        NCreatureVisualsType != null ? AccessTools.Property(NCreatureVisualsType, "SpineBody") : null;

    // Liquid-overlay shader
    public static readonly FieldInfo? NCVHueField =
        NCreatureVisualsType != null ? AccessTools.Field(NCreatureVisualsType, "_hue") : null;
    public static readonly FieldInfo? NCVLiquidOverlayTimerField =
        NCreatureVisualsType != null ? AccessTools.Field(NCreatureVisualsType, "_liquidOverlayTimer") : null;
    public static readonly FieldInfo? NCVSavedNormalMaterialField =
        NCreatureVisualsType != null ? AccessTools.Field(NCreatureVisualsType, "_savedNormalMaterial") : null;
    public static readonly FieldInfo? NCVCurrentLiquidOverlayMaterialField =
        NCreatureVisualsType != null ? AccessTools.Field(NCreatureVisualsType, "_currentLiquidOverlayMaterial") : null;
    public static readonly Type? SpineAnimAccessType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Bindings.MegaSpine.SpineAnimationAccess");
    public static readonly MethodInfo? SpineGetCurrentTrackMethod =
        SpineAnimAccessType != null ? AccessTools.Method(SpineAnimAccessType, "GetCurrentTrack") : null;
    public static readonly MethodInfo? SpineSetAnimationMethod =
        SpineAnimAccessType != null ? AccessTools.Method(SpineAnimAccessType, "SetAnimation") : null;
    public static readonly Type? MegaTrackEntryType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaTrackEntry");
    public static readonly MethodInfo? TrackGetAnimationMethod =
        MegaTrackEntryType != null ? AccessTools.Method(MegaTrackEntryType, "GetAnimation") : null;
    public static readonly Type? MegaAnimationType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaAnimation");
    public static readonly MethodInfo? AnimationGetDurationMethod =
        MegaAnimationType != null ? AccessTools.Method(MegaAnimationType, "GetDuration") : null;

    public static readonly Type? MegaSpriteType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite");
    public static readonly MethodInfo? MegaSpriteGetSkeletonMethod =
        MegaSpriteType != null ? AccessTools.Method(MegaSpriteType, "GetSkeleton") : null;
    public static readonly MethodInfo? MegaSpriteGetNormalMaterialMethod =
        MegaSpriteType != null ? AccessTools.Method(MegaSpriteType, "GetNormalMaterial") : null;
    public static readonly MethodInfo? MegaSpriteSetNormalMaterialMethod =
        MegaSpriteType != null ? AccessTools.Method(MegaSpriteType, "SetNormalMaterial") : null;
    public static readonly Type? MegaSkeletonType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSkeleton");
    public static readonly MethodInfo? SkeletonSetSlotsToSetupPoseMethod =
        MegaSkeletonType != null ? AccessTools.Method(MegaSkeletonType, "SetSlotsToSetupPose") : null;

    public static readonly MethodInfo? HurtAnimIsPlayingMethod =
        NCreatureVisualsType != null ? AccessTools.Method(NCreatureVisualsType, "IsPlayingHurtAnimation") : null;

    // CombatStateTracker
    public static readonly MethodInfo? NotifyCombatStateChangedMethod =
        AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged");

    // CombatManager end-turn / phase state
    public static readonly FieldInfo? CmPlayersReadyToEndTurnField =
        AccessTools.Field(typeof(CombatManager), "_playersReadyToEndTurn");
    public static readonly FieldInfo? CmPlayersReadyToBeginEnemyTurnField =
        AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn");
    public static readonly PropertyInfo? CmPlayerActionsDisabledProp =
        AccessTools.Property(typeof(CombatManager), "PlayerActionsDisabled");

    // NPlayerHand
    public static readonly FieldInfo? HandCurrentCardPlayField =
        AccessTools.Field(typeof(NPlayerHand), "_currentCardPlay");
    public static readonly FieldInfo? HandCurrentModeField =
        AccessTools.Field(typeof(NPlayerHand), "_currentMode");

    // NCreatureStateDisplay
    public static readonly MethodInfo? NCreatureStateDisplayRefreshValuesMethod =
        AccessTools.Method(typeof(NCreatureStateDisplay), "RefreshValues");
    public static readonly FieldInfo? NCreatureStateDisplayShowHideTweenField =
        AccessTools.Field(typeof(NCreatureStateDisplay), "_showHideTween");
    public static readonly FieldInfo? NCreatureStateDisplayHoverTweenField =
        AccessTools.Field(typeof(NCreatureStateDisplay), "_hoverTween");
    public static readonly FieldInfo? NCreatureStateDisplayOriginalPositionField =
        AccessTools.Field(typeof(NCreatureStateDisplay), "_originalPosition");

    // NPowerContainer
    public static readonly FieldInfo? NCreatureStateDisplayPowerContainerField =
        AccessTools.Field(typeof(NCreatureStateDisplay), "_powerContainer");
    public static readonly FieldInfo? NPowerContainerNodesField =
        AccessTools.Field(typeof(NPowerContainer), "_powerNodes");
    public static readonly MethodInfo? NPowerContainerAddMethod =
        AccessTools.Method(typeof(NPowerContainer), "Add", [typeof(PowerModel)]);
    public static readonly FieldInfo? NPowerContainerCreatureField =
        AccessTools.Field(typeof(NPowerContainer), "_creature");
    public static readonly MethodInfo? NPowerContainerConnectSignalsMethod =
        AccessTools.Method(typeof(NPowerContainer), "ConnectCreatureSignals");

    // NCombatCardPile
    public static readonly Type? NCombatCardPileType =
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Combat.NCombatCardPile");
    public static readonly FieldInfo? NCombatCardPileCurrentCountField =
        NCombatCardPileType != null ? AccessTools.Field(NCombatCardPileType, "_currentCount") : null;
    public static readonly FieldInfo? NCombatCardPileCountLabelField =
        NCombatCardPileType != null ? AccessTools.Field(NCombatCardPileType, "_countLabel") : null;

    // 방패 위치 버그 수정용 — NCreature → NCreatureStateDisplay → NHealthBar 체인
    public static readonly FieldInfo? NHealthBarOriginalBlockPositionField =
        AccessTools.Field(typeof(NHealthBar), "_originalBlockPosition");
    public static readonly FieldInfo? NHealthBarBlockTweenField =
        AccessTools.Field(typeof(NHealthBar), "_blockTween");

    static ReflectionCache()
    {
        LogStartupDiagnostics();
    }

    private static void LogStartupDiagnostics()
    {
        var nulls = new List<string>();
        void Check(string name, object? member) { if (member == null) nulls.Add(name); }

        Check(nameof(CombatManagerTurnStateField), CombatManagerTurnStateField);
        Check(nameof(AllCardsField), AllCardsField);
        Check(nameof(NextCreatureIdField), NextCreatureIdField);
        Check(nameof(PcsPetsField), PcsPetsField);
        Check(nameof(PcsPilesField), PcsPilesField);
        Check(nameof(PowerInternalDataField), PowerInternalDataField);
        Check(nameof(MonsterRngField), MonsterRngField);
        Check(nameof(MonsterSpawnedField), MonsterSpawnedField);
        Check(nameof(MonsterMoveStateMachineField), MonsterMoveStateMachineField);
        Check(nameof(NextMoveProp), NextMoveProp);
        Check(nameof(MonsterMoveStateMachineType), MonsterMoveStateMachineType);
        Check(nameof(MonsterStateType), MonsterStateType);
        Check(nameof(SmCurrentStateField), SmCurrentStateField);
        Check(nameof(SmPerformedFirstMoveField), SmPerformedFirstMoveField);
        Check(nameof(MonsterStateIdProperty), MonsterStateIdProperty);
        Check(nameof(SmStatesProp), SmStatesProp);
        Check(nameof(SmStateLogProp), SmStateLogProp);
        Check(nameof(SmForceCurrentStateMethod), SmForceCurrentStateMethod);
        Check(nameof(MoveStatePerformedField), MoveStatePerformedField);
        Check(nameof(RelicDynamicVarsField), RelicDynamicVarsField);
        Check(nameof(RelicStackCountField), RelicStackCountField);
        Check(nameof(RelicStatusProperty), RelicStatusProperty);
        Check(nameof(PlayerGoldField), PlayerGoldField);
        Check(nameof(PotionRemovedField), PotionRemovedField);
        Check(nameof(PotionOwnerField), PotionOwnerField);
        Check(nameof(RunManagerStateProperty), RunManagerStateProperty);
        Check(nameof(CmHistoryProperty), CmHistoryProperty);
        Check(nameof(HistoryEntriesField), HistoryEntriesField);
        Check(nameof(PowerInvokeAmountChangedMethod), PowerInvokeAmountChangedMethod);
        Check(nameof(PowerOwnerField), PowerOwnerField);
        Check(nameof(CardEnergyCostType), CardEnergyCostType);
        Check(nameof(EnergyCostCardField), EnergyCostCardField);
        Check(nameof(CardEnergyCostLocalModifiersField), CardEnergyCostLocalModifiersField);
        Check(nameof(CardEnergyCostProp), CardEnergyCostProp);
        Check(nameof(CardDynamicVarsProp), CardDynamicVarsProp);
        Check(nameof(DynamicVarsInitializeWithOwnerMethod), DynamicVarsInitializeWithOwnerMethod);
        Check(nameof(CardEnchantmentProp), CardEnchantmentProp);
        Check(nameof(CardAfflictionProp), CardAfflictionProp);
        Check(nameof(EnchantmentModelType), EnchantmentModelType);
        Check(nameof(AfflictionModelType), AfflictionModelType);
        Check(nameof(EnchantmentCardField), EnchantmentCardField);
        Check(nameof(AfflictionCardField), AfflictionCardField);
        Check(nameof(OrbQueueOrbsField), OrbQueueOrbsField);
        Check(nameof(OrbQueueCapacityField), OrbQueueCapacityField);
        Check(nameof(NOrbManagerOrbsField), NOrbManagerOrbsField);
        Check(nameof(NOrbManagerContainerField), NOrbManagerContainerField);
        Check(nameof(NOrbManagerTweenField), NOrbManagerTweenField);
        Check(nameof(NOrbManagerTweenLayoutMethod), NOrbManagerTweenLayoutMethod);
        Check(nameof(NOrbManagerUpdateNavMethod), NOrbManagerUpdateNavMethod);
        Check(nameof(NcrRemovingNodesField), NcrRemovingNodesField);
        Check(nameof(NCreatureEntityProp), NCreatureEntityProp);
        Check(nameof(NCreatureDeathAnimCancelTokenProp), NCreatureDeathAnimCancelTokenProp);
        Check(nameof(NCreatureIsPlayingDeathAnimProp), NCreatureIsPlayingDeathAnimProp);
        Check(nameof(NCreatureIntentFadeTweenField), NCreatureIntentFadeTweenField);
        Check(nameof(NCreatureShakeTweenField), NCreatureShakeTweenField);
        Check(nameof(NCreatureScaleTweenField), NCreatureScaleTweenField);
        Check(nameof(NCreatureTempScaleField), NCreatureTempScaleField);
        Check(nameof(NCreatureVisualsType), NCreatureVisualsType);
        Check(nameof(NCreatureSpineAnimationProp), NCreatureSpineAnimationProp);
        Check(nameof(NCVSpineAnimationProp), NCVSpineAnimationProp);
        Check(nameof(NCVSpineBodyProp), NCVSpineBodyProp);
        Check(nameof(NCVHueField), NCVHueField);
        Check(nameof(NCVLiquidOverlayTimerField), NCVLiquidOverlayTimerField);
        Check(nameof(NCVSavedNormalMaterialField), NCVSavedNormalMaterialField);
        Check(nameof(NCVCurrentLiquidOverlayMaterialField), NCVCurrentLiquidOverlayMaterialField);
        Check(nameof(SpineAnimAccessType), SpineAnimAccessType);
        Check(nameof(SpineGetCurrentTrackMethod), SpineGetCurrentTrackMethod);
        Check(nameof(SpineSetAnimationMethod), SpineSetAnimationMethod);
        Check(nameof(MegaTrackEntryType), MegaTrackEntryType);
        Check(nameof(TrackGetAnimationMethod), TrackGetAnimationMethod);
        Check(nameof(MegaAnimationType), MegaAnimationType);
        Check(nameof(AnimationGetDurationMethod), AnimationGetDurationMethod);
        Check(nameof(MegaSpriteType), MegaSpriteType);
        Check(nameof(MegaSpriteGetSkeletonMethod), MegaSpriteGetSkeletonMethod);
        Check(nameof(MegaSpriteGetNormalMaterialMethod), MegaSpriteGetNormalMaterialMethod);
        Check(nameof(MegaSpriteSetNormalMaterialMethod), MegaSpriteSetNormalMaterialMethod);
        Check(nameof(MegaSkeletonType), MegaSkeletonType);
        Check(nameof(SkeletonSetSlotsToSetupPoseMethod), SkeletonSetSlotsToSetupPoseMethod);
        Check(nameof(HurtAnimIsPlayingMethod), HurtAnimIsPlayingMethod);
        Check(nameof(NotifyCombatStateChangedMethod), NotifyCombatStateChangedMethod);
        Check(nameof(CmPlayersReadyToEndTurnField), CmPlayersReadyToEndTurnField);
        Check(nameof(CmPlayersReadyToBeginEnemyTurnField), CmPlayersReadyToBeginEnemyTurnField);
        Check(nameof(CmPlayerActionsDisabledProp), CmPlayerActionsDisabledProp);
        Check(nameof(HandCurrentCardPlayField), HandCurrentCardPlayField);
        Check(nameof(HandCurrentModeField), HandCurrentModeField);
        Check(nameof(NCreatureStateDisplayRefreshValuesMethod), NCreatureStateDisplayRefreshValuesMethod);
        Check(nameof(NCreatureStateDisplayShowHideTweenField), NCreatureStateDisplayShowHideTweenField);
        Check(nameof(NCreatureStateDisplayHoverTweenField), NCreatureStateDisplayHoverTweenField);
        Check(nameof(NCreatureStateDisplayOriginalPositionField), NCreatureStateDisplayOriginalPositionField);
        Check(nameof(NCreatureStateDisplayPowerContainerField), NCreatureStateDisplayPowerContainerField);
        Check(nameof(NPowerContainerNodesField), NPowerContainerNodesField);
        Check(nameof(NPowerContainerAddMethod), NPowerContainerAddMethod);
        Check(nameof(NPowerContainerCreatureField), NPowerContainerCreatureField);
        Check(nameof(NPowerContainerConnectSignalsMethod), NPowerContainerConnectSignalsMethod);
        Check(nameof(NCombatCardPileType), NCombatCardPileType);
        Check(nameof(NCombatCardPileCurrentCountField), NCombatCardPileCurrentCountField);
        Check(nameof(NCombatCardPileCountLabelField), NCombatCardPileCountLabelField);
        Check(nameof(NHealthBarOriginalBlockPositionField), NHealthBarOriginalBlockPositionField);
        Check(nameof(NHealthBarBlockTweenField), NHealthBarBlockTweenField);

        if (NCreatureVisualsType != null)
            UndoLogger.Info($"[Reflection] NCreatureVisuals resolved: {NCreatureVisualsType.FullName}");

        if (nulls.Count > 0)
            UndoLogger.Warn($"[Reflection] {nulls.Count} member(s) NULL — game update may have changed: {string.Join(", ", nulls)}");
        else
            UndoLogger.Info("[Reflection] all reflection targets resolved.");
    }
}