using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Windows;

public partial class HistoryWindow
{
    // Tower soakers in the logs stood up to 3.9 yalms from the tower's center.
    private const float TowerRadiusYalms = 4.0f;
    private const float MarkerGlyphSize = 18.0f;
    private const float ForsakenArenaMaxSize = 760.0f;

    // Spread and cone sizes from the game's action data (Spellscatter radius 5, Spellwave range 40). The cone's angle
    // is not in the data; 90 degrees is an assumption.
    private const float SpreadRadiusYalms = 5.0f;
    private const float StackRadiusYalms = 5.0f;
    private const float BaitRadiusYalms = 5.0f;
    private const float AllThingsEndingRangeYalms = 100.0f;
    private const float AllThingsEndingHalfAngleRadians = MathF.PI / 2.0f;
    private const float BossSquareHalfSize = 8.0f;
    private const float BossInnerRingOffsetYalms = 1.0f;
    private const float BossCenterToleranceYalms = 0.5f;
    private const float ConeRangeYalms = 40.0f;
    private const float ConeHalfAngleRadians = MathF.PI / 4.0f;
    private const int ConeSegments = 16;

    // The exact spot is a small dot; the job and marker icons sit beside it so they never hide the position.
    private const float PositionDotRadius = 4.0f;
    private static readonly Vector2 IconOffset = new(18.0f, -18.0f);

    private const int ForsakenTowerColumnCount = 8;
    private const int ForsakenFirstRoundColumn = 2;
    private const float ForsakenPairColumnWidth = 74.0f;
    private const float ForsakenPlayerColumnWidth = 150.0f;

    private static readonly (RaidPosition First, RaidPosition Second)[] ForsakenPairs =
    [
        (RaidPosition.MT, RaidPosition.H1),
        (RaidPosition.OT, RaidPosition.H2),
        (RaidPosition.M1, RaidPosition.R1),
        (RaidPosition.M2, RaidPosition.R2),
    ];

    private int selectedForsakenIndex;
    private int selectedForsakenRound;
    private bool showForsakenClones = true;
    private bool showForsakenTowers = true;
    private bool showForsakenStacks = true;
    private bool showForsakenSpreads = true;
    private bool showForsakenCones = true;
    private bool showForsakenIcons = true;
    private bool showForsakenMistakes = true;
    private bool forsakenPreviewActive;
    private ForsakenRun? forsakenPreviewRun;

    private void DrawForsakenTab()
    {
        if (forsakenPreviewActive)
        {
            forsakenPreviewRun ??= CreateSampleForsakenRun();
            ImGui.TextDisabled("Preview: sample data from a real pull. Not saved.");
            DrawForsakenRun(forsakenPreviewRun);

            ImGui.Spacing();
            if (ImGui.Button("Close preview##Forsaken"))
            {
                forsakenPreviewActive = false;
            }

            return;
        }

        var runs = GetForsakenRuns();
        if (runs.Count == 0)
        {
            ImGui.TextDisabled("No Forsaken runs recorded yet.");
            return;
        }

        selectedForsakenIndex = DrawRunPicker("Forsaken", runs, DescribeForsakenRun, selectedForsakenIndex);
        DrawForsakenRun(runs[selectedForsakenIndex]);

        ImGui.Spacing();
        if (DrawClearButton("Forsaken"))
        {
            plugin.ForsakenHistory.Clear();
            selectedForsakenIndex = 0;
        }
    }

    /// <summary>Draws the arena with a round picker, the mistakes list, and the marker table below it.</summary>
    private void DrawForsakenRun(ForsakenRun run)
    {
        if (run.Rounds.Count > 0)
        {
            DrawForsakenArena(run);
        }
        else
        {
            ImGui.TextDisabled("No positions were recorded for this run.");
        }

        ImGui.Spacing();
        DrawForsakenTable(run);
    }

    /// <summary>
    /// Draws the arena as it was at the selected round: the towers that went off, who soaked them, and every player
    /// with the marker they were holding.
    /// </summary>
    private void DrawForsakenArena(ForsakenRun run)
    {
        var rounds = run.Rounds.Where(candidate => candidate.Number > 0).OrderBy(candidate => candidate.Number).ToList();
        if (rounds.Count == 0)
        {
            ImGui.TextDisabled("No tower rounds were recorded for this run.");
            return;
        }

        if (rounds.All(candidate => candidate.Number != selectedForsakenRound))
        {
            selectedForsakenRound = rounds[0].Number;
        }

        var hasFinalBait = rounds.Any(candidate => candidate.Number == ForsakenRound.FinalBaitNumber);
        if (ImGui.BeginTable("##ForsakenRoundTabs", ForsakenFirstRoundColumn + ForsakenTowerColumnCount + (hasFinalBait ? 1 : 0), ImGuiTableFlags.None))
        {
            SetupForsakenColumns(hasFinalBait);
            ImGui.TableNextRow();
            foreach (var candidate in rounds)
            {
                ImGui.TableSetColumnIndex(ForsakenFirstRoundColumn + candidate.Number - 1);
                var selected = candidate.Number == selectedForsakenRound;
                if (selected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.75f, 1.0f));
                }

                if (ImGui.Button($"{(candidate.Number == ForsakenRound.FinalBaitNumber ? "Bait" : $"T{candidate.Number}")}##ForsakenRound{candidate.Number}", new Vector2(-float.Epsilon, 0.0f)))
                {
                    selectedForsakenRound = candidate.Number;
                }

                if (selected)
                {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndTable();
        }

        // The history shows each round turned so its towers are at the bottom; the strategy check keeps using the
        // recorded positions and its results are turned to match.
        var turn = ForsakenRotation.RotationFor(run, selectedForsakenRound);
        var view = ForsakenRotation.Rotate(run, turn);
        var originalRound = rounds.First(candidate => candidate.Number == selectedForsakenRound);
        var round = view.Rounds.First(candidate => candidate.Number == selectedForsakenRound);
        var recordedRun = run;
        run = view;

        ImGui.Checkbox("Clones##ForsakenLayer", ref showForsakenClones);
        ImGui.SameLine();
        ImGui.Checkbox("Tower damage##ForsakenLayer", ref showForsakenTowers);
        ImGui.SameLine();
        ImGui.Checkbox("Stack##ForsakenLayer", ref showForsakenStacks);
        ImGui.SameLine();
        ImGui.Checkbox("Spread##ForsakenLayer", ref showForsakenSpreads);
        ImGui.SameLine();
        ImGui.Checkbox("Cone##ForsakenLayer", ref showForsakenCones);
        ImGui.SameLine();
        ImGui.Checkbox("Player icons / markers##ForsakenLayer", ref showForsakenIcons);
        ImGui.SameLine();
        ImGui.Checkbox("Mistakes##ForsakenLayer", ref showForsakenMistakes);

        var drawList = BeginArena("##ForsakenArena", out var center, out var scale, out var arenaHovered, ForsakenArenaMaxSize);
        for (var index = 0; index < LimitCutStrategy.RingLabels.Length; index++)
        {
            var label = LimitCutStrategy.RingLabels[index];
            var waymark = ForsakenRotation.Rotate(
                new Vector2(LimitCutStrategy.WaymarkPositions[index].X, LimitCutStrategy.WaymarkPositions[index].Z),
                turn);
            DrawWaymark(
                drawList,
                LimitCutStrategy.ImageName(label),
                center + ((waymark - new Vector2(ArenaCenter)) * scale),
                char.IsLetter(label[0]),
                scale);
        }

        // The boss hitbox is part of the arena background, so it is drawn regardless of the layer toggles.
        DrawForsakenBossRings(drawList, run, center, scale);

        var towerFill = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.22f));
        var towerBorder = ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 1.0f, 0.9f));
        var towerOutline = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.8f));
        // With the tower layer off the towers stay as plain white outlines to show where they spawned, but none is
        // listed, so every player counts as a non-soaker and gets a gray dot.
        foreach (var tower in round.Towers)
        {
            var position = center + (new Vector2(tower.X - ArenaCenter, tower.Z - ArenaCenter) * scale);
            if (!showForsakenTowers)
            {
                drawList.AddCircle(position, TowerRadiusYalms * scale, towerOutline, 32, 1.5f);
                continue;
            }

            drawList.AddCircleFilled(position, TowerRadiusYalms * scale, towerFill, 32);
            drawList.AddCircle(position, TowerRadiusYalms * scale, towerBorder, 32, 2.0f);
        }

        // Markers are issued after a tower, so the ones held during tower N were issued after tower N-1.
        var markerHit = Math.Max(0, round.Number - 1);
        if (showForsakenClones)
        {
            DrawForsakenClones(drawList, round, center, scale);
        }

        DrawForsakenAttacks(drawList, run, round, markerHit, center, scale, showForsakenClones, showForsakenStacks, showForsakenSpreads, showForsakenCones);

        // Soakers are known even with the tower layer off: they decide whose markers resolved this round.
        var soakers = round.Towers.SelectMany(tower => tower.Soakers).ToHashSet();
        var evaluation = ForsakenStrategy.Evaluate(recordedRun, originalRound, ForsakenPositionOf)
            .Select(expectation => expectation with
            {
                Expected = ForsakenRotation.Rotate(expectation.Expected, turn),
                Actual = ForsakenRotation.Rotate(expectation.Actual, turn),
            });
        var mistakes = showForsakenMistakes ? evaluation.Where(expectation => expectation.Failed).ToList() : [];
        DrawForsakenPlayers(drawList, run, round, markerHit, soakers, mistakes, center, scale, arenaHovered);

        foreach (var mistake in mistakes)
        {
            var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == mistake.EntityId);
            ImGui.TextColored(ForsakenMistakeColor, $"{DescribeForsakenPlayer(player)}: {mistake.Slot} ({mistake.Distance:0.0} yalms away)");
        }
    }

    /// <summary>A player's name with their raid position, for mistake lists.</summary>
    private string DescribeForsakenPlayer(ForsakenPlayerRecord? player)
    {
        if (player is null)
        {
            return "Unknown";
        }

        var position = ForsakenPositionOf(player);
        return position == RaidPosition.None ? player.Name : $"{player.Name} ({position})";
    }

    /// <summary>
    /// Draws the spread circles and cones that resolved after the round's towers. A spread circle is centered on a
    /// hit player who held the spread marker; a cone starts at the player holding the cone marker and points at the
    /// player it hit.
    /// </summary>
    private static void DrawForsakenAttacks(
        ImDrawListPtr drawList,
        ForsakenRun run,
        ForsakenRound round,
        int markerHit,
        Vector2 center,
        float scale,
        bool showClones,
        bool showStacks,
        bool showSpreads,
        bool showCones)
    {
        var spreadFill = ImGui.GetColorU32(ForsakenSpreadColor with { W = 0.2f });
        var spreadBorder = ImGui.GetColorU32(ForsakenSpreadColor with { W = 0.9f });
        var coneFill = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.25f, 0.18f));
        var coneBorder = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.3f, 0.9f));
        var stackFill = ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.5f, 0.18f));
        var stackBorder = ImGui.GetColorU32(new Vector4(0.4f, 1.0f, 0.55f, 0.9f));
        var baitFill = ImGui.GetColorU32(new Vector4(0.8f, 0.5f, 1.0f, 0.18f));
        var baitBorder = ImGui.GetColorU32(new Vector4(0.8f, 0.5f, 1.0f, 0.9f));
        uint? MarkerOf(uint entityId)
            => run.Players.FirstOrDefault(candidate => candidate.EntityId == entityId) is { } player
                && run.TryGetMarkerDuring(player, round.Number, out var markerId)
                    ? markerId
                    : null;

        var drawnSpread = new HashSet<uint>();
        var drawnStack = new HashSet<uint>();
        var drawnBait = new HashSet<uint>();
        var coneOrigins = new List<Vector2>();
        foreach (var attack in round.Attacks)
        {
            var target = new Vector2(attack.TargetX, attack.TargetZ);
            if (attack.ActionId == P2ForsakenTracker.SpelldriverActionId)
            {
                // Only the stack marker holder is the circle's center; the others in it are just hit by it.
                if (showStacks && MarkerOf(attack.TargetEntityId) == P2ForsakenTracker.StackMarkerId && drawnStack.Add(attack.TargetEntityId))
                {
                    var position = center + ((target - new Vector2(ArenaCenter)) * scale);
                    drawList.AddCircleFilled(position, StackRadiusYalms * scale, stackFill, 48);
                    drawList.AddCircle(position, StackRadiusYalms * scale, stackBorder, 48, 2.0f);
                }

                continue;
            }

            if (attack.ActionId == P2ForsakenTracker.SpellscatterActionId)
            {
                if (showSpreads && MarkerOf(attack.TargetEntityId) == P2ForsakenTracker.SpreadMarkerId && drawnSpread.Add(attack.TargetEntityId))
                {
                    var position = center + ((target - new Vector2(ArenaCenter)) * scale);
                    drawList.AddCircleFilled(position, SpreadRadiusYalms * scale, spreadFill, 48);
                    drawList.AddCircle(position, SpreadRadiusYalms * scale, spreadBorder, 48, 2.0f);
                }

                continue;
            }

            if (attack.ActionId is >= P2ForsakenTracker.BaitFirstActionId and <= P2ForsakenTracker.BaitLastActionId)
            {
                // Bait circles are centered on the baited player.
                if (showClones && drawnBait.Add(attack.TargetEntityId))
                {
                    var position = center + ((target - new Vector2(ArenaCenter)) * scale);
                    drawList.AddCircleFilled(position, BaitRadiusYalms * scale, baitFill, 48);
                    drawList.AddCircle(position, BaitRadiusYalms * scale, baitBorder, 48, 2.0f);
                }

                continue;
            }

            if (!showCones || attack.ActionId != P2ForsakenTracker.SpellwaveActionId)
            {
                continue;
            }

            // Older recordings may lack the caster, so fall back to the nearest other cone holder.
            Vector2? origin = attack is { CasterX: float casterX, CasterZ: float casterZ }
                ? new Vector2(casterX, casterZ)
                : round.Spots
                    .Where(spot => spot.EntityId != attack.TargetEntityId && MarkerOf(spot.EntityId) == P2ForsakenTracker.ConeMarkerId)
                    .Select(spot => new Vector2(spot.X, spot.Z))
                    .OrderBy(candidate => Vector2.Distance(candidate, target))
                    .Cast<Vector2?>()
                    .FirstOrDefault();
            if (origin is not { } start
                || Vector2.Distance(start, target) < 0.01f
                || coneOrigins.Any(known => Vector2.Distance(known, start) < 0.5f))
            {
                continue;
            }

            coneOrigins.Add(start);
            DrawCone(drawList, start, Vector2.Normalize(target - start), ConeRangeYalms, ConeHalfAngleRadians, coneFill, coneBorder, center, scale);
        }
    }

    private readonly record struct ForsakenDot(uint EntityId, Vector2 Position, Vector4 Color, string Text);

    private static readonly Vector4 ForsakenTowerColor = new(0.3f, 0.65f, 1.0f, 1.0f);
    private static readonly Vector4 ForsakenMissedColor = new(0.5f, 0.52f, 0.56f, 1.0f);
    private static readonly Vector4 ForsakenCloneColor = new(0.8f, 0.5f, 1.0f, 1.0f);
    private static readonly Vector4 ForsakenStackColor = new(0.4f, 1.0f, 0.55f, 1.0f);
    private static readonly Vector4 ForsakenSpreadColor = new(1.0f, 0.55f, 0.1f, 1.0f);
    private static readonly Vector4 ForsakenConeColor = new(1.0f, 0.85f, 0.3f, 1.0f);
    private static readonly Vector4 ForsakenMistakeColor = new(1.0f, 0.2f, 0.2f, 1.0f);

    private static bool IsForsakenBait(uint actionId)
        => actionId is >= P2ForsakenTracker.BaitFirstActionId and <= P2ForsakenTracker.BaitLastActionId;

    private static bool IsForsakenCloneHit(uint actionId)
        => IsForsakenBait(actionId)
            || actionId is P2ForsakenTracker.AllThingsEndingActionId or P2ForsakenTracker.AllThingsEndingVariantActionId;

    private static bool IsForsakenSpellAttack(uint actionId)
        => actionId is P2ForsakenTracker.SpelldriverActionId
            or P2ForsakenTracker.SpellscatterActionId
            or P2ForsakenTracker.SpellwaveActionId;

    /// <summary>
    /// Works out which of the round's stack, spread and cone markers did not go off, and where each cone that did go
    /// off started. A player not hit by Path of Light this round has their marker unresolved, so it counts as not
    /// gone off. For the soakers nothing is judged unless the round recorded at least one of the three attacks.
    /// </summary>
    private static (Dictionary<uint, Vector2> ConeOrigins, HashSet<uint> MissedMarkers) AnalyzeForsakenAttacks(
        ForsakenRun run,
        ForsakenRound round,
        HashSet<uint> soakers,
        int markerHit)
    {
        var coneOrigins = new Dictionary<uint, Vector2>();
        var missedMarkers = new HashSet<uint>();
        if (soakers.Count > 0)
        {
            foreach (var player in run.Players.Where(player => !soakers.Contains(player.EntityId) && run.TryGetMarkerDuring(player, round.Number, out _)))
            {
                missedMarkers.Add(player.EntityId);
            }
        }

        var spells = round.Attacks.Where(attack => IsForsakenSpellAttack(attack.ActionId)).ToList();
        if (spells.Count == 0)
        {
            return (coneOrigins, missedMarkers);
        }

        var attackSpots = round.AttackSpots.Count > 0 ? round.AttackSpots : round.Spots;
        var coneHolders = new List<(uint EntityId, Vector2 Position)>();
        foreach (var player in run.Players)
        {
            if (!soakers.Contains(player.EntityId) || !run.TryGetMarkerDuring(player, round.Number, out var markerId))
            {
                continue;
            }

            switch (markerId)
            {
                case P2ForsakenTracker.StackMarkerId
                    when !spells.Any(attack => attack.ActionId == P2ForsakenTracker.SpelldriverActionId && attack.TargetEntityId == player.EntityId):
                case P2ForsakenTracker.SpreadMarkerId
                    when !spells.Any(attack => attack.ActionId == P2ForsakenTracker.SpellscatterActionId && attack.TargetEntityId == player.EntityId):
                    missedMarkers.Add(player.EntityId);
                    break;
                case P2ForsakenTracker.ConeMarkerId
                    when attackSpots.FirstOrDefault(spot => spot.EntityId == player.EntityId) is { } spot:
                    coneHolders.Add((player.EntityId, new Vector2(spot.X, spot.Z)));
                    break;
            }
        }

        // Without a recorded caster a cone's origin is unknown, so no cone can be judged.
        var cones = spells.Where(attack => attack.ActionId == P2ForsakenTracker.SpellwaveActionId).ToList();
        if (cones.Any(attack => attack.CasterX is null || attack.CasterZ is null))
        {
            return (coneOrigins, missedMarkers);
        }

        var origins = new List<Vector2>();
        foreach (var origin in cones.Select(attack => new Vector2(attack.CasterX!.Value, attack.CasterZ!.Value)))
        {
            if (!origins.Any(known => Vector2.Distance(known, origin) < 0.5f))
            {
                origins.Add(origin);
            }
        }

        // Each origin belongs to the nearest still unmatched cone holder.
        while (coneHolders.Count > 0 && origins.Count > 0)
        {
            var (holder, origin) = (from candidateHolder in coneHolders
                                    from candidateOrigin in origins
                                    orderby Vector2.DistanceSquared(candidateHolder.Position, candidateOrigin)
                                    select (candidateHolder, candidateOrigin)).First();
            coneOrigins[holder.EntityId] = origin;
            coneHolders.Remove(holder);
            origins.Remove(origin);
        }

        foreach (var (entityId, _) in coneHolders)
        {
            missedMarkers.Add(entityId);
        }

        return (coneOrigins, missedMarkers);
    }

    /// <summary>
    /// Links each clone of an odd round to the player who baited it in the previous even round (the clone spawned
    /// where it later casts from), and returns that player's position when the clones fixed their cleave direction.
    /// </summary>
    private static List<(uint EntityId, Vector2 ClonePosition, Vector2 PlayerPosition)> FindForsakenCloneLocks(ForsakenRun run, ForsakenRound round)
    {
        var locks = new List<(uint, Vector2, Vector2)>();
        var previous = run.Rounds.FirstOrDefault(candidate => candidate.Number == round.Number - 1);
        if (previous is null || round.FixSpots.Count == 0)
        {
            return locks;
        }

        foreach (var clone in round.Clones)
        {
            var bait = previous.Attacks.FirstOrDefault(attack => IsForsakenBait(attack.ActionId)
                && attack is { CasterX: float x, CasterZ: float z }
                && Vector2.Distance(new Vector2(x, z), new Vector2(clone.X, clone.Z)) <= 0.5f);
            if (bait is not null && round.FixSpots.FirstOrDefault(spot => spot.EntityId == bait.TargetEntityId) is { } spot)
            {
                locks.Add((bait.TargetEntityId, new Vector2(clone.X, clone.Z), new Vector2(spot.X, spot.Z)));
            }
        }

        return locks;
    }

    /// <summary>
    /// Builds every position dot of the enabled layers, in priority order (tower, clones, stack/spread/cone hits,
    /// cone origins). Players a mechanic did not hit get a gray dot at their position when it happened.
    /// </summary>
    private List<ForsakenDot> BuildForsakenDots(
        ForsakenRun run,
        ForsakenRound round,
        HashSet<uint> soakers,
        Dictionary<uint, Vector2> coneOrigins,
        List<(uint EntityId, Vector2 ClonePosition, Vector2 PlayerPosition)> cloneLocks)
    {
        var known = run.Players.ToDictionary(player => player.EntityId, player => player.Name);
        string NameOf(uint entityId) => known.GetValueOrDefault(entityId, "Unknown");
        var dots = new List<ForsakenDot>();

        if (showForsakenTowers && round.Number != ForsakenRound.FinalBaitNumber)
        {
            foreach (var spot in round.Spots.Where(spot => known.ContainsKey(spot.EntityId)))
            {
                var hit = soakers.Contains(spot.EntityId);
                dots.Add(new ForsakenDot(
                    spot.EntityId,
                    new Vector2(spot.X, spot.Z),
                    hit ? ForsakenTowerColor : ForsakenMissedColor,
                    hit
                        ? $"{NameOf(spot.EntityId)}: position when hit by Path of Light"
                        : $"{NameOf(spot.EntityId)}: position when the towers hit (not hit by Path of Light)"));
            }
        }

        var cloneHits = round.Attacks.Where(attack => IsForsakenCloneHit(attack.ActionId)).ToList();
        if (showForsakenClones)
        {
            foreach (var attack in cloneHits)
            {
                var source = IsForsakenBait(attack.ActionId)
                    ? (attack is { CasterX: float x, CasterZ: float z } && Vector2.Distance(new Vector2(x, z), new Vector2(ArenaCenter)) <= BossCenterToleranceYalms
                        ? "Kefka's bait circle"
                        : "a clone's bait circle")
                    : "a clone's All Things Ending";
                dots.Add(new ForsakenDot(
                    attack.TargetEntityId,
                    new Vector2(attack.TargetX, attack.TargetZ),
                    ForsakenCloneColor,
                    $"{NameOf(attack.TargetEntityId)}: position when hit by {source}"));
            }

            foreach (var (entityId, _, playerPosition) in cloneLocks)
            {
                dots.Add(new ForsakenDot(
                    entityId,
                    playerPosition,
                    ForsakenCloneColor,
                    $"{NameOf(entityId)}: position when their clone fixed its half-room cleave"));
            }

            if (cloneHits.Count > 0 || round.Clones.Count > 0 || round.CloneSpots.Count > 0)
            {
                var hitIds = cloneHits.Select(attack => attack.TargetEntityId).ToHashSet();
                var cloneSpots = round.CloneSpots.Count > 0 ? round.CloneSpots : round.Spots;
                foreach (var spot in cloneSpots.Where(spot => known.ContainsKey(spot.EntityId) && !hitIds.Contains(spot.EntityId)))
                {
                    dots.Add(new ForsakenDot(
                        spot.EntityId,
                        new Vector2(spot.X, spot.Z),
                        ForsakenMissedColor,
                        $"{NameOf(spot.EntityId)}: position when the clones hit (not hit by a clone)"));
                }
            }
        }

        var spells = round.Attacks.Where(attack => IsForsakenSpellAttack(attack.ActionId)).ToList();
        foreach (var attack in spells)
        {
            var (enabled, color, name) = attack.ActionId switch
            {
                P2ForsakenTracker.SpelldriverActionId => (showForsakenStacks, ForsakenStackColor, "stack"),
                P2ForsakenTracker.SpellscatterActionId => (showForsakenSpreads, ForsakenSpreadColor, "spread"),
                _ => (showForsakenCones, ForsakenConeColor, "cone"),
            };
            if (enabled)
            {
                dots.Add(new ForsakenDot(
                    attack.TargetEntityId,
                    new Vector2(attack.TargetX, attack.TargetZ),
                    color,
                    $"{NameOf(attack.TargetEntityId)}: position when hit by {name}"));
            }
        }

        if (showForsakenCones)
        {
            foreach (var (entityId, origin) in coneOrigins)
            {
                dots.Add(new ForsakenDot(entityId, origin, ForsakenConeColor, $"{NameOf(entityId)}: position where their cone went off"));
            }
        }

        var anyAttackLayer = showForsakenStacks || showForsakenSpreads || showForsakenCones;
        if (anyAttackLayer && (spells.Count > 0 || round.AttackSpots.Count > 0))
        {
            var hitIds = spells.Select(attack => attack.TargetEntityId).ToHashSet();
            var attackSpots = round.AttackSpots.Count > 0 ? round.AttackSpots : round.Spots;
            foreach (var spot in attackSpots.Where(spot => known.ContainsKey(spot.EntityId)
                && !hitIds.Contains(spot.EntityId)
                && !(showForsakenCones && coneOrigins.ContainsKey(spot.EntityId))))
            {
                dots.Add(new ForsakenDot(
                    spot.EntityId,
                    new Vector2(spot.X, spot.Z),
                    ForsakenMissedColor,
                    $"{NameOf(spot.EntityId)}: position when stack, spread and cone hit (not hit)"));
            }
        }

        return dots;
    }

    /// <summary>
    /// Draws the clone squares with their baiter lines, every position dot with its tooltip, and the player icons.
    /// An icon sits beside the player's first dot (by layer priority), or beside their tower-time position if they
    /// have none. Icons are drawn last so none hides a dot.
    /// </summary>
    private void DrawForsakenPlayers(
        ImDrawListPtr drawList,
        ForsakenRun run,
        ForsakenRound round,
        int markerHit,
        HashSet<uint> soakers,
        List<ForsakenExpectation> mistakes,
        Vector2 center,
        float scale,
        bool arenaHovered)
    {
        var (coneOrigins, missedMarkers) = AnalyzeForsakenAttacks(run, round, soakers, markerHit);
        var cloneLocks = FindForsakenCloneLocks(run, round);
        var dots = BuildForsakenDots(run, round, soakers, coneOrigins, cloneLocks);
        var mouse = ImGui.GetMousePos();
        var tooltip = new List<string>();
        Vector2 ToScreen(Vector2 world) => center + ((world - new Vector2(ArenaCenter)) * scale);

        // Odd rounds: a dashed line from each clone to where its baiter stood when the clone fixed its cleave.
        if (showForsakenClones)
        {
            foreach (var (_, clonePosition, playerPosition) in cloneLocks)
            {
                DrawDottedLine(drawList, ToScreen(clonePosition), ToScreen(playerPosition), ImGui.GetColorU32(ForsakenCloneColor with { W = 0.8f }));
            }
        }

        // Clones that baited a player: a dotted line from the clone to where that player stood when hit.
        if (showForsakenClones)
        {
            foreach (var attack in round.Attacks.Where(attack => IsForsakenBait(attack.ActionId)))
            {
                if (attack is not { CasterX: float cloneX, CasterZ: float cloneZ })
                {
                    continue;
                }

                var isBoss = Vector2.Distance(new Vector2(cloneX, cloneZ), new Vector2(ArenaCenter)) <= BossCenterToleranceYalms;
                var clonePosition = ToScreen(new Vector2(cloneX, cloneZ));
                DrawDottedLine(drawList, clonePosition, ToScreen(new Vector2(attack.TargetX, attack.TargetZ)), ImGui.GetColorU32(ForsakenCloneColor with { W = 0.8f }));
                DrawCloneSquare(drawList, clonePosition, isBoss);
                if (Vector2.Distance(mouse, clonePosition) <= (isBoss ? BossSquareHalfSize : PositionDotRadius) + 4.0f)
                {
                    var owner = run.Players.FirstOrDefault(candidate => candidate.EntityId == attack.TargetEntityId);
                    tooltip.Add($"{(isBoss ? "Kefka" : "Clone")} baited by {owner?.Name ?? "Unknown"}");
                }
            }
        }

        var anchors = new Dictionary<uint, Vector2>();
        foreach (var dot in dots)
        {
            anchors.TryAdd(dot.EntityId, ToScreen(dot.Position));
        }

        var icons = new List<(ForsakenPlayerRecord Player, Vector2 Position)>();
        if (showForsakenIcons)
        {
            foreach (var spot in round.Spots)
            {
                if (run.Players.FirstOrDefault(candidate => candidate.EntityId == spot.EntityId) is not { } player)
                {
                    continue;
                }

                var hasAnchor = anchors.TryGetValue(player.EntityId, out var anchor);
                var basePosition = hasAnchor ? anchor : ToScreen(new Vector2(spot.X, spot.Z));
                var iconPosition = basePosition + IconOffset;
                if (hasAnchor)
                {
                    drawList.AddLine(basePosition, iconPosition, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.35f)), 1.0f);
                }

                icons.Add((player, iconPosition));
            }
        }

        // Gray dots first, so a colored dot at the same spot stays visible on top.
        foreach (var dot in dots.OrderBy(dot => dot.Color == ForsakenMissedColor ? 0 : 1))
        {
            var position = ToScreen(dot.Position);
            drawList.AddCircleFilled(position, PositionDotRadius + 1.5f, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)), 16);
            drawList.AddCircleFilled(position, PositionDotRadius, ImGui.GetColorU32(dot.Color), 16);
        }

        foreach (var dot in dots)
        {
            if (Vector2.Distance(mouse, ToScreen(dot.Position)) <= PositionDotRadius + 4.0f)
            {
                tooltip.Add(dot.Text);
            }
        }

        foreach (var (player, iconPosition) in icons)
        {
            var iconName = JobIcons.ImageName(player.JobId);
            if (iconName is null)
            {
                drawList.AddCircleFilled(iconPosition, DotRadius, ImGui.GetColorU32(new Vector4(0.45f, 0.48f, 0.55f, 1.0f)), 24);
            }
            else
            {
                TryDrawIcon(drawList, iconName, iconPosition, DotRadius * JobIconScale);
            }

            var hasMarker = run.TryGetMarkerDuring(player, round.Number, out var markerId);
            hasMarker &= round.Number != ForsakenRound.FinalBaitNumber;
            var didNotGoOff = missedMarkers.Contains(player.EntityId);
            if (hasMarker && ForsakenMarkerImage(markerId, didNotGoOff) is { } markerImage)
            {
                var glyphCenter = iconPosition + new Vector2((DotRadius * JobIconScale * 0.5f) + (MarkerGlyphSize * 0.5f) + 2.0f, 0.0f);
                drawList.AddCircleFilled(glyphCenter, (MarkerGlyphSize * 0.5f) + 1.0f, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.7f)), 16);
                TryDrawIcon(drawList, markerImage, glyphCenter, MarkerGlyphSize);
            }

            if (Vector2.Distance(mouse, iconPosition) <= DotRadius)
            {
                var text = player.Name;
                if (ForsakenPositionOf(player) is var position and not RaidPosition.None)
                {
                    text += $" ({RaidPositions.Describe(position)})";
                }

                if (hasMarker)
                {
                    text += $", {P2ForsakenTracker.MarkerName(markerId)}{(didNotGoOff ? " (did not go off)" : string.Empty)}";
                }

                tooltip.Add(soakers.Contains(player.EntityId) ? $"{text}, soaked a tower" : text);
            }
        }

        // Drawn after everything else so the red "should have been here" marker is never hidden.
        foreach (var mistake in mistakes)
        {
            var from = ToScreen(mistake.Actual);
            var to = ToScreen(mistake.Expected);
            var red = ImGui.GetColorU32(ForsakenMistakeColor);
            var dotRadius = PositionDotRadius + 3.5f;
            var offset = to - from;
            if (offset.Length() > dotRadius)
            {
                // The arrow ends at the edge of the red dot so its head stays visible.
                var direction = Vector2.Normalize(offset);
                var tip = to - (direction * dotRadius);
                var side = new Vector2(-direction.Y, direction.X);
                drawList.AddLine(from, tip, red, 2.0f);
                drawList.AddTriangleFilled(tip, tip - (direction * 10.0f) + (side * 5.0f), tip - (direction * 10.0f) - (side * 5.0f), red);
            }

            drawList.AddCircleFilled(to, dotRadius, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)), 20);
            drawList.AddCircleFilled(to, PositionDotRadius + 2.0f, red, 20);

            var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == mistake.EntityId);
            if (Vector2.Distance(mouse, to) <= PositionDotRadius + 6.0f)
            {
                tooltip.Add($"{DescribeForsakenPlayer(player)} should have been here: {mistake.Slot} ({mistake.Distance:0.0} yalms off, {mistake.Tolerance:0.0} allowed)");
            }
        }

        if (arenaHovered && tooltip.Count > 0)
        {
            ImGui.SetTooltip(string.Join("\n", tooltip.Distinct()));
        }
    }

    /// <summary>Draws a cone as a triangle fan, with each ray cut off at the arena edge.</summary>
    private static void DrawCone(ImDrawListPtr drawList, Vector2 origin, Vector2 direction, float range, float halfAngle, uint fill, uint border, Vector2 center, float scale)
    {
        var relative = origin - new Vector2(ArenaCenter);
        var screenStart = center + (relative * scale);
        var previous = Vector2.Zero;
        for (var step = 0; step <= ConeSegments; step++)
        {
            var angle = (-halfAngle) + (2.0f * halfAngle * step / ConeSegments);
            var ray = new Vector2(
                (direction.X * MathF.Cos(angle)) - (direction.Y * MathF.Sin(angle)),
                (direction.X * MathF.Sin(angle)) + (direction.Y * MathF.Cos(angle)));
            var length = MathF.Min(range, DistanceToArenaEdge(relative, ray));
            var point = screenStart + (ray * length * scale);
            if (step > 0)
            {
                drawList.AddTriangleFilled(screenStart, previous, point, fill);
                drawList.AddLine(previous, point, border, 2.0f);
            }
            else
            {
                drawList.AddLine(screenStart, point, border, 2.0f);
            }

            if (step == ConeSegments)
            {
                drawList.AddLine(screenStart, point, border, 2.0f);
            }

            previous = point;
        }
    }

    /// <summary>
    /// Draws the boss's hitbox ring and a second ring one yalm inside it. Runs saved before the radius was
    /// recorded have none.
    /// </summary>
    private static void DrawForsakenBossRings(ImDrawListPtr drawList, ForsakenRun run, Vector2 center, float scale)
    {
        if (run.BossHitboxRadius <= BossInnerRingOffsetYalms)
        {
            return;
        }

        var ring = ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.9f, 0.9f));
        var innerRing = ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.9f, 0.5f));
        drawList.AddCircle(center, run.BossHitboxRadius * scale, ring, 48, 2.0f);
        drawList.AddCircle(center, (run.BossHitboxRadius - BossInnerRingOffsetYalms) * scale, innerRing, 48, 1.5f);
    }

    /// <summary>
    /// Draws the All Things Ending cones of the Kefka clones and the boss for this round, and a marker at each.
    /// </summary>
    private static void DrawForsakenClones(ImDrawListPtr drawList, ForsakenRound round, Vector2 center, float scale)
    {
        var fill = ImGui.GetColorU32(new Vector4(0.7f, 0.35f, 1.0f, 0.12f));
        var border = ImGui.GetColorU32(new Vector4(0.75f, 0.45f, 1.0f, 0.85f));
        foreach (var clone in round.Clones)
        {
            var facing = clone.Facing * MathF.PI / 180.0f;
            var direction = new Vector2(MathF.Sin(facing), -MathF.Cos(facing));
            var origin = new Vector2(clone.X, clone.Z);
            DrawCone(drawList, origin, direction, AllThingsEndingRangeYalms, AllThingsEndingHalfAngleRadians, fill, border, center, scale);
            DrawCloneSquare(
                drawList,
                center + ((origin - new Vector2(ArenaCenter)) * scale),
                Vector2.Distance(origin, new Vector2(ArenaCenter)) <= BossCenterToleranceYalms);
        }
    }

    /// <summary>
    /// Draws a clone's marker: a purple square, so it cannot be mistaken for a player's round dot. The boss, who
    /// always stands at the centre and acts as the fourth clone, gets a larger square with a K.
    /// </summary>
    private static void DrawCloneSquare(ImDrawListPtr drawList, Vector2 position, bool isBoss)
    {
        var halfSize = isBoss ? BossSquareHalfSize : PositionDotRadius;
        var half = new Vector2(halfSize);
        var outline = new Vector2(halfSize + 1.5f);
        drawList.AddRectFilled(position - outline, position + outline, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)));
        drawList.AddRectFilled(position - half, position + half, ImGui.GetColorU32(new Vector4(0.8f, 0.5f, 1.0f, 1.0f)));
        if (isBoss)
        {
            const string label = "K";
            drawList.AddText(position - (ImGui.CalcTextSize(label) * 0.5f), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)), label);
        }
    }

    /// <summary>Draws a thin dotted line between two screen positions.</summary>
    private static void DrawDottedLine(ImDrawListPtr drawList, Vector2 from, Vector2 to, uint color)
    {
        const float dashLength = 3.0f;
        const float gapLength = 4.0f;
        var length = Vector2.Distance(from, to);
        if (length < 0.01f)
        {
            return;
        }

        var direction = (to - from) / length;
        for (var travelled = 0.0f; travelled < length; travelled += dashLength + gapLength)
        {
            var dashEnd = MathF.Min(travelled + dashLength, length);
            drawList.AddLine(from + (direction * travelled), from + (direction * dashEnd), color, 1.0f);
        }
    }

    /// <summary>
    /// Builds a run from a real pull. Players are the four who soaked towers 1-3 and 8 (the Alpha group, the first
    /// four) and the four who soaked towers 4-7 (Beta); names are generic placeholders.
    /// </summary>
    private static ForsakenRun CreateSampleForsakenRun()
    {
        // Marker per hit, starting at hit 0 (initial marker); -1 means no marker that hit. Player 1 is the local player.
        var samples = new (string Name, uint Job, int[] Markers)[]
        {
            ("Player 1", 23, [716, 717, 716, 717, -1, -1, -1]),
            ("Player 2", 28, [715, 716, 715, 717, -1, -1, -1]),
            ("Player 3", 19, [717, 717, 717, 716, -1, -1, -1]),
            ("Player 4", 39, [715, 716, 715, 716, -1, -1, -1]),
            ("Player 5", 32, [717, -1, -1, -1, 716, 717, 715]),
            ("Player 6", 24, [717, -1, -1, -1, 717, 717, 715]),
            ("Player 7", 25, [716, -1, -1, -1, 715, 716, 716]),
            ("Player 8", 34, [716, -1, -1, -1, 715, 716, 717]),
        };

        var run = new ForsakenRun
        {
            StartedUtc = DateTime.UtcNow,
            Completed = true,
            BuddyName = "Player 4",
            LocalIsAlpha = true,

            // Taken from the 19:01 pull the sample is built from; see the saved runs for how it was derived.
            BossHitboxRadius = 6.0f,
        };

        for (var index = 0; index < samples.Length; index++)
        {
            var (name, job, markers) = samples[index];
            var player = new ForsakenPlayerRecord
            {
                EntityId = (uint)(index + 1),
                Name = name,
                JobId = job,
                IsLocalPlayer = index == 0,

                // The sample party is two support pairs and two dps pairs, one pair of each per group.
                Position = index switch
                {
                    0 => RaidPosition.R1,
                    1 => RaidPosition.H2,
                    2 => RaidPosition.OT,
                    3 => RaidPosition.M1,
                    4 => RaidPosition.MT,
                    5 => RaidPosition.H1,
                    6 => RaidPosition.R2,
                    _ => RaidPosition.M2,
                },
            };
            for (var hit = 0; hit < markers.Length; hit++)
            {
                if (markers[hit] >= 0)
                {
                    player.Markers.Add(new ForsakenMarkerEntry { Hit = hit, MarkerId = (uint)markers[hit] });
                }
            }

            run.Players.Add(player);
        }

        // One row per round: everyone's position (players 1-8) and the towers with their soakers (player numbers).
        (float X, float Z)[][] positions =
        [
            [(109.9f, 101.0f), (94.4f, 100.3f), (94.9f, 103.4f), (105.8f, 100.1f), (100.1f, 93.9f), (95.8f, 93.6f), (103.9f, 106.5f), (100.1f, 106.6f)],
            [(101.2f, 88.7f), (106.0f, 99.8f), (110.6f, 100.1f), (99.2f, 95.4f), (103.1f, 100.5f), (112.7f, 99.9f), (101.6f, 96.3f), (101.9f, 96.3f)],
            [(102.1f, 95.3f), (109.1f, 106.1f), (102.0f, 104.8f), (108.8f, 93.3f), (95.3f, 104.1f), (101.2f, 110.5f), (101.2f, 89.6f), (94.7f, 96.0f)],
            [(111.4f, 101.0f), (99.9f, 106.1f), (99.8f, 110.4f), (104.7f, 99.1f), (99.7f, 103.2f), (99.6f, 112.6f), (103.5f, 101.2f), (103.3f, 101.2f)],
            [(110.4f, 100.8f), (89.7f, 100.1f), (96.5f, 95.3f), (103.8f, 95.3f), (105.1f, 102.1f), (95.2f, 102.2f), (104.8f, 108.8f), (94.5f, 108.8f)],
            [(98.4f, 103.2f), (87.5f, 100.4f), (96.5f, 100.1f), (98.5f, 102.9f), (97.3f, 110.4f), (89.3f, 100.0f), (100.8f, 104.9f), (93.7f, 100.0f)],
            [(99.6f, 110.5f), (99.6f, 89.6f), (104.6f, 96.5f), (104.7f, 103.6f), (97.9f, 105.0f), (97.7f, 95.3f), (91.0f, 105.2f), (91.2f, 94.3f)],
            [(96.9f, 98.5f), (100.1f, 87.6f), (99.8f, 96.8f), (96.7f, 98.7f), (95.0f, 100.9f), (99.9f, 94.0f), (89.6f, 97.9f), (100.1f, 89.6f)],
            [(95.4f, 97.9f), (104.7f, 97.7f), (106.2f, 90.7f), (93.7f, 90.9f), (104.1f, 104.6f), (110.5f, 99.0f), (89.9f, 98.6f), (96.0f, 105.4f)],
        ];

        (float X, float Z, int[] Soakers)[][] towers =
        [
            [],
            [(108.0f, 100.0f, [2, 3]), (100.0f, 92.0f, [1, 4])],
            [(105.7f, 105.7f, [2, 3]), (105.7f, 94.3f, [4, 1])],
            [(100.0f, 108.0f, [2, 3]), (108.0f, 100.0f, [4, 1])],
            [(94.3f, 105.7f, [8, 6]), (105.7f, 105.7f, [7, 5])],
            [(92.0f, 100.0f, [8, 6]), (100.0f, 108.0f, [7, 5])],
            [(94.3f, 94.3f, [8, 6]), (94.3f, 105.7f, [7, 5])],
            [(100.0f, 92.0f, [6, 8]), (92.0f, 100.0f, [5, 7])],
            [(105.7f, 94.3f, [2, 3]), (94.3f, 94.3f, [4, 1])],
        ];

        // Spread and cone hits per round: action, player hit (number), where they stood, and the cone's origin.
        (uint Action, int Target, (float X, float Z) Position, (float X, float Z)? Caster)[][] attacks =
        [
            [],
            [(47808, 4, (99.2f, 95.4f), null), (47808, 7, (101.6f, 96.3f), null), (47808, 8, (101.9f, 96.3f), null), (47809, 1, (101.2f, 88.7f), null), (47808, 2, (106.0f, 99.8f), null), (47808, 5, (103.1f, 100.5f), null), (47808, 3, (110.7f, 100.1f), null), (47810, 6, (112.7f, 99.9f), (110.7f, 100.1f))],
            [(47809, 4, (108.8f, 93.3f), null), (47810, 7, (101.2f, 89.6f), (102.1f, 95.3f)), (47810, 6, (101.2f, 110.5f), (102.0f, 104.8f)), (47809, 2, (109.1f, 106.1f), null), (47833, 5, (95.3f, 104.1f), (98.3f, 101.4f)), (47833, 3, (102.0f, 104.8f), (100.5f, 101.1f)), (47833, 8, (94.7f, 96.0f), (97.9f, 98.4f)), (47831, 1, (102.1f, 95.3f), (100.0f, 100.0f))],
            [(47808, 4, (104.7f, 99.1f), null), (47808, 7, (103.5f, 101.2f), null), (47808, 8, (103.2f, 101.1f), null), (47809, 1, (111.4f, 101.0f), null), (47810, 6, (99.2f, 112.6f), (99.8f, 110.5f)), (47808, 2, (99.9f, 106.1f), null), (47808, 5, (99.7f, 103.2f), null), (47808, 3, (99.8f, 110.5f), null)],
            [(47809, 7, (104.8f, 108.8f), null), (47810, 1, (110.4f, 100.8f), (105.1f, 102.1f)), (47810, 2, (89.7f, 100.1f), (95.3f, 102.2f)), (47809, 8, (94.3f, 109.1f), null), (47833, 5, (105.1f, 102.1f), (101.4f, 100.6f)), (47833, 4, (103.8f, 95.3f), (101.3f, 98.4f)), (47831, 6, (95.2f, 102.2f), (100.0f, 100.0f)), (47833, 3, (96.5f, 95.3f), (98.9f, 98.5f))],
            [(47808, 7, (100.8f, 104.9f), null), (47808, 1, (98.4f, 103.2f), null), (47808, 4, (98.5f, 102.9f), null), (47809, 5, (97.3f, 110.4f), null), (47808, 8, (93.7f, 100.0f), null), (47808, 3, (96.5f, 100.1f), null), (47808, 6, (89.3f, 100.0f), null), (47810, 2, (87.5f, 100.4f), (89.4f, 100.0f))],
            [(47810, 1, (99.6f, 110.5f), (97.9f, 105.0f)), (47809, 7, (91.0f, 105.2f), null), (47809, 8, (91.0f, 94.2f), null), (47810, 2, (99.6f, 89.6f), (97.7f, 95.4f)), (47832, 5, (97.9f, 105.0f), (99.4f, 101.3f)), (47832, 4, (104.7f, 103.6f), (101.5f, 101.1f)), (47830, 6, (97.7f, 95.3f), (100.0f, 100.0f)), (47832, 3, (104.6f, 96.5f), (101.4f, 98.9f))],
            [(47809, 7, (89.6f, 97.9f), null), (47808, 5, (95.0f, 100.9f), null), (47808, 4, (96.7f, 98.7f), null), (47808, 1, (96.9f, 98.5f), null), (47808, 6, (99.9f, 94.0f), null), (47808, 3, (99.9f, 96.7f), null), (47808, 8, (99.9f, 89.3f), null), (47810, 2, (100.1f, 87.6f), (99.9f, 89.3f))],
            [(47810, 7, (89.9f, 98.6f), (95.4f, 97.9f)), (47809, 4, (93.7f, 90.9f), null), (47810, 6, (110.5f, 99.0f), (104.7f, 97.8f)), (47809, 3, (106.2f, 90.7f), null), (47832, 5, (104.1f, 104.6f), (101.5f, 101.6f)), (47832, 8, (96.0f, 105.4f), (98.4f, 102.2f)), (47832, 2, (104.7f, 97.7f), (101.1f, 99.5f)), (47830, 1, (95.4f, 97.9f), (100.0f, 100.0f))],
        ];

        // All Things Ending casters per round: x, z and the direction the cone points (degrees clockwise from north).
        (float X, float Z, float Facing)[][] clones =
        [
            [], [], [],
            [(100.5f, 101.1f, 310f), (98.3f, 101.4f, 302f), (97.9f, 98.4f, 313f), (100.0f, 100.0f, 315f)],
            [],
            [(101.4f, 100.6f, 54f), (100.0f, 100.0f, 45f), (101.3f, 98.4f, 46f), (98.9f, 98.5f, 35f)],
            [],
            [(101.5f, 101.1f, 133f), (99.4f, 101.3f, 125f), (101.4f, 98.9f, 149f), (100.0f, 100.0f, 136f)],
            [],
        ];

        for (var number = 0; number < positions.Length; number++)
        {
            var round = new ForsakenRound { Number = number };
            foreach (var (x, z, facing) in clones[number])
            {
                round.Clones.Add(new ForsakenCloneRecord { X = x, Z = z, Facing = facing });
            }
            for (var player = 0; player < positions[number].Length; player++)
            {
                round.Spots.Add(new ForsakenSpot
                {
                    EntityId = (uint)(player + 1),
                    X = positions[number][player].X,
                    Z = positions[number][player].Z,
                });
            }

            foreach (var (x, z, soakers) in towers[number])
            {
                round.Towers.Add(new ForsakenTowerRecord
                {
                    X = x,
                    Z = z,
                    Soakers = soakers.Select(soaker => (uint)soaker).ToList(),
                });
            }

            foreach (var (action, target, position, caster) in attacks[number])
            {
                round.Attacks.Add(new ForsakenAttackRecord
                {
                    ActionId = action,
                    TargetEntityId = (uint)target,
                    TargetX = position.X,
                    TargetZ = position.Z,
                    CasterX = caster?.X,
                    CasterZ = caster?.Z,
                });
            }

            run.Rounds.Add(round);
        }

        return run;
    }

    /// <summary>Lists the run in progress first, followed by saved runs, newest first.</summary>
    private List<ForsakenRun> GetForsakenRuns()
    {
        var runs = new List<ForsakenRun>();
        if (plugin.P2ForsakenTracker.CurrentRun is { } liveRun)
        {
            runs.Add(liveRun);
        }

        runs.AddRange(plugin.ForsakenHistory.Runs);
        return runs;
    }

    private string DescribeForsakenRun(ForsakenRun run)
    {
        if (ReferenceEquals(run, plugin.P2ForsakenTracker.CurrentRun))
        {
            return LiveRunLabel;
        }

        var label = run.StartedUtc.ToLocalTime().ToString("g");
        label = run.Completed ? label : $"{label} (incomplete)";
        var mistakes = run.Rounds.Sum(round => ForsakenStrategy.CountMistakes(run, round, ForsakenPositionOf));
        return mistakes > 0 ? $"{label} - {mistakes} mistake{(mistakes == 1 ? string.Empty : "s")}" : label;
    }

    /// <summary>The player's position as saved with the run, or for older runs the one configured for their name.</summary>
    private RaidPosition ForsakenPositionOf(ForsakenPlayerRecord player)
        => player.Position != RaidPosition.None ? player.Position : plugin.Configuration.GetPosition(player.Name);

    /// <summary>
    /// Sets up the columns shared by the round tabs and the marker table, so a tab sits right above its column: a
    /// pair column, the player, one equal column per tower and optionally the final bait.
    /// </summary>
    private static void SetupForsakenColumns(bool finalBaitColumn)
    {
        ImGui.TableSetupColumn("Pair", ImGuiTableColumnFlags.WidthFixed, ForsakenPairColumnWidth);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, ForsakenPlayerColumnWidth);
        for (var tower = 1; tower <= ForsakenTowerColumnCount; tower++)
        {
            ImGui.TableSetupColumn($"T{tower}", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        }

        if (finalBaitColumn)
        {
            ImGui.TableSetupColumn("Bait", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        }
    }

    /// <summary>
    /// Groups the players into their buddy pairs (MT/H1, OT/H2, M1/R1, M2/R2) in that order. Players whose partner is
    /// unknown follow as single entries.
    /// </summary>
    private List<List<ForsakenPlayerRecord>> GroupForsakenPlayers(ForsakenRun run)
    {
        var remaining = run.Players.OrderByDescending(candidate => candidate.IsLocalPlayer).ToList();
        var groups = new List<List<ForsakenPlayerRecord>>();
        foreach (var (first, second) in ForsakenPairs)
        {
            var left = remaining.Find(candidate => ForsakenPositionOf(candidate) == first);
            var right = remaining.Find(candidate => ForsakenPositionOf(candidate) == second);
            if (left is null || right is null)
            {
                continue;
            }

            groups.Add([left, right]);
            remaining.Remove(left);
            remaining.Remove(right);
        }

        groups.AddRange(remaining.Select(candidate => new List<ForsakenPlayerRecord> { candidate }));
        return groups;
    }

    /// <summary>
    /// One row per player, buddies next to each other. Column Tn is the marker the player was holding during tower
    /// n, which is the one shown in the arena for that tower.
    /// </summary>
    private void DrawForsakenTable(ForsakenRun run)
    {
        var hasFinalBait = run.Rounds.Any(round => round.Number == ForsakenRound.FinalBaitNumber);
        if (!ImGui.BeginTable("##ForsakenHistoryTable", ForsakenFirstRoundColumn + ForsakenTowerColumnCount + (hasFinalBait ? 1 : 0), ImGuiTableFlags.Borders))
        {
            return;
        }

        SetupForsakenColumns(hasFinalBait);
        ImGui.TableHeadersRow();
        var alphaTint = ImGui.GetColorU32(new Vector4(0.22f, 0.38f, 0.62f, 0.35f));
        var betaTint = ImGui.GetColorU32(new Vector4(0.62f, 0.42f, 0.15f, 0.35f));
        var alphaText = ImGui.GetColorU32(new Vector4(0.55f, 0.75f, 1.0f, 1.0f));
        var betaText = ImGui.GetColorU32(new Vector4(1.0f, 0.75f, 0.4f, 1.0f));
        var lineColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.9f));
        var drawList = ImGui.GetWindowDrawList();
        foreach (var group in GroupForsakenPlayers(run))
        {
            // Buddies who started on different markers are Alpha, matching markers mean Beta.
            bool? isAlpha = group.Count == 2
                && group[0].TryGetMarker(0, out var firstMarker)
                && group[1].TryGetMarker(0, out var secondMarker)
                    ? firstMarker != secondMarker
                    : null;
            var tint = isAlpha switch
            {
                true => alphaTint,
                false => betaTint,
                _ => 0u,
            };

            var firstRowCenterY = 0.0f;
            for (var index = 0; index < group.Count; index++)
            {
                var player = group[index];
                ImGui.TableNextRow(ImGuiTableRowFlags.None, IconSize);
                if (tint != 0u)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, tint);
                }

                ImGui.TableSetColumnIndex(0);
                var cell = ImGui.GetCursorScreenPos();
                var centerY = cell.Y + (IconSize / 2.0f);
                if (index == 0)
                {
                    firstRowCenterY = centerY;
                }
                else if (isAlpha is bool alpha)
                {
                    // A bracket from the first buddy down to this one, with the group in the middle.
                    var x = cell.X + 6.0f;
                    drawList.AddLine(new Vector2(x, firstRowCenterY), new Vector2(x, centerY), lineColor, 2.0f);
                    drawList.AddLine(new Vector2(x, firstRowCenterY), new Vector2(x + 8.0f, firstRowCenterY), lineColor, 2.0f);
                    drawList.AddLine(new Vector2(x, centerY), new Vector2(x + 8.0f, centerY), lineColor, 2.0f);
                    var label = alpha ? "Alpha" : "Beta";
                    drawList.AddText(
                        new Vector2(x + 14.0f, ((firstRowCenterY + centerY) / 2.0f) - (ImGui.GetTextLineHeight() / 2.0f)),
                        alpha ? alphaText : betaText,
                        label);
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((IconSize - ImGui.GetTextLineHeight()) / 2.0f));
                ImGui.TextUnformatted(player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name);

                for (var tower = 1; tower <= ForsakenTowerColumnCount; tower++)
                {
                    ImGui.TableSetColumnIndex(ForsakenFirstRoundColumn + tower - 1);
                    var towerRound = run.Rounds.Find(round => round.Number == tower);
                    if (towerRound is not null && run.TryGetMarkerDuring(player, tower, out var markerId))
                    {
                        // A player who is not in a tower this round will not have their mechanic resolve, so gray.
                        var inTower = towerRound.Towers.Any(candidate => candidate.Soakers.Contains(player.EntityId));
                        var free = ImGui.GetContentRegionAvail().X - IconSize;
                        if (free > 0.0f)
                        {
                            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (free / 2.0f));
                        }

                        DrawForsakenMarker(markerId, !inTower);
                    }
                }
            }
        }

        ImGui.EndTable();
    }

    private static string? ForsakenMarkerImage(uint markerId, bool gray = false)
    {
        return markerId switch
        {
            715 => gray ? "stack_gray.png" : "stack.png",
            716 => gray ? "spread_gray.png" : "spread.png",
            717 => gray ? "cone_gray.png" : "cone.png",
            _ => null,
        };
    }

    private void DrawForsakenMarker(uint markerId, bool gray)
    {
        var imageName = ForsakenMarkerImage(markerId, gray);
        if (imageName is null)
        {
            ImGui.TextDisabled(markerId.ToString());
            return;
        }

        DrawImage(imageName);
    }
}
