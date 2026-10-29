using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace SamplePlugin;

/// <summary>
/// Owns the game hooks shared by every mechanic tracker and republishes them as events, so each address
/// is hooked only once no matter how many mechanics are tracked.
/// </summary>
internal sealed unsafe class GameEventMonitor : IDisposable
{
    private const uint TargetIconCategory = 34;

    private readonly Hook<ActionEffectHandler.Delegates.Receive> actionEffectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> actorControlHook;

    public GameEventMonitor()
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
    }

    /// <summary>Raised with the caster entity ID and action ID whenever an action effect packet arrives.</summary>
    public event Action<uint, uint>? ActionEffectReceived;

    /// <summary>Raised once per target with the caster entity ID, action ID and target entity ID.</summary>
    public event Action<uint, uint, uint>? ActionEffectTargetReceived;

    /// <summary>Raised with the entity ID, marker ID and second argument whenever a head marker is shown.</summary>
    public event Action<uint, uint, uint>? TargetIconReceived;

    public void Dispose()
    {
        actionEffectHook.Dispose();
        actorControlHook.Dispose();
    }

    private void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        System.Numerics.Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        if (header is not null)
        {
            try
            {
                // Targets go first so a tracker that finishes on this action has already seen who was hit.
                if (targetEntityIds is not null)
                {
                    for (var i = 0; i < header->NumTargets; i++)
                    {
                        ActionEffectTargetReceived?.Invoke(casterEntityId, header->ActionId, targetEntityIds[i].ObjectId);
                    }
                }

                ActionEffectReceived?.Invoke(casterEntityId, header->ActionId);
            }
            catch (Exception ex)
            {
                // An exception escaping a game hook would take the game down with it.
                Plugin.Log.Error(ex, "Action effect handler failed.");
            }
        }

        actionEffectHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
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
        if (category == TargetIconCategory)
        {
            try
            {
                TargetIconReceived?.Invoke(entityId, arg1, arg2);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Target icon handler failed.");
            }
        }

        actorControlHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
    }
}
