using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace SamplePlugin;

/// <summary>
/// Tracks P4 Kefka Says: the Grand Cross, Chaos and Flood debuffs on the party, the real/fake tells of the bosses
/// and the order in which everything resolves. Debuffs are read from the party's status lists every frame, because
/// the game sends no event when a status is gained or lost.
/// </summary>
internal sealed class KefkaSaysTracker : IDisposable
{
    private const uint SigmascapeV4TerritoryId = 1363;
    private const uint ManaReleaseActionId = 47781;
    private const uint WhiteAntilightActionId = 50068;
    private const uint BlackAntilightActionId = 50069;
    private const uint FirstKefkaMarkerId = 673;
    private const uint LastKefkaMarkerId = 678;
    private const int PartySize = 8;

    // A status counts as expired (rather than removed early, for example by death) if it vanished this close to its end.
    private const float ExpiryToleranceSeconds = 3.0f;
    private const long TellFreshnessMilliseconds = 25_000;
    private const long SignalMemoryMilliseconds = 60_000;
    private const long ChargeElementTimeoutMilliseconds = 10_000;
    private const float ChargeMarkerLeadSeconds = 3.0f;
    private const float MaximumRunSeconds = 200.0f;
    private const float SecondsToKeepAfterRelease = 10.0f;

    private readonly GameEventMonitor monitor;
    private readonly RunHistory<KefkaSaysRun> history;
    private readonly Dictionary<(uint Entity, uint Status), TrackedDebuff> tracked = [];
    private readonly Dictionary<string, (KefkaReality Reality, long Tick)> latestTells = [];
    private readonly Dictionary<string, int> lastTellParams = [];
    private readonly List<(string Source, int Value, long Tick)> earlySignals = [];
    private readonly HashSet<uint> chargeStatusesLastFrame = [];
    private KefkaSaysRun? currentRun;
    private long startTick;
    private int chargeCount;
    private KefkaStepRecord? pendingCharge;
    private string? pendingChargeElement;
    private float pendingChargeSeconds;
    private long pendingChargeTick;

    public KefkaSaysTracker(GameEventMonitor monitor, RunHistory<KefkaSaysRun> history)
    {
        this.monitor = monitor;
        this.history = history;

        monitor.ActionEffectReceived += OnActionEffect;
        monitor.ActionEffectTargetReceived += OnActionEffectTarget;
        monitor.TargetIconReceived += OnTargetIcon;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>True from the first Grand Cross debuff until the mechanic is over.</summary>
    public bool IsActive => currentRun is not null;

    /// <summary>The run being recorded right now, or null outside Kefka Says.</summary>
    public KefkaSaysRun? CurrentRun => currentRun;

    /// <summary>Seconds since the first Grand Cross debuff was handed out; debuff expiry times use the same clock.</summary>
    public float CurrentSeconds => currentRun is null ? 0.0f : (Environment.TickCount64 - startTick) / 1000.0f;

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
        earlySignals.Clear();
        latestTells.Clear();
        lastTellParams.Clear();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        // A wipe leaves the territory unchanged, so leaving combat also ends the mechanic.
        if (!Plugin.Condition[ConditionFlag.InCombat])
        {
            FinishRun();
            earlySignals.Clear();
            return;
        }

        PollBossTells();
        PollParty();
        PollKefkaCharge();
        FinishIfOver();
    }

    private void OnTargetIcon(uint entityId, uint markerId, uint arg2)
    {
        if (markerId is < FirstKefkaMarkerId or > LastKefkaMarkerId
            || Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId
            || Plugin.ObjectTable.SearchByEntityId(entityId)?.Name.TextValue != KefkaSaysRules.KefkaName)
        {
            return;
        }

        AddSignal(KefkaSaysRules.KefkaName, (int)markerId);
    }

    private void OnActionEffect(uint casterEntityId, uint actionId)
    {
        if (currentRun is null || Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        switch (actionId)
        {
            case ManaReleaseActionId:
                CreateStepOnce(KefkaStep.ManaRelease);
                break;
        }
    }

    /// <summary>Remembers which Antilight colour actually hit each player, so a wrong half shows up in the history.</summary>
    private void OnActionEffectTarget(uint casterEntityId, uint actionId, uint targetEntityId)
    {
        if (currentRun is null
            || actionId is not (WhiteAntilightActionId or BlackAntilightActionId)
            || Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        var step = CreateStepOnce(KefkaStep.FloodOfNaught);
        var colour = actionId == WhiteAntilightActionId ? nameof(KefkaWound.White) : nameof(KefkaWound.Black);
        if (step.Players.FirstOrDefault(player => player.EntityId == targetEntityId) is { } hit)
        {
            hit.Actual = colour;
        }

        // Each colour is one attack from one caster; its position fixes which half of the arena it covers.
        if (step.Areas.All(area => area.Colour != colour)
            && (Plugin.ObjectTable.SearchById(casterEntityId) ?? Plugin.ObjectTable.SearchByEntityId(casterEntityId)) is { } caster)
        {
            step.Areas.Add(new KefkaAreaRecord { Colour = colour, CasterX = caster.Position.X, CasterZ = caster.Position.Z });
        }
    }

    /// <summary>Notes the Real/Fake tells on Neo Exdeath and Chaos; they appear shortly before the debuffs they decide.</summary>
    private void PollBossTells()
    {
        var present = new HashSet<string>();
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara boss
                || boss.Name.TextValue is not (KefkaSaysRules.NeoExdeathName or KefkaSaysRules.ChaosName))
            {
                continue;
            }

            var source = boss.Name.TextValue;
            foreach (var status in boss.StatusList)
            {
                if (status.StatusId != KefkaSaysRules.BossTellStatusId)
                {
                    continue;
                }

                present.Add(source);
                if (!lastTellParams.TryGetValue(source, out var last) || last != status.Param)
                {
                    lastTellParams[source] = status.Param;
                    AddSignal(source, status.Param);
                }

                break;
            }
        }

        // A tell that disappeared must be reported again when the next one shows up with the same value.
        foreach (var source in lastTellParams.Keys.Where(key => !present.Contains(key)).ToList())
        {
            lastTellParams.Remove(source);
        }
    }

    private void PollParty()
    {
        foreach (var member in Plugin.PartyList)
        {
            if (Plugin.ObjectTable.SearchByEntityId(member.EntityId) is not IBattleChara chara)
            {
                continue;
            }

            var seen = new HashSet<uint>();
            foreach (var status in chara.StatusList)
            {
                if (status.StatusId == 0)
                {
                    continue;
                }

                if (KefkaSaysRules.WoundOf(status.StatusId) is var wound && wound != KefkaWound.None)
                {
                    if (currentRun is not null)
                    {
                        // Only the first wound counts: the Antilight hit later swaps it, which must not rewrite what was handed out.
                        var woundPlayer = GetPlayer(member.EntityId);
                        if (woundPlayer.Wound == KefkaWound.None)
                        {
                            woundPlayer.Wound = wound;
                            woundPlayer.WoundStatusId = status.StatusId;
                        }
                    }

                    continue;
                }

                if (KefkaSaysRules.KindOf(status.StatusId) is not { } kind)
                {
                    continue;
                }

                // Only a Grand Cross debuff can start a run, so a stray status left over from earlier cannot.
                if (currentRun is null && !IsGrandCrossDebuff(kind))
                {
                    continue;
                }

                seen.Add(status.StatusId);
                TrackDebuff(member.EntityId, status.StatusId, kind, status.RemainingTime);
            }

            ReportExpiredDebuffs(member.EntityId, seen);
        }
    }

    private void TrackDebuff(uint entityId, uint statusId, KefkaDebuffKind kind, float remaining)
    {
        if (currentRun is null)
        {
            StartRun();
        }

        var run = currentRun!;
        if (!tracked.TryGetValue((entityId, statusId), out var tracking))
        {
            var expiresAt = CurrentSeconds + remaining;
            var isLong = IsGrandCrossDebuff(kind) && expiresAt >= KefkaSaysRules.LongTierThresholdSeconds;
            var debuff = new KefkaDebuffRecord
            {
                StatusId = statusId,
                Kind = kind,
                IsLong = isLong,
                ExpiresAtSeconds = expiresAt,
                Reality = TellReality(kind),
            };

            GetPlayer(entityId).Debuffs.Add(debuff);
            tracking = new TrackedDebuff(debuff, remaining);
            tracked[(entityId, statusId)] = tracking;
        }

        tracking.LastRemaining = remaining;

        // The tell can arrive a moment after the debuff, so a value that was unknown is filled in once it is seen.
        if (tracking.Debuff.Reality == KefkaReality.Unknown)
        {
            tracking.Debuff.Reality = TellReality(tracking.Debuff.Kind);
        }
    }

    /// <summary>Finds tracked debuffs of one player that are no longer on them and treats the ones that ran out as resolved.</summary>
    private void ReportExpiredDebuffs(uint entityId, HashSet<uint> seen)
    {
        foreach (var key in tracked.Keys.Where(key => key.Entity == entityId && !seen.Contains(key.Status)).ToList())
        {
            var tracking = tracked[key];
            tracked.Remove(key);
            if (tracking.LastRemaining > ExpiryToleranceSeconds
                || tracking.Debuff.Kind is KefkaDebuffKind.AllaganField or KefkaDebuffKind.BeyondDeath)
            {
                continue;
            }

            CreateStepOnce(KefkaSaysRules.StepOf(tracking.Debuff.Kind, tracking.Debuff.IsLong));
        }
    }

    /// <summary>
    /// Waits for Kefka to gain a Charged status (thunder with the first Mana Charge, ice with the second) and banks the
    /// real/fake value of the Kefka marker that comes with it.
    /// </summary>
    private void PollKefkaCharge()
    {
        if (currentRun is null)
        {
            chargeStatusesLastFrame.Clear();
            return;
        }

        var current = new HashSet<uint>();
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara kefka || kefka.Name.TextValue != KefkaSaysRules.KefkaName)
            {
                continue;
            }

            foreach (var status in kefka.StatusList)
            {
                if (status.StatusId is KefkaSaysRules.ThunderChargedStatusId or KefkaSaysRules.IceChargedStatusId)
                {
                    current.Add(status.StatusId);
                }
            }
        }

        // Comparing with the previous frame keeps a charge status left over from the first Mana Charge from counting again.
        var gained = current.Except(chargeStatusesLastFrame).ToList();
        chargeStatusesLastFrame.Clear();
        chargeStatusesLastFrame.UnionWith(current);

        if (pendingCharge is null)
        {
            if (gained.Count == 0 || chargeCount >= 2)
            {
                return;
            }

            chargeCount++;
            pendingCharge = CreateStep(chargeCount == 1 ? KefkaStep.ManaCharge1 : KefkaStep.ManaCharge2);
            pendingChargeElement = gained[0] == KefkaSaysRules.ThunderChargedStatusId
                ? KefkaSaysRules.LightningElement
                : KefkaSaysRules.IceElement;
            pendingChargeSeconds = CurrentSeconds;
            pendingChargeTick = Environment.TickCount64;
        }

        // The marker shows up within a frame or two of the status, so it may already be in the signal list.
        var marker = currentRun.Signals.LastOrDefault(signal =>
            signal.Source == KefkaSaysRules.KefkaName
            && signal.Seconds >= pendingChargeSeconds - ChargeMarkerLeadSeconds
            && KefkaSaysRules.ManaMarker(signal.Value) is { } value
            && value.Element == pendingChargeElement);
        if (marker is not null)
        {
            BankCharge(KefkaSaysRules.ManaMarker(marker.Value)!.Value.Reality, marker.Seconds);
        }
        else if (Environment.TickCount64 - pendingChargeTick > ChargeElementTimeoutMilliseconds)
        {
            BankCharge(KefkaReality.Unknown, pendingChargeSeconds);
        }
    }

    /// <summary>Grand Cross debuffs are handed out in two waves and split into a short and a long half.</summary>
    private static bool IsGrandCrossDebuff(KefkaDebuffKind kind)
    {
        return kind is KefkaDebuffKind.CompressedWater or KefkaDebuffKind.ForkedLightning
            or KefkaDebuffKind.CursedShriek or KefkaDebuffKind.AccelerationBomb;
    }

    /// <summary>Stores the value Kefka's marker gave the element that was just charged, to be added again by Mana Release.</summary>
    private void BankCharge(KefkaReality reality, float seconds)
    {
        var run = currentRun!;
        run.ManaBanks.Add(new KefkaManaBankRecord { Element = pendingChargeElement!, Reality = reality, Seconds = seconds });
        pendingCharge!.Result = KefkaSaysRules.ResultFor(run, pendingCharge.Step);
        pendingCharge = null;
        pendingChargeElement = null;
    }

    /// <summary>The newest tell of the boss that decides this debuff, or unknown if none was seen recently.</summary>
    private KefkaReality TellReality(KefkaDebuffKind kind)
    {
        var source = KefkaSaysRules.TellSourceOf(kind);
        return source is not null
            && latestTells.TryGetValue(source, out var tell)
            && Environment.TickCount64 - tell.Tick <= TellFreshnessMilliseconds
                ? tell.Reality
                : KefkaReality.Unknown;
    }

    private void AddSignal(string source, int value)
    {
        var now = Environment.TickCount64;
        var reality = KefkaSaysRules.RealityOfTell(value);
        if (reality != KefkaReality.Unknown)
        {
            latestTells[source] = (reality, now);
        }

        if (currentRun is null)
        {
            earlySignals.RemoveAll(signal => now - signal.Tick > SignalMemoryMilliseconds);
            earlySignals.Add((source, value, now));
            return;
        }

        currentRun.Signals.Add(new KefkaSignalRecord { Source = source, Value = value, Seconds = (now - startTick) / 1000.0f });
    }

    /// <summary>Starts recording and adds every party member, so players without a debuff yet still show up.</summary>
    private void StartRun()
    {
        startTick = Environment.TickCount64;
        currentRun = new KefkaSaysRun { StartedUtc = DateTime.UtcNow };
        chargeCount = 0;
        pendingCharge = null;

        foreach (var member in Plugin.PartyList.Take(PartySize))
        {
            GetPlayer(member.EntityId);
        }

        // The tells and Kefka's markers come before the first debuff, so they are copied in with a negative time.
        foreach (var (source, value, tick) in earlySignals.Where(signal => startTick - signal.Tick <= SignalMemoryMilliseconds))
        {
            currentRun.Signals.Add(new KefkaSignalRecord { Source = source, Value = value, Seconds = (tick - startTick) / 1000.0f });
        }

        earlySignals.Clear();
    }

    private KefkaPlayerRecord GetPlayer(uint entityId)
    {
        var run = currentRun!;
        if (run.Players.FirstOrDefault(player => player.EntityId == entityId) is { } existing)
        {
            return existing;
        }

        var member = Plugin.PartyList.FirstOrDefault(candidate => candidate.EntityId == entityId);
        var player = new KefkaPlayerRecord
        {
            EntityId = entityId,
            Name = member?.Name.TextValue ?? entityId.ToString(),
            IsLocalPlayer = Plugin.ObjectTable.LocalPlayer?.EntityId == entityId,
            JobId = member?.ClassJob.RowId ?? 0,
        };
        run.Players.Add(player);
        return player;
    }

    private KefkaStepRecord CreateStepOnce(KefkaStep step)
    {
        return currentRun!.Steps.FirstOrDefault(existing => existing.Step == step) ?? CreateStep(step);
    }

    /// <summary>Records a resolved step with everybody's position and what each player was meant to do.</summary>
    private KefkaStepRecord CreateStep(KefkaStep step)
    {
        var run = currentRun!;
        var record = new KefkaStepRecord { Step = step, Seconds = CurrentSeconds };
        foreach (var player in run.Players)
        {
            var label = KefkaSaysRules.LabelFor(run, player, step, out var involved);
            var stepPlayer = new KefkaStepPlayer { EntityId = player.EntityId, Label = label, Involved = involved };
            if (Plugin.ObjectTable.SearchByEntityId(player.EntityId) is { } gameObject)
            {
                stepPlayer.X = gameObject.Position.X;
                stepPlayer.Z = gameObject.Position.Z;
            }

            record.Players.Add(stepPlayer);
        }

        record.Result = KefkaSaysRules.ResultFor(run, step);
        run.Steps.Add(record);
        return record;
    }

    private void FinishIfOver()
    {
        if (currentRun is not { } run)
        {
            return;
        }

        var release = run.Steps.FirstOrDefault(step => step.Step == KefkaStep.ManaRelease);
        if (CurrentSeconds > MaximumRunSeconds || (release is not null && CurrentSeconds - release.Seconds > SecondsToKeepAfterRelease))
        {
            FinishRun();
        }
    }

    /// <summary>Stores the run in the persistent history and clears the live state.</summary>
    private void FinishRun()
    {
        var run = currentRun;
        currentRun = null;
        tracked.Clear();
        chargeStatusesLastFrame.Clear();
        pendingCharge = null;
        chargeCount = 0;

        if (run is not null && run.Players.Any(player => player.Debuffs.Count > 0))
        {
            history.Add(run);
        }
    }

    private sealed class TrackedDebuff(KefkaDebuffRecord debuff, float lastRemaining)
    {
        public KefkaDebuffRecord Debuff { get; } = debuff;

        public float LastRemaining { get; set; } = lastRemaining;
    }
}
