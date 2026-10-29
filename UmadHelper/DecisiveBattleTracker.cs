using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace SamplePlugin;

/// <summary>
/// Records every cast of "the Decisive Battle" (Chaos and Exdeath): who receives Epic Hero or Fated Hero, in which
/// order, and where the players and both bosses stand. Statuses are read from the party's status lists every frame.
/// </summary>
internal sealed class DecisiveBattleTracker : IDisposable
{
    private const uint SigmascapeV4TerritoryId = 1363;
    private const uint EpicHeroStatusId = 4192;
    private const uint FatedHeroStatusId = 4194;
    private const uint ChaosCastActionId = 49890;
    private const uint ExdeathCastActionId = 49891;
    private const string ChaosName = "Chaos";
    private const string ExdeathName = "Exdeath";

    // All statuses of one cast arrive within a frame or two; casts are far apart, so this separates them safely.
    private const long CastGapMilliseconds = 3_000;

    private readonly RunHistory<DecisiveBattleRun> history;
    private readonly HashSet<(uint Entity, uint Status)> heroStatuses = [];
    private readonly HashSet<(uint Entity, uint Status)> currentStatuses = [];
    private readonly HashSet<uint> chaosIds = [];
    private readonly HashSet<uint> exdeathIds = [];
    private DecisiveBattleRun? currentRun;
    private DecisiveBattleCastRecord? currentCast;
    private long runStartTick;
    private long lastGainTick;

    // Statuses that already exist when tracking starts (joining mid-fight) are not new gains.
    private bool primed;

    public DecisiveBattleTracker(RunHistory<DecisiveBattleRun> history)
    {
        this.history = history;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    /// <summary>The run being recorded right now, or null before the first cast of a pull.</summary>
    public DecisiveBattleRun? CurrentRun => currentRun;

    public void Dispose()
    {
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        FinishRun();
    }

    private void OnTerritoryChanged(uint _)
    {
        FinishRun();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (Plugin.ClientState.TerritoryType != SigmascapeV4TerritoryId)
        {
            return;
        }

        // A wipe leaves the territory unchanged, so leaving combat ends the pull.
        if (!Plugin.Condition[ConditionFlag.InCombat])
        {
            FinishRun();
            return;
        }

        RememberCastingBosses();
        PollParty();
        primed = true;
    }

    /// <summary>Remembers which entities cast the Decisive Battle, so the bosses are found even if their names differ.</summary>
    private void RememberCastingBosses()
    {
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara { IsCasting: true } chara)
            {
                continue;
            }

            switch (chara.CastActionId)
            {
                case ChaosCastActionId:
                    chaosIds.Add(chara.EntityId);
                    break;
                case ExdeathCastActionId:
                    exdeathIds.Add(chara.EntityId);
                    break;
            }
        }
    }

    private void PollParty()
    {
        currentStatuses.Clear();
        foreach (var member in Plugin.PartyList)
        {
            if (Plugin.ObjectTable.SearchByEntityId(member.EntityId) is not IBattleChara chara)
            {
                continue;
            }

            foreach (var status in chara.StatusList)
            {
                var hero = status.StatusId switch
                {
                    EpicHeroStatusId => DecisiveBattleHero.Epic,
                    FatedHeroStatusId => DecisiveBattleHero.Fated,
                    _ => (DecisiveBattleHero?)null,
                };

                if (hero is null)
                {
                    continue;
                }

                var key = (member.EntityId, status.StatusId);
                currentStatuses.Add(key);
                if (primed && !heroStatuses.Contains(key))
                {
                    RecordGain(member, chara, hero.Value);
                }
            }
        }

        heroStatuses.Clear();
        heroStatuses.UnionWith(currentStatuses);
    }

    private void RecordGain(Dalamud.Game.ClientState.Party.IPartyMember member, IBattleChara chara, DecisiveBattleHero hero)
    {
        var now = Environment.TickCount64;
        if (currentRun is null)
        {
            currentRun = new DecisiveBattleRun { StartedUtc = DateTime.UtcNow };
            runStartTick = now;
        }

        if (currentCast is null || now - lastGainTick > CastGapMilliseconds)
        {
            currentCast = new DecisiveBattleCastRecord { Seconds = (now - runStartTick) / 1000.0f };
            if (FindBoss(chaosIds, ChaosName) is { } chaos)
            {
                currentCast.ChaosX = chaos.Position.X;
                currentCast.ChaosZ = chaos.Position.Z;
            }

            if (FindBoss(exdeathIds, ExdeathName) is { } exdeath)
            {
                currentCast.ExdeathX = exdeath.Position.X;
                currentCast.ExdeathZ = exdeath.Position.Z;
            }

            currentRun.Casts.Add(currentCast);
        }

        lastGainTick = now;
        currentCast.Players.Add(new DecisiveBattlePlayerRecord
        {
            EntityId = member.EntityId,
            Name = member.Name.TextValue,
            IsLocalPlayer = Plugin.ObjectTable.LocalPlayer?.EntityId == member.EntityId,
            JobId = member.ClassJob.RowId,
            Hero = hero,
            X = chara.Position.X,
            Z = chara.Position.Z,
        });
    }

    /// <summary>Finds a boss by the entity that was seen casting, falling back to its name ("Neo Exdeath" also matches).</summary>
    private static IGameObject? FindBoss(HashSet<uint> castingIds, string name)
    {
        foreach (var entityId in castingIds)
        {
            if (Plugin.ObjectTable.SearchByEntityId(entityId) is { } known)
            {
                return known;
            }
        }

        return Plugin.ObjectTable.FirstOrDefault(gameObject =>
            gameObject is IBattleChara && gameObject.Name.TextValue.EndsWith(name, StringComparison.Ordinal));
    }

    /// <summary>Stores the pull in the persistent history and clears the live state.</summary>
    private void FinishRun()
    {
        var run = currentRun;
        currentRun = null;
        currentCast = null;
        primed = false;
        heroStatuses.Clear();
        chaosIds.Clear();
        exdeathIds.Clear();

        if (run is not null && run.Casts.Count > 0)
        {
            history.Add(run);
        }
    }
}
