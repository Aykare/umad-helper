using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Windows;

public partial class HistoryWindow
{
    private const float ArenaCenter = 100.0f;
    private const float ArenaRadiusYalms = 20.0f;
    private const float ViewRadiusYalms = 22.5f;

    // Players standing correctly in the logs are 18.4 to 19.5 yalms from the center, so the rings sit just inside the edge.
    private const float TargetRadiusYalms = 19.2f;
    private const float MinArenaSize = 260.0f;
    private const float MaxArenaSize = 420.0f;
    private const float DotRadius = 13.0f;
    private const float CloneRadius = 8.0f;

    // Waymark ground footprint sizes, taken from the WaymarkStudio plugin (sourpuh/ffxiv_waymarkstudio).
    private const float WaymarkCircleRadiusYalms = 1.25f;
    private const float WaymarkSquareHalfWidthYalms = 1.1f;

    // The line attack is a rectangle of width 6 (the Action sheet's XAxisModifier for action 47844).
    private const float LineWidthYalms = 6.0f;

    // A square icon fits inside the status-coloured circle behind it when it is about 1.7x the radius wide.
    private const float JobIconScale = 1.7f;
    private const float WaymarkSize = 26.0f;

    private int selectedLimitCutIndex;
    private bool limitCutPreviewActive;
    private LimitCutRun? limitCutPreviewRun;

    private void DrawLimitCutTab()
    {
        if (limitCutPreviewActive)
        {
            limitCutPreviewRun ??= CreateSampleLimitCutRun();
            ImGui.TextDisabled("Preview: sample data from a real pull. Not saved.");
            DrawLimitCutSummary(limitCutPreviewRun);
            DrawLimitCutArena(limitCutPreviewRun);

            ImGui.Spacing();
            if (ImGui.Button("Close preview##LimitCut"))
            {
                limitCutPreviewActive = false;
            }

            return;
        }

        var runs = GetLimitCutRuns();
        if (runs.Count == 0)
        {
            ImGui.TextDisabled("No Limit Cut runs recorded yet.");
            return;
        }

        selectedLimitCutIndex = DrawRunPicker("LimitCut", runs, DescribeLimitCutRun, selectedLimitCutIndex);
        var selectedRun = runs[selectedLimitCutIndex];
        DrawLimitCutSummary(selectedRun);
        DrawLimitCutArena(selectedRun);

        ImGui.Spacing();
        if (DrawClearButton("LimitCut"))
        {
            plugin.LimitCutHistory.Clear();
            selectedLimitCutIndex = 0;
        }
    }

    /// <summary>
    /// Builds a run from a real pull: first clone at marker 1, rotating clockwise. Positions are the logged ones,
    /// except player 6, who is moved next to player 7 so the preview also shows a mistake.
    /// </summary>
    private static LimitCutRun CreateSampleLimitCutRun()
    {
        var run = new LimitCutRun
        {
            StartedUtc = DateTime.UtcNow,
            OriginIndex = 7,
            Rotation = LimitCutRotation.Clockwise,
        };

        (float X, float Z)[] positions =
        [
            (107.0f, 117.5f),
            (117.4f, 108.1f),
            (117.8f, 93.3f),
            (106.4f, 81.9f),
            (92.3f, 82.6f),
            (82.0f, 106.0f),
            (81.8f, 107.0f),
            (92.5f, 117.4f),
        ];

        // Warrior, dragoon, monk, samurai, bard, black mage, white mage and scholar for numbers 1 to 8.
        uint[] jobs = [21, 22, 20, 34, 23, 25, 24, 28];

        for (var number = 1; number <= 8; number++)
        {
            var player = new LimitCutPlayerRecord
            {
                EntityId = (uint)number,
                Name = $"Player {number}",
                IsLocalPlayer = number == 7,
                Number = number,
                JobId = jobs[number - 1],
                ActualX = positions[number - 1].X,
                ActualZ = positions[number - 1].Z,
            };

            // Clone n stands n-1 steps clockwise from the first clone and aims its line at the player.
            var cloneAngle = (((7 + number - 1) % 8) * 45.0f) * MathF.PI / 180.0f;
            player.CloneX = ArenaCenter + (ArenaRadiusYalms * MathF.Sin(cloneAngle));
            player.CloneZ = ArenaCenter - (ArenaRadiusYalms * MathF.Cos(cloneAngle));
            var aimDegrees = MathF.Atan2(player.ActualX.Value - player.CloneX.Value, -(player.ActualZ.Value - player.CloneZ.Value))
                * 180.0f / MathF.PI;
            player.AimFacing = (aimDegrees + 360.0f) % 360.0f;
            run.Players.Add(player);
        }

        return run;
    }

    /// <summary>Lists the run in progress first, followed by saved runs, newest first.</summary>
    private List<LimitCutRun> GetLimitCutRuns()
    {
        var runs = new List<LimitCutRun>();
        if (plugin.LimitCutTracker.CurrentRun is { } liveRun)
        {
            runs.Add(liveRun);
        }

        runs.AddRange(plugin.LimitCutHistory.Runs);
        return runs;
    }

    private string DescribeLimitCutRun(LimitCutRun run)
    {
        return ReferenceEquals(run, plugin.LimitCutTracker.CurrentRun)
            ? LiveRunLabel
            : run.StartedUtc.ToLocalTime().ToString("g");
    }

    private static void DrawLimitCutSummary(LimitCutRun run)
    {
        if (run.OriginIndex is not int origin)
        {
            return;
        }

        ImGui.TextDisabled(
            $"Origin: {LimitCutStrategy.RingLabels[origin]}, rotation: {LimitCutStrategy.RotationName(run.Rotation)}");
    }

    /// <summary>
    /// Draws the arena from above (north up): a ring where each player should have stood and a filled dot where they
    /// actually stood, joined by a red line when they were off.
    /// </summary>
    private void DrawLimitCutArena(LimitCutRun run)
    {
        var drawList = BeginArena("##LimitCutArena", out var center, out var scale, out var arenaHovered, ForsakenArenaMaxSize);

        for (var index = 0; index < LimitCutStrategy.RingLabels.Length; index++)
        {
            var label = LimitCutStrategy.RingLabels[index];
            DrawWaymark(
                drawList,
                LimitCutStrategy.ImageName(label),
                center + (new Vector2(
                    LimitCutStrategy.WaymarkPositions[index].X - ArenaCenter,
                    LimitCutStrategy.WaymarkPositions[index].Z - ArenaCenter) * scale),
                char.IsLetter(label[0]),
                scale);
        }

        var onPosition = new Vector4(0.2f, 0.7f, 0.35f, 1.0f);
        var offPosition = new Vector4(0.85f, 0.25f, 0.25f, 1.0f);
        DrawLimitCutLines(drawList, run, center, scale);
        var wrongPlayers = new List<(LimitCutPlayerRecord Player, int Expected, int Actual)>();
        var markers = new List<(LimitCutPlayerRecord Player, Vector2 Position, bool Wrong)>();

        foreach (var player in run.Players.OrderBy(candidate => candidate.Number))
        {
            int? expectedGap = run.OriginIndex is int origin
                && LimitCutStrategy.TryGetFinalGap(run.Rotation, origin, player.Number, out var gapIndex)
                    ? gapIndex
                    : null;

            Vector2? actualPosition = null;
            int? actualGap = null;
            if (player is { ActualX: float x, ActualZ: float z })
            {
                actualPosition = center + (new Vector2(x - ArenaCenter, z - ArenaCenter) * scale);
                actualGap = LimitCutStrategy.GetActualGap(x, z, expectedGap);
            }

            var wrong = expectedGap is int expected && actualGap is int actual && expected != actual;
            if (expectedGap is int expectedIndex)
            {
                var expectedPosition = center + PolarOffset((expectedIndex * 45.0f) + 22.5f, TargetRadiusYalms * scale);
                var ringColor = wrong ? offPosition : new Vector4(0.7f, 0.72f, 0.78f, 0.9f);
                drawList.AddCircle(expectedPosition, DotRadius, ImGui.GetColorU32(ringColor), 24, 2.0f);

                if (wrong && actualPosition is { } stood)
                {
                    drawList.AddLine(stood, expectedPosition, ImGui.GetColorU32(offPosition), 2.0f);
                    wrongPlayers.Add((player, expectedIndex, actualGap!.Value));
                }
            }

            if (actualPosition is { } position)
            {
                markers.Add((player, position, wrong));
            }
        }

        var mouse = ImGui.GetMousePos();
        LimitCutPlayerRecord? hoveredPlayer = null;
        foreach (var (player, position, wrong) in markers)
        {
            // With a job icon the dot is only drawn to flag a mistake; the icon alone means the player was on position.
            var iconName = JobIcons.ImageName(player.JobId);
            if (wrong || iconName is null)
            {
                drawList.AddCircleFilled(position, DotRadius, ImGui.GetColorU32(wrong ? offPosition : onPosition), 24);
            }

            if (iconName is null || !TryDrawIcon(drawList, iconName, position, DotRadius * JobIconScale))
            {
                var label = player.Number.ToString();
                drawList.AddText(position - (ImGui.CalcTextSize(label) * 0.5f), ImGui.GetColorU32(Vector4.One), label);
            }

            if (Vector2.Distance(mouse, position) <= DotRadius + 2.0f)
            {
                hoveredPlayer = player;
            }
        }

        if (arenaHovered && hoveredPlayer is not null)
        {
            ImGui.SetTooltip(hoveredPlayer.IsLocalPlayer
                ? $"{hoveredPlayer.Name} (you), #{hoveredPlayer.Number}"
                : $"{hoveredPlayer.Name}, #{hoveredPlayer.Number}");
        }

        ImGui.TextDisabled("Ring: where to stand. Icon: where they stood (red circle: off position). Orange: clone lines. Purple squares: clones.");
        if (markers.Count == 0)
        {
            ImGui.TextDisabled("No positions were recorded for this run.");
            return;
        }

        ImGui.TextUnformatted($"{markers.Count - wrongPlayers.Count}/{markers.Count} on position");
        foreach (var (player, expected, actual) in wrongPlayers)
        {
            ImGui.TextColored(
                offPosition,
                $"#{player.Number} {player.Name}: should be {DescribeGap(expected)}, stood {DescribeGap(actual)}");
        }
    }

    /// <summary>
    /// Reserves a centered square canvas and draws the empty arena on it. The out values map world coordinates to
    /// the screen: <c>center + ((worldX - ArenaCenter, worldZ - ArenaCenter) * scale)</c>, with north up.
    /// </summary>
    private static ImDrawListPtr BeginArena(string id, out Vector2 center, out float scale, out bool hovered, float maxSize = MaxArenaSize)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var side = Math.Clamp(MathF.Min(available, maxSize), MinArenaSize, maxSize);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0.0f, (available - side) * 0.5f));

        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(side, side));
        hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        center = topLeft + new Vector2(side * 0.5f);
        scale = side * 0.5f / ViewRadiusYalms;

        drawList.AddCircleFilled(center, ArenaRadiusYalms * scale, ImGui.GetColorU32(new Vector4(0.1f, 0.11f, 0.14f, 1.0f)), 64);
        drawList.AddCircle(center, ArenaRadiusYalms * scale, ImGui.GetColorU32(new Vector4(0.45f, 0.48f, 0.55f, 1.0f)), 64, 2.0f);
        return drawList;
    }

    /// <summary>
    /// Draws each clone's line attack (a rectangle from the clone through its target, cut off at the arena edge)
    /// and a small marker on the clone showing which number it belongs to.
    /// </summary>
    private static void DrawLimitCutLines(ImDrawListPtr drawList, LimitCutRun run, Vector2 center, float scale)
    {
        var fill = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.15f, 0.25f));
        var border = ImGui.GetColorU32(new Vector4(1.0f, 0.6f, 0.25f, 0.8f));
        var clones = new List<(int Number, Vector2 Position)>();

        foreach (var player in run.Players.OrderBy(candidate => candidate.Number))
        {
            if (player is not { CloneX: float cloneX, CloneZ: float cloneZ, AimFacing: float facing })
            {
                continue;
            }

            var radians = facing * MathF.PI / 180.0f;
            var direction = new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
            var side = new Vector2(-direction.Y, direction.X);

            // Distance along the aim until the line leaves the arena circle, so it is not drawn past the edge.
            var relative = new Vector2(cloneX - ArenaCenter, cloneZ - ArenaCenter);
            var length = DistanceToArenaEdge(relative, direction);

            var start = center + (relative * scale);
            var end = start + (direction * length * scale);
            var half = side * (LineWidthYalms * 0.5f * scale);
            drawList.AddQuadFilled(start - half, start + half, end + half, end - half, fill);
            drawList.AddQuad(start - half, start + half, end + half, end - half, border, 1.5f);
            clones.Add((player.Number, start));
        }

        foreach (var (number, position) in clones)
        {
            var half = new Vector2(CloneRadius);
            var outline = new Vector2(CloneRadius + 1.5f);
            drawList.AddRectFilled(position - outline, position + outline, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)));
            drawList.AddRectFilled(position - half, position + half, ImGui.GetColorU32(new Vector4(0.8f, 0.5f, 1.0f, 1.0f)));
            var label = number.ToString();
            drawList.AddText(position - (ImGui.CalcTextSize(label) * 0.5f), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)), label);
        }
    }

    /// <summary>
    /// Distance in yalms from a point (relative to the arena center) along a unit direction until the arena circle
    /// is left; zero if the point is already outside.
    /// </summary>
    private static float DistanceToArenaEdge(Vector2 relative, Vector2 direction)
    {
        var along = Vector2.Dot(relative, direction);
        var discriminant = (along * along) - relative.LengthSquared() + (ArenaRadiusYalms * ArenaRadiusYalms);
        return MathF.Max(0.0f, -along + MathF.Sqrt(MathF.Max(0.0f, discriminant)));
    }

    /// <summary>Describes a gap by its two bordering waymarks, for example "3-C".</summary>
    private static string DescribeGap(int gapIndex)
    {
        var (first, second) = LimitCutStrategy.GapLabels(gapIndex);
        return $"{first}-{second}";
    }

    /// <summary>Converts a clockwise angle from north and a pixel distance into a screen offset (north is up).</summary>
    private static Vector2 PolarOffset(float angleDegrees, float distance)
    {
        var radians = angleDegrees * MathF.PI / 180.0f;
        return new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * distance;
    }

    /// <summary>Draws a waymark's real ground footprint (circle for A-D, square for 1-4) with its icon on top.</summary>
    private void DrawWaymark(ImDrawListPtr drawList, string imageName, Vector2 centerPosition, bool isCircle, float scale)
    {
        // The stroke is centered on its path, so the fill stops one stroke width short of the footprint edge and the
        // stroke fills that gap; otherwise it would overlap the translucent fill and look brighter than it.
        const float strokeWidth = 1.5f;
        var fill = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.22f));
        var outline = fill;
        if (isCircle)
        {
            var radius = WaymarkCircleRadiusYalms * scale;
            drawList.AddCircleFilled(centerPosition, radius - strokeWidth, fill, 32);
            drawList.AddCircle(centerPosition, radius - (strokeWidth * 0.5f), outline, 32, strokeWidth);
        }
        else
        {
            var half = new Vector2(WaymarkSquareHalfWidthYalms * scale);
            var fillHalf = half - new Vector2(strokeWidth);
            var strokeHalf = half - new Vector2(strokeWidth * 0.5f);
            drawList.AddRectFilled(centerPosition - fillHalf, centerPosition + fillHalf, fill);
            drawList.AddRect(centerPosition - strokeHalf, centerPosition + strokeHalf, outline, 0.0f, ImDrawFlags.None, strokeWidth);
        }

        TryDrawIcon(drawList, imageName, centerPosition, WaymarkSize);
    }

    /// <summary>Draws a square image centered on a screen position; returns false if it is not loaded.</summary>
    private bool TryDrawIcon(ImDrawListPtr drawList, string imageName, Vector2 centerPosition, float size)
    {
        var texture = Plugin.TextureProvider.GetFromFile(System.IO.Path.Combine(markerImageDirectory, imageName));
        if (texture.GetWrapOrDefault() is not { } image)
        {
            return false;
        }

        var half = new Vector2(size * 0.5f);
        drawList.AddImage(image.Handle, centerPosition - half, centerPosition + half);
        return true;
    }
}
