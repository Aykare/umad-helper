using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;

namespace SamplePlugin;

/// <summary>
/// Tracks the P3 Limit Cut: the Kefka clones preview their attacks one by one (the first clone is the origin and the
/// second shows the rotation), then every player receives a numbered head marker.
/// </summary>
internal sealed class LimitCutTracker : IDisposable
{
    private const uint SigmascapeV4TerritoryId = 1363;
    private const uint PreviewActionId = 47843;
    private const uint ResolveActionId = 47844;
    private const int PartySize = 8;
    private const float MinimumCloneDistanceFromCenter = 10.0f;
    private const float MaximumCloneDistanceFromCenter = 35.0f;
    private const float MinimumDistanceBetweenClones = 2.0f;
    private const long MaximumRunMilliseconds = 90_000;

    // Head marker IDs observed identically in every logged pull: 336-339 are numbers 1-4, 437-440 are 5-8.
    private const uint FirstLowNumberMarkerId = 336;
    private const uint FirstHighNumberMarkerId = 437;
    private const int NumbersPerMarkerBlock = 4;

    private readonly GameEventMonitor monitor;
    private readonly RunHistory<LimitCutRun> history;
    private readonly HashSet<uint> previewCasters = [];
    private readonly HashSet<uint> resolveCasters = [];
    private LimitCutRun? currentRun;
    private Vector3? firstClonePosition;
    private long startTick;

    public LimitCutTracker(GameEventMonitor monitor, RunHistory<LimitCutRun> history)
    {
        this.monitor = monitor;
        this.history = history;

        monitor.ActionEffectReceived += OnActionEffect;
        monitor.ActionEffectTargetReceived += OnActionEffectTarget;
        monitor.TargetIconReceived += OnTargetIcon;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>True from the first clone preview until the attacks resolve.</summary>
    public bool IsActive => currentRun is not null;

    /// <summary>The run being recorded right now, or null outside Limit Cut.</summary>
    public LimitCutRun? CurrentRun => currentRun;

    /// <summary>The local player's limit cut number once it has been shown.</summary>
    public int? LocalNumber => currentRun?.Players.FirstOrDefault(player => player.IsLocalPlayer)?.Number;

    public void Dispose()
    {
        monitor.ActionEffectReceived -= OnActionEffect;
        monitor.ActionEffectTargetReceived -= OnActionEffectTarget;
        monitor.TargetIconReceived -= OnTargetIcon;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        FinishRun();
    }

    private void OnTerritoryChanged(uint _)
    {
        FinishRun();
    }

    private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework _)
    {
        if (currentRun is null)
        {
            return;
        }

        // A wipe leaves the territory unchanged, so leaving combat or a stalled run also ends the mechanic.
        if (!Plugin.Condition[ConditionFlag.InCombat] || Environment.TickCount64 - startTick > MaximumRunMilliseconds)
        {
            FinishRun();
        }
    }

    private void OnActionEffect(uint casterEntityId, uint actionId)
    {
        if (Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        switch (actionId)
        {
            case PreviewActionId:
                OnClonePreview(casterEntityId);
                break;
            case ResolveActionId when currentRun is not null:
                resolveCasters.Add(casterEntityId);
                if (resolveCasters.Count >= PartySize)
                {
                    FinishRun();
                }

                break;
        }
    }

    /// <summary>Remembers where the hit player stood at the moment their numbered attack resolved.</summary>
    private void OnActionEffectTarget(uint casterEntityId, uint actionId, uint targetEntityId)
    {
        if (actionId != ResolveActionId
            || currentRun is not { } run
            || Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == targetEntityId);
        if (player is null || player.ActualX is not null)
        {
            return;
        }

        if (Plugin.ObjectTable.SearchByEntityId(targetEntityId) is { } gameObject)
        {
            player.ActualX = gameObject.Position.X;
            player.ActualZ = gameObject.Position.Z;
        }

        // The clone aims its line at this player, so its facing at the hit is the line's direction.
        if (Plugin.ObjectTable.SearchByEntityId(casterEntityId) is { } clone)
        {
            player.CloneX = clone.Position.X;
            player.CloneZ = clone.Position.Z;
            player.AimFacing = LimitCutStrategy.FacingDegrees(clone.Rotation);
        }
    }

    /// <summary>The first clone gives the origin; the next clone at a different spot gives the rotation.</summary>
    private void OnClonePreview(uint casterEntityId)
    {
        if (previewCasters.Contains(casterEntityId) || !TryGetClonePosition(casterEntityId, out var position))
        {
            return;
        }

        previewCasters.Add(casterEntityId);
        if (currentRun is null)
        {
            currentRun = new LimitCutRun { StartedUtc = DateTime.UtcNow };
            startTick = Environment.TickCount64;
        }

        var angle = LimitCutStrategy.AngleFromNorth(position.X, position.Z);
        if (firstClonePosition is not { } first)
        {
            firstClonePosition = position;
            currentRun.OriginIndex = LimitCutStrategy.RingIndexFromAngle(angle);
            return;
        }

        if (currentRun.Rotation != LimitCutRotation.Unknown
            || Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(first.X, first.Z)) < MinimumDistanceBetweenClones)
        {
            return;
        }

        var delta = NormalizeDegrees(angle - LimitCutStrategy.AngleFromNorth(first.X, first.Z));
        currentRun.Rotation = delta switch
        {
            > 0.0f => LimitCutRotation.Clockwise,
            < 0.0f => LimitCutRotation.CounterClockwise,
            _ => LimitCutRotation.Unknown,
        };
    }

    private void OnTargetIcon(uint entityId, uint markerId, uint arg2)
    {
        if (currentRun is not { } run || ToLimitCutNumber(markerId) is not int number)
        {
            return;
        }

        var member = Plugin.PartyList.FirstOrDefault(candidate => candidate.EntityId == entityId)
            ?? Plugin.PartyList.FirstOrDefault(candidate => candidate.EntityId == arg2);
        if (member is null || run.Players.Any(player => player.EntityId == member.EntityId))
        {
            return;
        }

        run.Players.Add(new LimitCutPlayerRecord
        {
            EntityId = member.EntityId,
            Name = member.Name.TextValue,
            IsLocalPlayer = Plugin.ObjectTable.LocalPlayer?.EntityId == member.EntityId,
            Number = number,
            JobId = member.ClassJob.RowId,
        });
    }

    /// <summary>Stores the run in the persistent history and clears the live state.</summary>
    private void FinishRun()
    {
        var run = currentRun;
        currentRun = null;
        firstClonePosition = null;
        previewCasters.Clear();
        resolveCasters.Clear();

        if (run?.OriginIndex is not null)
        {
            history.Add(run);
        }
    }

    private static int? ToLimitCutNumber(uint markerId)
    {
        if (markerId >= FirstLowNumberMarkerId && markerId < FirstLowNumberMarkerId + NumbersPerMarkerBlock)
        {
            return (int)(markerId - FirstLowNumberMarkerId) + 1;
        }

        if (markerId >= FirstHighNumberMarkerId && markerId < FirstHighNumberMarkerId + NumbersPerMarkerBlock)
        {
            return (int)(markerId - FirstHighNumberMarkerId) + 1 + NumbersPerMarkerBlock;
        }

        return null;
    }

    private static bool TryGetClonePosition(uint casterEntityId, out Vector3 position)
    {
        position = default;
        var gameObject = Plugin.ObjectTable.SearchByEntityId(casterEntityId);
        if (gameObject is null)
        {
            return false;
        }

        position = gameObject.Position;
        var distance = MathF.Sqrt(
            MathF.Pow(position.X - 100.0f, 2) + MathF.Pow(position.Z - 100.0f, 2));
        return float.IsFinite(distance)
            && distance is >= MinimumCloneDistanceFromCenter and <= MaximumCloneDistanceFromCenter;
    }

    private static float NormalizeDegrees(float degrees)
    {
        while (degrees > 180.0f)
        {
            degrees -= 360.0f;
        }

        while (degrees < -180.0f)
        {
            degrees += 360.0f;
        }

        return degrees;
    }
}
