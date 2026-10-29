using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace SamplePlugin;

internal sealed class P2ForsakenTracker : IDisposable
{
    private const uint SigmascapeV4TerritoryId = 1363;
    private const uint ForsakenActionId = 47804;
    private const uint PastEndActionId = 47827;
    private const uint FutureEndActionId = 47826;
    internal const uint AllThingsEndingActionId = 47836;
    internal const uint AllThingsEndingVariantActionId = 47837;
    private const uint SpellsTroubleStatusId = 5083;
    private const int InitialStacksPerPlayer = 4;
    private const int PartySize = 8;
    private const long MarkerHitGapMilliseconds = 3000;
    private const uint PathOfLightActionId = 47806;
    internal const uint SpelldriverActionId = 47808;
    internal const uint SpellscatterActionId = 47809;
    internal const uint SpellwaveActionId = 47810;

    // The Past's End / Future's End circles baited by the clones and the boss (47830-47833) before each even tower.
    internal const uint BaitFirstActionId = 47830;
    internal const uint BaitLastActionId = 47833;
    private const long CloneWindowMilliseconds = 2000;
    private const long CloneGroupGapMilliseconds = 1000;

    // All Things Ending is a 4.7 s cast and the odd tower hits right after it resolves.
    private const long CloneFixWindowMilliseconds = 8000;
    private const float ArenaCenter = 100f;
    private const float BossCenterToleranceYalms = 0.5f;
    private const long TowerGroupGapMilliseconds = 3000;

    internal const uint StackMarkerId = 715;
    internal const uint SpreadMarkerId = 716;
    internal const uint ConeMarkerId = 717;

    private readonly GameEventMonitor monitor;
    private readonly Dictionary<uint, uint> initialMarkers = [];
    private readonly Dictionary<uint, int> remainingStacks = [];
    private readonly HashSet<uint> playersSeenWithStacks = [];
    private readonly Dictionary<uint, uint> observedCasts = [];
    private readonly Configuration configuration;
    private readonly RunHistory<ForsakenRun> history;
    private readonly Dictionary<uint, ForsakenTowerRecord> currentTowers = [];
    private readonly List<(long Tick, ForsakenCloneRecord Clone)> pendingClones = [];
    private readonly List<(long Tick, ForsakenAttackRecord Bait)> pendingBaits = [];
    private readonly Dictionary<uint, ForsakenAttackRecord> baitsByCaster = [];
    private (long Tick, List<ForsakenSpot> Spots)? pendingCloneSpots;
    private long lastCloneEventTick;
    private (long Tick, List<ForsakenSpot> Spots)? pendingFixSpots;
    private ForsakenRun? currentRun;
    private long lastMarkerEventTick;
    private long lastTowerEventTick;
    private int markerHitIndex = -1;
    private int towerRoundCount;
    private ForsakenRound? currentTowerRound;
    private int? lastObservedTowerRounds;
    private bool finalAllThingsEndingSeen;
    private int activePhaseIndex;

    public bool IsForsakenPhase { get; private set; }

    /// <summary>The run being recorded right now, or null outside Forsaken.</summary>
    public ForsakenRun? CurrentRun => currentRun;

    public uint? LastLocalMarkerId { get; private set; }

    public uint? InitialLocalMarkerId { get; private set; }

    public int? LocalRemainingStacks { get; private set; }

    public int? CompletedTowerRounds { get; private set; }

    public string? LastResolve { get; private set; }

    public int ActivePhaseIndex => activePhaseIndex;

    public P2ForsakenTracker(GameEventMonitor monitor, Configuration configuration, RunHistory<ForsakenRun> history)
    {
        this.monitor = monitor;
        this.configuration = configuration;
        this.history = history;

        monitor.ActionEffectReceived += OnActionEffect;
        monitor.ActionEffectTargetReceived += OnActionEffectTarget;
        monitor.TargetIconReceived += OnTargetIcon;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>Returns the human-readable Forsaken marker name.</summary>
    public static string MarkerName(uint markerId)
    {
        return markerId switch
        {
            StackMarkerId => "Stack",
            SpreadMarkerId => "Spread",
            ConeMarkerId => "Cone",
            _ => "Unknown marker",
        };
    }

    /// <summary>Gets the marker assigned when Forsaken began for a party member.</summary>
    public bool TryGetInitialMarker(uint entityId, out uint markerId)
    {
        return initialMarkers.TryGetValue(entityId, out markerId);
    }

    /// <summary>Releases event subscriptions used by the tracker.</summary>
    public void Dispose()
    {
        monitor.ActionEffectReceived -= OnActionEffect;
        monitor.ActionEffectTargetReceived -= OnActionEffectTarget;
        monitor.TargetIconReceived -= OnTargetIcon;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        ResetForsakenState();
    }

    private void OnTerritoryChanged(uint _)
    {
        ResetForsakenState();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!IsForsakenPhase || Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        // A wipe leaves the territory unchanged, so leaving combat is what ends an unfinished run.
        if (!Plugin.Condition[ConditionFlag.InCombat])
        {
            ResetForsakenState();
            return;
        }

        foreach (var member in Plugin.PartyList)
        {
            var foundStacks = false;
            foreach (var status in member.Statuses)
            {
                if (status.StatusId != SpellsTroubleStatusId)
                {
                    continue;
                }

                remainingStacks[member.EntityId] = (int)status.Param;
                playersSeenWithStacks.Add(member.EntityId);
                foundStacks = true;
                break;
            }

            if (!foundStacks && playersSeenWithStacks.Contains(member.EntityId))
            {
                remainingStacks[member.EntityId] = 0;
            }
        }

        UpdateTowerRoundCount();
        UpdateEncounterCasts();
        RecordBossHitbox();
    }

    /// <summary>Reads the boss's hitbox radius once; the boss is the Kefka standing at the arena centre, unlike the clones.</summary>
    private void RecordBossHitbox()
    {
        if (currentRun is not { BossHitboxRadius: 0 } run)
        {
            return;
        }

        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject.ObjectKind != ObjectKind.BattleNpc
                || !gameObject.Name.TextValue.Contains("Kefka", StringComparison.OrdinalIgnoreCase)
                || MathF.Abs(gameObject.Position.X - ArenaCenter) > BossCenterToleranceYalms
                || MathF.Abs(gameObject.Position.Z - ArenaCenter) > BossCenterToleranceYalms)
            {
                continue;
            }

            run.BossHitboxRadius = gameObject.HitboxRadius;
            return;
        }
    }

    private void UpdateTowerRoundCount()
    {
        if (playersSeenWithStacks.Count < PartySize)
        {
            return;
        }

        var totalRemainingStacks = 0;
        foreach (var stacks in remainingStacks.Values)
        {
            totalRemainingStacks += stacks;
        }

        var stacksRemoved = (PartySize * InitialStacksPerPlayer) - totalRemainingStacks;
        if (stacksRemoved >= 0)
        {
            var completedRounds = Math.Clamp(stacksRemoved / 4, 0, 8);
            if (lastObservedTowerRounds is int previousRounds && completedRounds > previousRounds)
            {
                for (var tower = previousRounds + 1; tower <= completedRounds; tower++)
                {
                    activePhaseIndex = tower % 2 == 0 ? 2 : 1;
                    if (tower % 2 == 1)
                    {
                        LastResolve = null;
                    }
                }
            }

            CompletedTowerRounds = completedRounds;
            lastObservedTowerRounds = completedRounds;
        }

        if (Plugin.ObjectTable.LocalPlayer is { } localPlayer
            && remainingStacks.TryGetValue(localPlayer.EntityId, out var localStacks))
        {
            LocalRemainingStacks = localStacks;
        }
    }

    private void UpdateEncounterCasts()
    {
        var activeTrackedCasts = new HashSet<uint>();
        var finalAllThingsEndingCasting = false;
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara { IsCasting: true } character)
            {
                continue;
            }

            var actionId = character.CastActionId;
            var isTrackedCast = actionId is PastEndActionId
                or FutureEndActionId
                or AllThingsEndingActionId
                or AllThingsEndingVariantActionId;
            if (!isTrackedCast)
            {
                continue;
            }

            activeTrackedCasts.Add(character.EntityId);
            var isNewCast = !observedCasts.TryGetValue(character.EntityId, out var previousActionId)
                || previousActionId != actionId;
            observedCasts[character.EntityId] = actionId;
            if (CompletedTowerRounds == 8
                && actionId is AllThingsEndingActionId or AllThingsEndingVariantActionId)
            {
                finalAllThingsEndingCasting = true;
            }

            if (!isNewCast)
            {
                continue;
            }

            switch (actionId)
            {
                case PastEndActionId:
                    LastResolve = "Past's End";
                    break;
                case FutureEndActionId:
                    LastResolve = "Future's End";
                    break;
                case AllThingsEndingActionId:
                case AllThingsEndingVariantActionId:
                    activePhaseIndex = 0;

                    // The clones cast one after another; only the first of the volley takes the snapshot.
                    var castTick = Environment.TickCount64;
                    if (currentRun is { } castRun
                        && (pendingFixSpots is not { } fix || castTick - fix.Tick > CloneGroupGapMilliseconds))
                    {
                        pendingFixSpots = (castTick, TakeSnapshot(castRun, 0).Spots);
                    }

                    break;
            }
        }

        foreach (var entityId in observedCasts.Keys.ToArray())
        {
            if (!activeTrackedCasts.Contains(entityId))
            {
                observedCasts.Remove(entityId);
            }
        }

        // The last All Things Ending after the eighth tower ends the mechanic: once every clone's cast is over, go idle.
        if (CompletedTowerRounds != 8)
        {
            return;
        }

        if (finalAllThingsEndingCasting)
        {
            finalAllThingsEndingSeen = true;
        }
        else if (finalAllThingsEndingSeen &&  activeTrackedCasts.Count == 0)
        {
            ResetForsakenState();
        }
    }

    /// <summary>Stores the active run in the persistent history once Forsaken ends, wipes, or is interrupted.</summary>
    private void FinishRun()
    {
        if (currentRun is not { } run)
        {
            return;
        }

        currentRun = null;
        if (run.Players.Count == 0)
        {
            return;
        }

        run.Completed = CompletedTowerRounds == 8;
        AddFinalBaitRound(run);
        foreach (var player in run.Players)
        {
            player.Position = configuration.GetPosition(player.Name);
        }
        var buddy = RaidPositions.ResolveBuddy(configuration).Buddy;
        if (buddy is not null
            && Plugin.ObjectTable.LocalPlayer is { } localPlayer
            && initialMarkers.TryGetValue(localPlayer.EntityId, out var localMarker)
            && initialMarkers.TryGetValue(buddy.EntityId, out var buddyMarker))
        {
            run.BuddyName = buddy.Name.TextValue;
            run.LocalIsAlpha = localMarker != buddyMarker;
        }

        history.Add(run);
    }

    /// <summary>
    /// The last All Things Ending comes after the eighth tower and has no tower of its own, so nothing would attach
    /// its snapshot to a round. It is kept as a final pseudo-round, with whatever clone data arrived after tower 8.
    /// </summary>
    private void AddFinalBaitRound(ForsakenRun run)
    {
        if (CompletedTowerRounds != 8
            || pendingFixSpots is not { } fix
            || fix.Tick <= lastTowerEventTick
            || run.Rounds.All(round => round.Number != 8))
        {
            return;
        }

        var round = new ForsakenRound
        {
            Number = ForsakenRound.FinalBaitNumber,
            Spots = [.. fix.Spots],
            FixSpots = [.. fix.Spots],
            Clones = [.. pendingClones.Where(entry => entry.Tick > lastTowerEventTick).Select(entry => entry.Clone)],
            Attacks = [.. pendingBaits.Where(entry => entry.Tick > lastTowerEventTick).Select(entry => entry.Bait)],
        };
        if (pendingCloneSpots is { } cloneSpots && cloneSpots.Tick > lastTowerEventTick)
        {
            round.CloneSpots = cloneSpots.Spots;
        }

        run.Rounds.Add(round);
    }

    /// <summary>Adds a marker to the run, grouping events that arrive together into one tower hit.</summary>
    private void RecordMarker(uint entityId, uint markerId)
    {
        if (currentRun is not { } run)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (markerHitIndex < 0 || now - lastMarkerEventTick > MarkerHitGapMilliseconds)
        {
            markerHitIndex++;
        }

        lastMarkerEventTick = now;

        var player = GetOrAddPlayer(run, entityId);
        player.Markers.Add(new ForsakenMarkerEntry { Hit = markerHitIndex, MarkerId = markerId });

        // The first marker batch means everyone is in place and no tower has gone off yet.
        if (markerHitIndex == 0 && run.Rounds.Count == 0)
        {
            run.Rounds.Add(TakeSnapshot(run, 0));
        }
    }

    /// <summary>Finds the player's record in the run, creating it (with name and job) on first sight.</summary>
    private static ForsakenPlayerRecord GetOrAddPlayer(ForsakenRun run, uint entityId)
    {
        var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == entityId);
        if (player is not null)
        {
            return player;
        }

        var member = Plugin.PartyList.FirstOrDefault(candidate => candidate.EntityId == entityId);
        player = new ForsakenPlayerRecord
        {
            EntityId = entityId,
            Name = member?.Name.TextValue ?? "Unknown",
            JobId = member?.ClassJob.RowId ?? 0,
            IsLocalPlayer = Plugin.ObjectTable.LocalPlayer?.EntityId == entityId,
        };
        run.Players.Add(player);
        return player;
    }

    /// <summary>Records where every party member stands right now.</summary>
    private static ForsakenRound TakeSnapshot(ForsakenRun run, int number)
    {
        var round = new ForsakenRound { Number = number };
        foreach (var member in Plugin.PartyList)
        {
            GetOrAddPlayer(run, member.EntityId);
            if (Plugin.ObjectTable.SearchById(member.EntityId) is { } gameObject)
            {
                round.Spots.Add(new ForsakenSpot
                {
                    EntityId = member.EntityId,
                    X = gameObject.Position.X,
                    Z = gameObject.Position.Z,
                });
            }
        }

        return round;
    }

    /// <summary>
    /// Records a tower going off. The first hit of a batch starts a new round (and snapshots everyone's position);
    /// each tower is its own caster, so hits are grouped by caster and located at the caster's position.
    /// </summary>
    private void OnActionEffectTarget(uint casterEntityId, uint actionId, uint targetEntityId)
    {
        if (!IsForsakenPhase || currentRun is null)
        {
            return;
        }

        if (actionId is SpelldriverActionId or SpellscatterActionId or SpellwaveActionId
            or AllThingsEndingActionId or AllThingsEndingVariantActionId
            or (>= BaitFirstActionId and <= BaitLastActionId))
        {
            RecordAttack(casterEntityId, actionId, targetEntityId);
            return;
        }

        if (actionId != PathOfLightActionId)
        {
            return;
        }

        var run = currentRun;
        var now = Environment.TickCount64;
        if (currentTowerRound is null || now - lastTowerEventTick > TowerGroupGapMilliseconds)
        {
            towerRoundCount++;
            currentTowerRound = TakeSnapshot(run, towerRoundCount);
            currentTowers.Clear();
            run.Rounds.Add(currentTowerRound);
            foreach (var (tick, clone) in pendingClones)
            {
                if (now - tick <= CloneWindowMilliseconds)
                {
                    currentTowerRound.Clones.Add(clone);
                }
            }

            pendingClones.Clear();

            // The clone volley's position snapshot was taken a moment before the round existed.
            if (pendingCloneSpots is { } pendingSpots && now - pendingSpots.Tick <= CloneWindowMilliseconds)
            {
                currentTowerRound.CloneSpots = pendingSpots.Spots;
            }

            pendingCloneSpots = null;

            if (pendingFixSpots is { } pendingFix && now - pendingFix.Tick <= CloneFixWindowMilliseconds)
            {
                currentTowerRound.FixSpots = pendingFix.Spots;
            }

            pendingFixSpots = null;

            foreach (var (tick, bait) in pendingBaits)
            {
                if (now - tick <= CloneWindowMilliseconds)
                {
                    currentTowerRound.Attacks.Add(bait);
                }
            }

            pendingBaits.Clear();
        }

        lastTowerEventTick = now;

        if (!currentTowers.TryGetValue(casterEntityId, out var tower))
        {
            if (Plugin.ObjectTable.SearchById(casterEntityId) is not { } towerObject)
            {
                return;
            }

            tower = new ForsakenTowerRecord { X = towerObject.Position.X, Z = towerObject.Position.Z };
            currentTowers[casterEntityId] = tower;
            currentTowerRound.Towers.Add(tower);
        }

        tower.Soakers.Add(targetEntityId);

        // The position at the moment the tower hit is the exact soak position, so it replaces the snapshot's.
        if (Plugin.ObjectTable.SearchById(targetEntityId) is { } soaker
            && currentTowerRound.Spots.FirstOrDefault(spot => spot.EntityId == targetEntityId) is { } soakerSpot)
        {
            soakerSpot.X = soaker.Position.X;
            soakerSpot.Z = soaker.Position.Z;
        }
    }

    /// <summary>
    /// Records a stack, spread or cone hit, or a hit by a clone (bait circle or All Things Ending), in the current
    /// tower round. Stack, spread and cone resolve right after the round's towers; clone hits land just before or
    /// after them. The cone's caster stands on the player holding the cone marker.
    /// </summary>
    private void RecordAttack(uint casterEntityId, uint actionId, uint targetEntityId)
    {
        var isBait = actionId is >= BaitFirstActionId and <= BaitLastActionId;
        var isCloneHit = isBait || actionId is AllThingsEndingActionId or AllThingsEndingVariantActionId;
        if ((currentTowerRound is null && !isCloneHit) || currentRun is not { } run
            || Plugin.ObjectTable.SearchById(targetEntityId) is not { } target)
        {
            return;
        }

        var attack = new ForsakenAttackRecord
        {
            ActionId = actionId,
            TargetEntityId = targetEntityId,
            TargetX = target.Position.X,
            TargetZ = target.Position.Z,
        };

        // A bait's caster is a clone that still stands at the centre; its spawn position is filled in once it casts
        // All Things Ending (see RecordClone).
        if (isBait)
        {
            baitsByCaster[casterEntityId] = attack;
        }
        else if (Plugin.ObjectTable.SearchById(casterEntityId) is { } caster)
        {
            attack.CasterX = caster.Position.X;
            attack.CasterZ = caster.Position.Z;
        }

        var now = Environment.TickCount64;
        if (isCloneHit)
        {
            CaptureCloneSpots(now);
        }
        else if (currentTowerRound is { AttackSpots.Count: 0 } round)
        {
            round.AttackSpots = TakeSnapshot(run, 0).Spots;
        }

        // Baits land a moment before their even tower, when the previous round is still the current one.
        if (isCloneHit && (currentTowerRound is null || now - lastTowerEventTick > CloneWindowMilliseconds))
        {
            pendingBaits.Add((now, attack));
            return;
        }

        currentTowerRound?.Attacks.Add(attack);
    }

    /// <summary>
    /// Snapshots everyone's position at the first hit of a clone volley (the bait circles, or All Things Ending), so
    /// players who were not hit still have a position for that moment. The snapshot waits for its round if the
    /// round's first tower has not gone off yet.
    /// </summary>
    private void CaptureCloneSpots(long now)
    {
        if (currentRun is not { } run)
        {
            return;
        }

        var isNewVolley = lastCloneEventTick == 0 || now - lastCloneEventTick > CloneGroupGapMilliseconds;
        lastCloneEventTick = now;
        if (!isNewVolley)
        {
            return;
        }

        var spots = TakeSnapshot(run, 0).Spots;
        if (currentTowerRound is { CloneSpots.Count: 0 } round && now - lastTowerEventTick <= CloneWindowMilliseconds)
        {
            round.CloneSpots = spots;
            return;
        }

        pendingCloneSpots = (now, spots);
    }

    private void ResetForsakenState()
    {
        FinishRun();
        IsForsakenPhase = false;
        towerRoundCount = 0;
        currentTowerRound = null;
        currentTowers.Clear();
        pendingClones.Clear();
        pendingBaits.Clear();
        baitsByCaster.Clear();
        pendingCloneSpots = null;
        pendingFixSpots = null;
        lastCloneEventTick = 0;
        LocalRemainingStacks = null;
        CompletedTowerRounds = null;
        LastResolve = null;
        lastObservedTowerRounds = null;
        finalAllThingsEndingSeen = false;
        activePhaseIndex = 0;
        initialMarkers.Clear();
        remainingStacks.Clear();
        playersSeenWithStacks.Clear();
        observedCasts.Clear();
    }

    private void OnTargetIcon(uint entityId, uint markerId, uint arg2)
    {
        if (!IsForsakenPhase)
        {
            return;
        }

        // Limit Cut head markers can arrive while the Forsaken phase flag is still set; they are not Forsaken markers.
        if (markerId is not (StackMarkerId or SpreadMarkerId or ConeMarkerId))
        {
            return;
        }

        var targetEntityId = IsPartyMember(entityId)
            ? entityId
            : IsPartyMember(arg2) ? arg2 : 0;

        if (targetEntityId == 0)
        {
            return;
        }

        initialMarkers.TryAdd(targetEntityId, markerId);
        RecordMarker(targetEntityId, markerId);

        if (Plugin.ObjectTable.LocalPlayer is { } localPlayer && targetEntityId == localPlayer.EntityId)
        {
            LastLocalMarkerId = markerId;
            InitialLocalMarkerId ??= markerId;
        }
    }

    private bool IsPartyMember(uint entityId)
    {
        foreach (var member in Plugin.PartyList)
        {
            if (member.EntityId == entityId)
            {
                return true;
            }
        }

        return false;
    }

    private void OnActionEffect(uint casterEntityId, uint actionId)
    {
        if (Plugin.ClientState.TerritoryType == SigmascapeV4TerritoryId && actionId == ForsakenActionId)
        {
            ResetForsakenState();
            IsForsakenPhase = true;
            currentRun = new ForsakenRun { StartedUtc = DateTime.UtcNow };
            markerHitIndex = -1;
            return;
        }

        if (IsForsakenPhase && actionId is AllThingsEndingActionId or AllThingsEndingVariantActionId)
        {
            RecordClone(casterEntityId, actionId);
        }
    }

    /// <summary>
    /// Records where a Kefka (clone or boss) fired its All Things Ending cone. The cone resolves around the time of
    /// an odd tower, sometimes just before the tower's first hit, so early ones wait in a pending list.
    /// </summary>
    private void RecordClone(uint casterEntityId, uint actionId)
    {
        if (Plugin.ObjectTable.SearchById(casterEntityId) is not { } caster)
        {
            return;
        }

        // The cone points the way the caster was cast. At resolution the 47837 variant's caster is turned half a
        // circle away from that, while the 47836 variant's caster still faces it (checked against the logged pulls).
        var turn = actionId == AllThingsEndingVariantActionId ? MathF.PI : 0.0f;
        var clone = new ForsakenCloneRecord
        {
            X = caster.Position.X,
            Z = caster.Position.Z,
            Facing = LimitCutStrategy.FacingDegrees(caster.Rotation + turn),
        };

        // The clone that was baited stands where it will cast from, so that is where it spawned.
        if (baitsByCaster.TryGetValue(casterEntityId, out var bait))
        {
            bait.CasterX = clone.X;
            bait.CasterZ = clone.Z;
        }

        var now = Environment.TickCount64;
        CaptureCloneSpots(now);
        if (currentTowerRound is not null && now - lastTowerEventTick <= CloneWindowMilliseconds)
        {
            currentTowerRound.Clones.Add(clone);
            return;
        }

        pendingClones.Add((now, clone));
    }
}
