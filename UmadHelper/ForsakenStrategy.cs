using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SamplePlugin;

/// <summary>Where one player should have stood in a tower round, compared with where they actually were.</summary>
/// <param name="EntityId">The player.</param>
/// <param name="Expected">The strategy's spot, in world coordinates.</param>
/// <param name="Actual">Where the player stood when the round's towers went off.</param>
/// <param name="Distance">Yalms between the two.</param>
/// <param name="Tolerance">How far off a player may be before it counts as a mistake.</param>
/// <param name="Slot">A short description of the spot, for display.</param>
/// <param name="Bait">True for the Past's End / Future's End bait check, false for a tower-time position.</param>
internal readonly record struct ForsakenExpectation(
    uint EntityId,
    Vector2 Expected,
    Vector2 Actual,
    float Distance,
    float Tolerance,
    string Slot,
    bool Bait = false)
{
    /// <summary>True when the player stood further from their spot than the tolerance allows.</summary>
    public bool Failed => Distance > Tolerance;
}

/// <summary>
/// The Forsaken tower positions. Every set is mirrored and rotated around the arena, so spots are stored in a frame
/// that is derived from the round's towers: x points from the arena center to the left tower (odd rounds) or to the
/// middle of the two towers (even rounds), and z is 90 degrees clockwise from it. The left tower is the one whose
/// partner lies 90 degrees counter-clockwise from it; supports take it.
/// The spot values are the averages of what 28 logged runs did (all eight raid positions, every set), with the few
/// obvious mistakes left out, so they are what the group actually settled on rather than a guess at the strategy.
/// </summary>
internal static class ForsakenStrategy
{
    private const float ArenaCenter = 100.0f;
    private const float TowerAngleToleranceDegrees = 10.0f;

    // Logged players stayed within about 1 yalm of these spots, except for spread, which varies by a few yalms.
    private const float FreeTolerance = 2.5f;
    private const float SoakTolerance = 3.0f;
    private const float SpreadTolerance = 4.5f;

    // Players baited about 8 yalms from the center; the snapshot can catch them still walking out, so the minimum
    // radius is only used to place the expected spot, and the tolerance is wide.
    private const float MaxMeleeRadius = 8.5f;
    private const float BaitTolerance = 4.5f;

    // The Past's End / Future's End bait circles come in two action ids each (47830-47833).
    private const uint FutureBaitActionIdA = 47830;
    private const uint PastBaitActionIdA = 47831;
    private const uint FutureBaitActionIdB = 47832;
    private const uint PastBaitActionIdB = 47833;

    private readonly record struct Spot(float X, float Z, float Tolerance, string Text);

    private enum Role
    {
        Tank,
        Healer,
        Melee,
        Ranged,
    }

    /// <summary>
    /// Compares every player's position when the round's towers went off with their strategy spot. Players whose
    /// spot cannot be told (no tower pair, no marker for a soaker, no raid position for a non-soaker) are left out.
    /// </summary>
    /// <param name="run">The run, for player markers.</param>
    /// <param name="round">The tower round to evaluate.</param>
    /// <param name="positionOf">Resolves a player's raid position; used for players who did not soak a tower.</param>
    public static List<ForsakenExpectation> Evaluate(ForsakenRun run, ForsakenRound round, Func<ForsakenPlayerRecord, RaidPosition> positionOf)
    {
        var result = EvaluateBait(run, round);
        if (round.Number <= 0 || !TryGetFrame(round, out var rotation, out var leftIndex))
        {
            return result;
        }

        var odd = round.Number % 2 == 1;
        var groups = BuildGroups(run);
        var expectedGroup = ExpectedGroup(round.Number);

        // The markers each tower is still missing, so a player who missed their tower can be matched to the one they belonged in.
        var missing = round.Towers.Select((tower, index) => RequiredMarkers(odd, index == leftIndex)).ToList();
        for (var index = 0; index < round.Towers.Count; index++)
        {
            foreach (var soaker in round.Towers[index].Soakers)
            {
                var soakerPlayer = run.Players.Find(candidate => candidate.EntityId == soaker);
                if (soakerPlayer is not null && soakerPlayer.TryGetMarkerHeldAt(round.Number - 1, out var soakerMarker))
                {
                    missing[index].Remove(soakerMarker);
                }
            }
        }

        foreach (var actual in round.Spots)
        {
            var player = run.Players.Find(candidate => candidate.EntityId == actual.EntityId);
            if (player is null)
            {
                continue;
            }

            var towerIndex = round.Towers.FindIndex(tower => tower.Soakers.Contains(player.EntityId));
            Spot? spot;
            if (towerIndex >= 0)
            {
                spot = player.TryGetMarkerHeldAt(round.Number - 1, out var markerId)
                    ? SoakSpot(odd, towerIndex == leftIndex, markerId)
                    : null;
            }
            else if (!groups.TryGetValue(player.EntityId, out var group))
            {
                // Without knowing which group the player belongs to, neither a soak nor a free spot can be assumed.
                spot = null;
            }
            else if (group == expectedGroup)
            {
                // The player should have been in a tower but was in none: judge them against the tower they missed.
                spot = player.TryGetMarkerHeldAt(round.Number - 1, out var missedMarker)
                    ? MissedSoakSpot(round, missing, leftIndex, odd, missedMarker, new Vector2(actual.X, actual.Z))
                    : null;
            }
            else
            {
                spot = FreeSpot(odd, positionOf(player));
            }

            if (spot is not { } found)
            {
                continue;
            }

            var expected = ToWorld(found.X, found.Z, rotation);
            var position = new Vector2(actual.X, actual.Z);
            result.Add(new ForsakenExpectation(player.EntityId, expected, position, Vector2.Distance(expected, position), found.Tolerance, found.Text));
        }

        return result;
    }

    /// <summary>Counts the position mistakes in one round.</summary>
    public static int CountMistakes(ForsakenRun run, ForsakenRound round, Func<ForsakenPlayerRecord, RaidPosition> positionOf)
        => Evaluate(run, round, positionOf).FindAll(expectation => expectation.Failed).Count;

    /// <summary>
    /// Checks where everyone stood when the clones started casting All Things Ending, which is where the Past's End
    /// or Future's End bait of the previous even set put their clones. Everyone baits at max melee: for Past between
    /// the coming odd towers, for Future on the opposite side. The final cast after the eighth tower is always baited
    /// toward the waymark that was between the eighth set's towers. Only odd rounds from the third on have the snapshot.
    /// </summary>
    private static List<ForsakenExpectation> EvaluateBait(ForsakenRun run, ForsakenRound round)
    {
        var result = new List<ForsakenExpectation>();
        var isFinal = round.Number == ForsakenRound.FinalBaitNumber;
        if (round.FixSpots.Count == 0 || !(isFinal || (round.Number >= 3 && round.Number % 2 == 1)))
        {
            return result;
        }

        var previous = run.Rounds.Find(candidate => candidate.Number == round.Number - 1);
        if (previous is null || !TryGetBaitKind(previous, out var past) || !TryGetMidAngle(isFinal ? previous : round, out var midAngle))
        {
            return result;
        }

        var facing = midAngle;
        var waymarkLabel = string.Empty;
        if (isFinal)
        {
            var best = 0;
            for (var index = 1; index < LimitCutStrategy.WaymarkPositions.Length; index++)
            {
                if (AngleDistance(WaymarkAngle(index), midAngle) < AngleDistance(WaymarkAngle(best), midAngle))
                {
                    best = index;
                }
            }

            // Logged players baited about 7.5 yalms out along the waymark's direction, so the spot is that ray.
            facing = WaymarkAngle(best);
            waymarkLabel = LimitCutStrategy.RingLabels[best];
            past = true;
        }

        var slot = isFinal
            ? $"Final bait: toward waymark {waymarkLabel} (it was between the set 8 towers), at least max melee"
            : past
                ? "Past's End bait: between the coming towers, at least max melee"
                : "Future's End bait: on the side away from the coming towers, at least max melee";
        var direction = (past ? facing : facing + 180.0f) * (MathF.PI / 180.0f);
        foreach (var spot in round.FixSpots)
        {
            var actual = new Vector2(spot.X, spot.Z);
            var radius = MathF.Max(Vector2.Distance(actual, new Vector2(ArenaCenter)), MaxMeleeRadius);
            var expected = new Vector2(ArenaCenter + (radius * MathF.Cos(direction)), ArenaCenter + (radius * MathF.Sin(direction)));
            result.Add(new ForsakenExpectation(spot.EntityId, expected, actual, Vector2.Distance(expected, actual), BaitTolerance, slot, true));
        }

        return result;
    }

    /// <summary>Tells from the round's Past's End / Future's End bait circles which of the two was cast.</summary>
    private static bool TryGetBaitKind(ForsakenRound round, out bool past)
    {
        past = false;
        foreach (var attack in round.Attacks)
        {
            if (attack.ActionId is PastBaitActionIdA or PastBaitActionIdB)
            {
                past = true;
                return true;
            }

            if (attack.ActionId is FutureBaitActionIdA or FutureBaitActionIdB)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The direction (degrees clockwise from east) from the arena center to the middle of the round's two towers.</summary>
    private static bool TryGetMidAngle(ForsakenRound round, out float angle)
    {
        angle = 0.0f;
        if (round.Towers.Count != 2)
        {
            return false;
        }

        var sin = 0.0f;
        var cos = 0.0f;
        foreach (var tower in round.Towers)
        {
            var radians = AngleOf(tower) * (MathF.PI / 180.0f);
            sin += MathF.Sin(radians);
            cos += MathF.Cos(radians);
        }

        angle = MathF.Atan2(sin, cos) * (180.0f / MathF.PI);
        return true;
    }

    private static float WaymarkAngle(int index)
        => MathF.Atan2(LimitCutStrategy.WaymarkPositions[index].Z - ArenaCenter, LimitCutStrategy.WaymarkPositions[index].X - ArenaCenter) * (180.0f / MathF.PI);

    private static float AngleDistance(float first, float second)
        => Math.Abs(WrapDegrees(first - second));

    /// <summary>Finds the frame rotation (degrees, clockwise from east) and which of the two towers is the left one.</summary>
    private static bool TryGetFrame(ForsakenRound round, out float rotation, out int leftIndex)
    {
        rotation = 0.0f;
        leftIndex = -1;
        if (round.Towers.Count != 2)
        {
            return false;
        }

        var first = AngleOf(round.Towers[0]);
        var second = AngleOf(round.Towers[1]);
        var difference = WrapDegrees(second - first);
        if (Math.Abs(difference + 90.0f) <= TowerAngleToleranceDegrees)
        {
            leftIndex = 0;
        }
        else if (Math.Abs(difference - 90.0f) <= TowerAngleToleranceDegrees)
        {
            leftIndex = 1;
        }
        else
        {
            return false;
        }

        var leftAngle = leftIndex == 0 ? first : second;
        rotation = round.Number % 2 == 1 ? leftAngle : leftAngle - 45.0f;
        return true;
    }

    private static float AngleOf(ForsakenTowerRecord tower)
        => MathF.Atan2(tower.Z - ArenaCenter, tower.X - ArenaCenter) * (180.0f / MathF.PI);

    private static float WrapDegrees(float degrees)
    {
        degrees %= 360.0f;
        return degrees switch
        {
            > 180.0f => degrees - 360.0f,
            <= -180.0f => degrees + 360.0f,
            _ => degrees,
        };
    }

    /// <summary>Rotates a frame-relative offset back into world coordinates.</summary>
    private static Vector2 ToWorld(float x, float z, float rotationDegrees)
    {
        var angle = rotationDegrees * (MathF.PI / 180.0f);
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        return new Vector2(ArenaCenter + (x * cos) - (z * sin), ArenaCenter + (x * sin) + (z * cos));
    }

    /// <summary>
    /// Which half of the raid soaks a round: one group of four takes sets 1 to 3 and 8, the other takes sets 4 to 7.
    /// </summary>
    private static int ExpectedGroup(int roundNumber)
        => roundNumber is <= 3 or 8 ? 0 : 1;

    /// <summary>
    /// Sorts the players into the two soak groups from who actually soaked. A player who never soaked is put in the
    /// other group once one group is full; if that cannot be told the player is left out.
    /// </summary>
    private static Dictionary<uint, int> BuildGroups(ForsakenRun run)
    {
        var groups = new Dictionary<uint, int>();
        foreach (var round in run.Rounds)
        {
            if (round.Number <= 0 || round.Number > 8)
            {
                continue;
            }

            foreach (var soaker in round.Towers.SelectMany(tower => tower.Soakers))
            {
                groups.TryAdd(soaker, ExpectedGroup(round.Number));
            }
        }

        const int groupSize = 4;
        foreach (var known in new[] { 0, 1 })
        {
            if (groups.Values.Count(group => group == known) != groupSize)
            {
                continue;
            }

            foreach (var player in run.Players.Where(candidate => !groups.ContainsKey(candidate.EntityId)))
            {
                groups[player.EntityId] = 1 - known;
            }
        }

        return groups;
    }

    /// <summary>The markers the two players in a tower hold: stack and cone on the left odd tower, stack and spread on the right one, cone and spread on even towers.</summary>
    private static List<uint> RequiredMarkers(bool odd, bool left)
    {
        const uint stack = P2ForsakenTracker.StackMarkerId;
        const uint spread = P2ForsakenTracker.SpreadMarkerId;
        const uint cone = P2ForsakenTracker.ConeMarkerId;
        return (odd, left) switch
        {
            (true, true) => [stack, cone],
            (true, false) => [stack, spread],
            _ => [cone, spread],
        };
    }

    /// <summary>
    /// The spot of a player who was in no tower although they had to soak: the tower that still lacked their marker
    /// (the nearest of them when both do). Null when no tower lacks it.
    /// </summary>
    private static Spot? MissedSoakSpot(ForsakenRound round, List<List<uint>> missing, int leftIndex, bool odd, uint markerId, Vector2 actual)
    {
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < round.Towers.Count; index++)
        {
            if (!missing[index].Contains(markerId))
            {
                continue;
            }

            var distance = Vector2.Distance(actual, new Vector2(round.Towers[index].X, round.Towers[index].Z));
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return null;
        }

        missing[best].Remove(markerId);
        return SoakSpot(odd, best == leftIndex, markerId);
    }

    /// <summary>The spot of someone soaking a tower, by the round's parity, the tower's side and their marker.</summary>
    private static Spot? SoakSpot(bool odd, bool left, uint markerId)
    {
        const uint stack = P2ForsakenTracker.StackMarkerId;
        const uint spread = P2ForsakenTracker.SpreadMarkerId;
        const uint cone = P2ForsakenTracker.ConeMarkerId;
        var side = left ? "Left" : "Right";
        var sign = left ? 1.0f : -1.0f;

        return (odd, left, markerId) switch
        {
            (true, true, stack) => new Spot(6.3f, 0.0f, SoakTolerance, "Left tower stack: on the boss hitbox, in line with the tower"),
            (true, true, cone) => new Spot(10.3f, 0.0f, SoakTolerance, "Left tower cone: away from the stack, just off the tower edge"),
            (true, false, stack) => new Spot(-0.7f, -4.8f, SoakTolerance, "Right tower stack: between the boss hitbox and the tower, before the midpoint"),
            (true, false, spread) => new Spot(1.4f, -10.7f, SpreadTolerance, "Right tower spread: south of the tower, away from the boss"),
            (false, _, cone) => new Spot(2.2f, 4.75f * sign, SoakTolerance, $"{side} tower cone: front edge of the tower, on the inner hitbox ring"),
            (false, _, spread) => new Spot(8.9f, 6.2f * sign, SpreadTolerance, $"{side} tower spread: south edge of the tower, across from the cone"),
            _ => null,
        };
    }

    /// <summary>The spot of someone not soaking a tower, by the round's parity and their role.</summary>
    private static Spot? FreeSpot(bool odd, RaidPosition position)
    {
        var role = RoleOf(position);
        if (role is null)
        {
            return null;
        }

        return (odd, role) switch
        {
            (true, Role.Tank) => new Spot(3.3f, 0.1f, FreeTolerance, "Stack tank: right in front of the left tower's stack"),
            (true, Role.Healer) => new Spot(12.6f, 0.0f, FreeTolerance, "Healer: behind the left tower's cone"),
            (true, Role.Melee) => new Spot(1.0f, -3.3f, FreeTolerance, "Stack with the right tower's stack"),
            (true, Role.Ranged) => new Spot(1.2f, -3.3f, FreeTolerance, "Stack with the right tower's stack"),
            (false, Role.Tank) => new Spot(-4.9f, 3.8f, FreeTolerance, "Clone bait: outer ring of the boss hitbox, left side"),
            (false, Role.Melee) => new Spot(-5.1f, -3.7f, FreeTolerance, "Clone bait: outer ring of the boss hitbox, right side"),
            (false, Role.Healer) => new Spot(0.4f, 10.3f, FreeTolerance, "Cone bait: on the left marker"),
            (false, Role.Ranged) => new Spot(0.5f, -10.2f, FreeTolerance, "Cone bait: on the right marker"),
            _ => null,
        };
    }

    private static Role? RoleOf(RaidPosition position)
    {
        return position switch
        {
            RaidPosition.MT or RaidPosition.OT => Role.Tank,
            RaidPosition.H1 or RaidPosition.H2 => Role.Healer,
            RaidPosition.M1 or RaidPosition.M2 => Role.Melee,
            RaidPosition.R1 or RaidPosition.R2 => Role.Ranged,
            _ => null,
        };
    }
}
