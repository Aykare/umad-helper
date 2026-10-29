using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SamplePlugin;

/// <summary>
/// Turns a Forsaken run around the arena center so a round's towers always end up at the bottom of the history view.
/// Angles are measured clockwise on screen (x east, z south), the same way the game's z axis runs.
/// </summary>
internal static class ForsakenRotation
{
    private const float ArenaCenter = 100.0f;
    private const float SouthDegrees = 90.0f;

    /// <summary>
    /// The clockwise rotation in degrees that puts the middle of the round's towers due south. The final bait uses
    /// the eighth set's towers, and a round without recorded towers is not turned.
    /// </summary>
    public static float RotationFor(ForsakenRun run, int roundNumber)
    {
        var towerRound = run.Rounds.Find(round => round.Number == (roundNumber == ForsakenRound.FinalBaitNumber ? 8 : roundNumber));
        if (towerRound is null || towerRound.Towers.Count == 0)
        {
            return 0.0f;
        }

        var sin = 0.0f;
        var cos = 0.0f;
        foreach (var tower in towerRound.Towers)
        {
            var radians = MathF.Atan2(tower.Z - ArenaCenter, tower.X - ArenaCenter);
            sin += MathF.Sin(radians);
            cos += MathF.Cos(radians);
        }

        return SouthDegrees - (MathF.Atan2(sin, cos) * (180.0f / MathF.PI));
    }

    /// <summary>Rotates a world position around the arena center.</summary>
    public static Vector2 Rotate(Vector2 position, float degrees)
    {
        var radians = degrees * (MathF.PI / 180.0f);
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var x = position.X - ArenaCenter;
        var z = position.Y - ArenaCenter;
        return new Vector2(ArenaCenter + (x * cos) - (z * sin), ArenaCenter + (x * sin) + (z * cos));
    }

    /// <summary>
    /// A copy of the run with every position and clone facing rotated. Players are shared with the original; the
    /// original is never modified.
    /// </summary>
    public static ForsakenRun Rotate(ForsakenRun run, float degrees)
    {
        if (degrees == 0.0f)
        {
            return run;
        }

        return new ForsakenRun
        {
            StartedUtc = run.StartedUtc,
            Completed = run.Completed,
            BuddyName = run.BuddyName,
            LocalIsAlpha = run.LocalIsAlpha,
            Players = run.Players,
            BossHitboxRadius = run.BossHitboxRadius,
            Rounds = run.Rounds.Select(round => Rotate(round, degrees)).ToList(),
        };
    }

    private static ForsakenRound Rotate(ForsakenRound round, float degrees)
    {
        return new ForsakenRound
        {
            Number = round.Number,
            Spots = RotateSpots(round.Spots, degrees),
            CloneSpots = RotateSpots(round.CloneSpots, degrees),
            FixSpots = RotateSpots(round.FixSpots, degrees),
            AttackSpots = RotateSpots(round.AttackSpots, degrees),
            Towers = round.Towers.Select(tower =>
            {
                var position = Rotate(new Vector2(tower.X, tower.Z), degrees);
                return new ForsakenTowerRecord { X = position.X, Z = position.Y, Soakers = tower.Soakers };
            }).ToList(),
            Clones = round.Clones.Select(clone =>
            {
                var position = Rotate(new Vector2(clone.X, clone.Z), degrees);
                return new ForsakenCloneRecord { X = position.X, Z = position.Y, Facing = clone.Facing + degrees };
            }).ToList(),
            Attacks = round.Attacks.Select(attack => Rotate(attack, degrees)).ToList(),
        };
    }

    private static List<ForsakenSpot> RotateSpots(List<ForsakenSpot> spots, float degrees)
    {
        return spots.Select(spot =>
        {
            var position = Rotate(new Vector2(spot.X, spot.Z), degrees);
            return new ForsakenSpot { EntityId = spot.EntityId, X = position.X, Z = position.Y };
        }).ToList();
    }

    private static ForsakenAttackRecord Rotate(ForsakenAttackRecord attack, float degrees)
    {
        var target = Rotate(new Vector2(attack.TargetX, attack.TargetZ), degrees);
        var rotated = new ForsakenAttackRecord
        {
            ActionId = attack.ActionId,
            TargetEntityId = attack.TargetEntityId,
            TargetX = target.X,
            TargetZ = target.Y,
        };

        if (attack is { CasterX: float casterX, CasterZ: float casterZ })
        {
            var caster = Rotate(new Vector2(casterX, casterZ), degrees);
            rotated.CasterX = caster.X;
            rotated.CasterZ = caster.Y;
        }

        return rotated;
    }
}
