using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace SamplePlugin;

internal sealed unsafe class P2ForsakenTracker : IDisposable
{
    private const uint SigmascapeV4TerritoryId = 1363;
    private const uint ForsakenActionId = 47804;
    private const uint PastEndActionId = 47827;
    private const uint FutureEndActionId = 47826;
    private const uint AllThingsEndingActionId = 47836;
    private const uint AllThingsEndingVariantActionId = 47837;
    private const uint TargetIconCategory = 34;
    private const uint SpellsTroubleStatusId = 5083;
    private const int InitialStacksPerPlayer = 4;
    private const int PartySize = 8;

    private const uint StackMarkerId = 715;
    private const uint SpreadMarkerId = 716;
    private const uint ConeMarkerId = 717;

    private readonly Hook<ActionEffectHandler.Delegates.Receive> actionEffectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> actorControlHook;
    private readonly Dictionary<uint, uint> initialMarkers = [];
    private readonly Dictionary<uint, int> remainingStacks = [];
    private readonly HashSet<uint> playersSeenWithStacks = [];
    private readonly Dictionary<uint, uint> observedCasts = [];
    private int? lastObservedTowerRounds;
    private bool finalResolveCastSeen;
    private int activePhaseIndex;

    public bool IsForsakenPhase { get; private set; }

    public uint? LastLocalMarkerId { get; private set; }

    public uint? InitialLocalMarkerId { get; private set; }

    public int? LocalRemainingStacks { get; private set; }

    public int? CompletedTowerRounds { get; private set; }

    public string? LastResolve { get; private set; }

    public int ActivePhaseIndex => activePhaseIndex;

    public P2ForsakenTracker()
    {
        actionEffectHook = Plugin.GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive,
            OnReceiveActionEffect);

        try
        {
            actorControlHook = Plugin.GameInteropProvider.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
                PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket,
                OnActorControl);

            actionEffectHook.Enable();
            actorControlHook.Enable();
        }
        catch
        {
            actionEffectHook.Dispose();
            actorControlHook?.Dispose();
            throw;
        }

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

    /// <summary>Releases hooks and event subscriptions used by the tracker.</summary>
    public void Dispose()
    {
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        actionEffectHook.Dispose();
        actorControlHook.Dispose();
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
        var finalResolveCasting = false;
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
            if (!isNewCast)
            {
                if (CompletedTowerRounds == 8 && actionId is PastEndActionId or FutureEndActionId)
                {
                    finalResolveCasting = true;
                }

                continue;
            }

            switch (actionId)
            {
                case PastEndActionId:
                    LastResolve = "Past's End";
                    finalResolveCasting = CompletedTowerRounds == 8;
                    break;
                case FutureEndActionId:
                    LastResolve = "Future's End";
                    finalResolveCasting = CompletedTowerRounds == 8;
                    break;
                case AllThingsEndingActionId:
                case AllThingsEndingVariantActionId:
                    activePhaseIndex = 0;
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

        if (CompletedTowerRounds == 8 && finalResolveCasting)
        {
            finalResolveCastSeen = true;
        }
        else if (CompletedTowerRounds == 8 && finalResolveCastSeen)
        {
            ResetForsakenState();
        }
    }

    private void ResetForsakenState()
    {
        IsForsakenPhase = false;
        LastLocalMarkerId = null;
        InitialLocalMarkerId = null;
        LocalRemainingStacks = null;
        CompletedTowerRounds = null;
        LastResolve = null;
        lastObservedTowerRounds = null;
        finalResolveCastSeen = false;
        activePhaseIndex = 0;
        initialMarkers.Clear();
        remainingStacks.Clear();
        playersSeenWithStacks.Clear();
        observedCasts.Clear();
    }

    private void OnActorControl(
        uint entityId,
        uint category,
        uint arg1,
        uint arg2,
        uint arg3,
        uint arg4,
        uint arg5,
        uint arg6,
        uint arg7,
        uint arg8,
        GameObjectId targetId,
        bool isRecorded)
    {
        if (IsForsakenPhase && category == TargetIconCategory)
        {
            var targetEntityId = IsPartyMember(entityId)
                ? entityId
                : IsPartyMember(arg2) ? arg2 : 0;

            if (targetEntityId != 0)
            {
                initialMarkers.TryAdd(targetEntityId, arg1);

                if (Plugin.ObjectTable.LocalPlayer is { } localPlayer && targetEntityId == localPlayer.EntityId)
                {
                    LastLocalMarkerId = arg1;
                    InitialLocalMarkerId ??= arg1;
                }
            }
        }

        actorControlHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
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

    private void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        System.Numerics.Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        if (Plugin.ClientState.TerritoryType == SigmascapeV4TerritoryId
            && header is not null
            && header->ActionId == ForsakenActionId)
        {
            ResetForsakenState();
            IsForsakenPhase = true;
        }

        actionEffectHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }
}
