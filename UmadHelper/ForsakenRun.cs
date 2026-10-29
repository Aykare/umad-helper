using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin;

/// <summary>One marker a player received; hit 0 is the initial assignment, hit N follows tower N.</summary>
internal sealed class ForsakenMarkerEntry
{
    public int Hit { get; set; }

    public uint MarkerId { get; set; }
}

/// <summary>Where one party member stood when a snapshot was taken.</summary>
internal sealed class ForsakenSpot
{
    public uint EntityId { get; set; }

    public float X { get; set; }

    public float Z { get; set; }
}

/// <summary>A tower that went off and the players who were hit by it.</summary>
internal sealed class ForsakenTowerRecord
{
    public float X { get; set; }

    public float Z { get; set; }

    public List<uint> Soakers { get; set; } = [];
}

/// <summary>One person hit by a stack, spread, cone, clone bait or clone cone attack.</summary>
internal sealed class ForsakenAttackRecord
{
    public uint ActionId { get; set; }

    /// <summary>
    /// Where the attack came from: the cone's holder for a cone, or the clone that spawned from the baited player for
    /// a clone bait (unset for the boss's own bait and before the clone had spawned).
    /// </summary>
    public float? CasterX { get; set; }

    public float? CasterZ { get; set; }

    public uint TargetEntityId { get; set; }

    public float TargetX { get; set; }

    public float TargetZ { get; set; }
}

/// <summary>A Kefka (clone or the boss) that fired an All Things Ending cone, and the direction the cone pointed.</summary>
internal sealed class ForsakenCloneRecord
{
    public float X { get; set; }

    public float Z { get; set; }

    /// <summary>Cone direction in degrees clockwise from north.</summary>
    public float Facing { get; set; }
}

/// <summary>
/// Everyone's position at one point of the run. Round 0 is the start (before any tower), round N is the moment
/// tower N went off.
/// </summary>
internal sealed class ForsakenRound
{
    /// <summary>
    /// The number of the pseudo-round that holds the final All Things Ending bait after the eighth tower. It has no
    /// towers; its spots are everyone's positions when the clones started casting.
    /// </summary>
    public const int FinalBaitNumber = 9;

    public int Number { get; set; }

    public List<ForsakenSpot> Spots { get; set; } = [];

    public List<ForsakenTowerRecord> Towers { get; set; } = [];

    /// <summary>Stack, spread, cone and clone bait hits that resolved around this round's towers.</summary>
    public List<ForsakenAttackRecord> Attacks { get; set; } = [];

    /// <summary>All Things Ending casters; only odd rounds after the first have any.</summary>
    public List<ForsakenCloneRecord> Clones { get; set; } = [];

    /// <summary>
    /// Everyone's position when the clones' attacks landed (the bait circles before an even tower, or All Things
    /// Ending before an odd one). Empty for runs saved before it was recorded.
    /// </summary>
    public List<ForsakenSpot> CloneSpots { get; set; } = [];

    /// <summary>
    /// Everyone's position when the clones started casting All Things Ending, which is when they fix the direction of
    /// their half-room cleave. Only odd rounds after the first have it; empty for runs saved before it was recorded.
    /// </summary>
    public List<ForsakenSpot> FixSpots { get; set; } = [];

    /// <summary>
    /// Everyone's position when Spelldriver, Spellscatter and Spellwave landed. Empty for runs saved before it was
    /// recorded.
    /// </summary>
    public List<ForsakenSpot> AttackSpots { get; set; } = [];
}

/// <summary>The markers one party member received during a Forsaken run.</summary>
internal sealed class ForsakenPlayerRecord
{
    public uint EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsLocalPlayer { get; set; }

    public uint JobId { get; set; }

    /// <summary>The player's raid position when the run was saved; None for runs saved before positions existed.</summary>
    public RaidPosition Position { get; set; }

    public List<ForsakenMarkerEntry> Markers { get; set; } = [];

    /// <summary>Looks up the marker issued at the given hit, if this player received one.</summary>
    public bool TryGetMarker(int hit, out uint markerId)
    {
        foreach (var entry in Markers)
        {
            if (entry.Hit == hit)
            {
                markerId = entry.MarkerId;
                return true;
            }
        }

        markerId = 0;
        return false;
    }

    /// <summary>Gets the marker the player was holding after the given hit (the latest one issued at or before it).</summary>
    public bool TryGetMarkerHeldAt(int hit, out uint markerId)
    {
        var found = false;
        var latestHit = int.MinValue;
        markerId = 0;
        foreach (var entry in Markers)
        {
            if (entry.Hit <= hit && entry.Hit >= latestHit)
            {
                latestHit = entry.Hit;
                markerId = entry.MarkerId;
                found = true;
            }
        }

        return found;
    }
}

/// <summary>A single Forsaken attempt, kept so marker assignments can be reviewed after the pull.</summary>
internal sealed class ForsakenRun : IRecordedRun
{
    public DateTime StartedUtc { get; set; }

    public bool Completed { get; set; }

    public string? BuddyName { get; set; }

    public bool? LocalIsAlpha { get; set; }

    public List<ForsakenPlayerRecord> Players { get; set; } = [];

    /// <summary>Position snapshots with the towers; empty for runs saved before they were recorded.</summary>
    public List<ForsakenRound> Rounds { get; set; } = [];

    /// <summary>How many towers each player soaks before they are done and are issued no further marker.</summary>
    public const int SoaksPerPlayer = 4;

    /// <summary>
    /// Gets the marker a player holds during a tower round (the latest one issued before it). Players who already
    /// soaked all their towers get no new marker, so what they last received is stale and not returned.
    /// </summary>
    public bool TryGetMarkerDuring(ForsakenPlayerRecord player, int roundNumber, out uint markerId)
    {
        var soaked = Rounds.Count(round => round.Number < roundNumber
            && round.Towers.Any(tower => tower.Soakers.Contains(player.EntityId)));
        if (soaked >= SoaksPerPlayer)
        {
            markerId = 0;
            return false;
        }

        return player.TryGetMarkerHeldAt(Math.Max(0, roundNumber - 1), out markerId);
    }

    /// <summary>The boss's hitbox radius in yalms, read from the game; 0 when it was not recorded.</summary>
    public float BossHitboxRadius { get; set; }
}
