using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;

namespace SamplePlugin.Windows;

public partial class HistoryWindow
{
    private static readonly Vector4 KefkaRealRing = new(0.55f, 0.9f, 0.6f, 1.0f);
    private static readonly Vector4 KefkaFakeRing = new(1.0f, 0.62f, 0.35f, 1.0f);
    private static readonly Vector4 KefkaWrongRing = new(0.9f, 0.25f, 0.25f, 1.0f);
    private static readonly Vector4 KefkaNeutralRing = new(0.7f, 0.72f, 0.78f, 0.9f);
    private static readonly Vector4 KefkaWhiteAntilightColor = new(0.8f, 0.5f, 1.0f, 1.0f);
    private static readonly Vector4 KefkaBlackAntilightColor = new(0.3f, 0.6f, 1.0f, 1.0f);

    // Boss positions seen in the logs; Kefka stands in the middle.
    private static readonly (string Letter, float X, float Z)[] KefkaBosses =
    [
        ("K", ArenaCenter, ArenaCenter),
        ("N", 114.1f, 85.9f),
        ("C", 87.3f, 87.3f),
    ];

    // Status icons are tall (24x32), so they are drawn at that ratio.
    private static readonly Vector2 KefkaStatusIconSize = new(12.0f, 16.0f);

    private readonly Dictionary<uint, uint> kefkaStatusIconIds = [];

    // The area sizes are estimates; the logs do not record the radius of the stack and spread attacks.
    private const float KefkaStackRadiusYalms = 6.0f;
    private const float KefkaSpreadRadiusYalms = 5.0f;
    private const float KefkaMarkerToleranceYalms = 5.0f;
    private static readonly Vector4 KefkaStackColor = new(0.3f, 0.6f, 1.0f, 1.0f);
    private static readonly Vector4 KefkaSpreadColor = new(0.8f, 0.5f, 1.0f, 1.0f);

    private int selectedKefkaIndex;
    private int selectedKefkaStep;
    private bool kefkaPreviewActive;
    private KefkaSaysRun? kefkaPreviewRun;

    private void DrawKefkaSaysTab()
    {
        if (kefkaPreviewActive)
        {
            kefkaPreviewRun ??= KefkaSaysSample.Create();
            ImGui.TextDisabled("Preview: sample data with made-up positions. Not saved.");
            DrawKefkaSaysRun(kefkaPreviewRun);

            ImGui.Spacing();
            if (ImGui.Button("Close preview##KefkaSays"))
            {
                kefkaPreviewActive = false;
            }

            return;
        }

        var runs = GetKefkaSaysRuns();
        if (runs.Count == 0)
        {
            ImGui.TextDisabled("No Kefka Says runs recorded yet.");
            return;
        }

        selectedKefkaIndex = DrawRunPicker("KefkaSays", runs, DescribeKefkaSaysRun, selectedKefkaIndex);
        DrawKefkaSaysRun(runs[selectedKefkaIndex]);

        ImGui.Spacing();
        if (DrawClearButton("KefkaSays"))
        {
            plugin.KefkaSaysHistory.Clear();
            selectedKefkaIndex = 0;
        }
    }

    /// <summary>Lists the run in progress first, followed by saved runs, newest first.</summary>
    private List<KefkaSaysRun> GetKefkaSaysRuns()
    {
        var runs = new List<KefkaSaysRun>();
        if (plugin.KefkaSaysTracker.CurrentRun is { } liveRun)
        {
            runs.Add(liveRun);
        }

        runs.AddRange(plugin.KefkaSaysHistory.Runs);
        return runs;
    }

    private string DescribeKefkaSaysRun(KefkaSaysRun run)
    {
        return ReferenceEquals(run, plugin.KefkaSaysTracker.CurrentRun)
            ? LiveRunLabel
            : run.StartedUtc.ToLocalTime().ToString("g");
    }

    /// <summary>Draws the step picker, the arena of the chosen step, the player list and the raw data tables.</summary>
    private void DrawKefkaSaysRun(KefkaSaysRun run)
    {
        var steps = Enum.GetValues<KefkaStep>();
        foreach (var step in steps)
        {
            var recorded = run.Steps.Any(record => record.Step == step);
            var selected = (int)step == selectedKefkaStep;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.75f, 1.0f));
            }

            ImGui.BeginDisabled(!recorded);
            if (ImGui.Button($"{(int)step + 1}##KefkaStep{(int)step}"))
            {
                selectedKefkaStep = (int)step;
            }

            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(KefkaSaysRules.StepName(step));
            }

            if (selected)
            {
                ImGui.PopStyleColor();
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        var record2 = run.Steps.FirstOrDefault(candidate => (int)candidate.Step == selectedKefkaStep)
            ?? run.Steps.OrderBy(candidate => candidate.Step).FirstOrDefault();
        if (record2 is null)
        {
            ImGui.TextDisabled("No steps were recorded for this run.");
        }
        else
        {
            selectedKefkaStep = (int)record2.Step;
            DrawKefkaStep(run, record2);
        }

        if (ImGui.CollapsingHeader("Debuffs##KefkaSays"))
        {
            DrawKefkaDebuffTable(run);
        }

        if (ImGui.CollapsingHeader("Signals##KefkaSays"))
        {
            DrawKefkaSignalTable(run);
        }
    }

    /// <summary>Draws one resolved step: its title and result, the arena with every player, and what each had to do.</summary>
    private void DrawKefkaStep(KefkaSaysRun run, KefkaStepRecord record)
    {
        var result = record.Result.Length > 0 ? $": {record.Result}" : string.Empty;
        ImGui.TextUnformatted($"{(int)record.Step + 1}. {KefkaSaysRules.StepName(record.Step)} ({record.Seconds:0.0}s){result}");

        var isFlood = record.Step == KefkaStep.FloodOfNaught;
        if (isFlood)
        {
            var reality = KefkaSaysRules.StepReality(run, KefkaStep.FloodOfNaught);
            ImGui.TextColored(
                reality switch
                {
                    KefkaReality.Real => KefkaRealRing,
                    KefkaReality.Fake => KefkaFakeRing,
                    _ => KefkaNeutralRing,
                },
                $"Flood of Naught cast: {KefkaSaysRules.RealityName(reality)}");
        }

        var drawList = BeginArena("##KefkaArena", out var center, out var scale, out var arenaHovered, ForsakenArenaMaxSize);
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

        if (isFlood)
        {
            DrawKefkaAntilightAreas(drawList, record, center, scale);
        }

        var isStackSpread = record.Step is KefkaStep.ShortStackSpread or KefkaStep.LongStackSpread;
        if (isStackSpread)
        {
            DrawKefkaStackSpreadAreas(drawList, record, center, scale);
        }

        foreach (var (letter, bossX, bossZ) in KefkaBossesFor(record, isFlood))
        {
            var bossPosition = center + (new Vector2(bossX - ArenaCenter, bossZ - ArenaCenter) * scale);
            var half = new Vector2(7.0f);
            drawList.AddRectFilled(bossPosition - half, bossPosition + half, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.36f, 1.0f)));
            drawList.AddText(bossPosition - (ImGui.CalcTextSize(letter) * 0.5f), ImGui.GetColorU32(Vector4.One), letter);
        }

        var mouse = ImGui.GetMousePos();
        string? hoverText = null;
        var positioned = 0;
        foreach (var stepPlayer in record.Players)
        {
            var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == stepPlayer.EntityId);
            if (player is null || stepPlayer is not { X: float x, Z: float z })
            {
                continue;
            }

            positioned++;
            var position = center + (new Vector2(x - ArenaCenter, z - ArenaCenter) * scale);
            var ring = KefkaRingColor(run, record, player, stepPlayer);
            var iconCenter = position;
            if (isFlood)
            {
                // The exact spot is a small dot (green: right half, red: wrong half) with the job icon beside it.
                iconCenter = position + IconOffset;
                drawList.AddLine(position, iconCenter, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.35f)), 1.0f);
                drawList.AddCircleFilled(position, PositionDotRadius + 1.5f, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)), 16);
                drawList.AddCircleFilled(position, PositionDotRadius, ImGui.GetColorU32(KefkaFloodDotColor(player, stepPlayer)), 16);
                if (Vector2.Distance(mouse, position) <= PositionDotRadius + 4.0f)
                {
                    hoverText = DescribeKefkaFloodDot(player, stepPlayer);
                }
            }
            else
            {
                drawList.AddCircle(position, DotRadius + 2.0f, ImGui.GetColorU32(ring), 24, stepPlayer.Involved ? 3.0f : 1.5f);
            }

            var iconName = JobIcons.ImageName(player.JobId);
            if (iconName is null || !TryDrawIcon(drawList, iconName, iconCenter, DotRadius * JobIconScale))
            {
                drawList.AddCircleFilled(iconCenter, DotRadius, ImGui.GetColorU32(new Vector4(0.25f, 0.3f, 0.4f, 1.0f)), 24);
            }

            if (Vector2.Distance(mouse, iconCenter) <= DotRadius + 2.0f)
            {
                hoverText = DescribeKefkaPlayer(player, stepPlayer);
            }

            if (isFlood && DrawKefkaFloodStatusIcons(drawList, player, iconCenter, mouse) is { } statusHover)
            {
                hoverText = statusHover;
            }
        }

        if (isStackSpread)
        {
            DrawKefkaMarkerMistakes(drawList, run, record, center, scale, mouse, ref hoverText);
        }

        if (arenaHovered && hoverText is not null)
        {
            ImGui.SetTooltip(hoverText);
        }

        ImGui.TextDisabled(isFlood
            ? "Dot: where the player stood when the Antilights hit (green: stood in the right half, red: wrong half, gray: unknown). Status icons beside the job icon: wound, Allagan Field or Beyond Death. Purple: White Antilight, blue: Black Antilight. K, N: Kefka and Neo Exdeath (between its two Antilight clones)."
            : isStackSpread
                ? "Icon: where the holder stood when the debuff resolved. Blue area: stack, purple area: spread (sizes are estimates). Supports stack on A and spread on D, DPS stack on C and spread on B; a red arrow points to the mark a holder missed. K, N, C: Kefka, Neo Exdeath, Chaos."
                : "Icon: where the player stood when the step resolved. Ring: green real, orange fake, gray not involved. K, N, C: Kefka, Neo Exdeath, Chaos.");
        if (positioned == 0)
        {
            ImGui.TextDisabled("No positions were recorded for this step.");
        }

        foreach (var stepPlayer in record.Players)
        {
            var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == stepPlayer.EntityId);
            if (player is null || (stepPlayer.Label.Length == 0 && stepPlayer.Actual is null))
            {
                continue;
            }

            ImGui.TextColored(KefkaRingColor(run, record, player, stepPlayer), DescribeKefkaPlayer(player, stepPlayer));
        }
    }

    /// <summary>
    /// Draws the area of every stack (blue) and spread (purple) of a step, centered on the player who held the debuff.
    /// </summary>
    private static void DrawKefkaStackSpreadAreas(ImDrawListPtr drawList, KefkaStepRecord record, Vector2 center, float scale)
    {
        foreach (var stepPlayer in record.Players)
        {
            if (!stepPlayer.Involved || stepPlayer.X is not float x || stepPlayer.Z is not float z)
            {
                continue;
            }

            var (color, radius) = stepPlayer.Label switch
            {
                "Stack" => (KefkaStackColor, KefkaStackRadiusYalms),
                "Spread" => (KefkaSpreadColor, KefkaSpreadRadiusYalms),
                _ => (Vector4.Zero, 0.0f),
            };
            if (radius <= 0.0f)
            {
                continue;
            }

            var position = center + (new Vector2(x - ArenaCenter, z - ArenaCenter) * scale);
            drawList.AddCircleFilled(position, radius * scale, ImGui.GetColorU32(color with { W = 0.2f }), 48);
            drawList.AddCircle(position, radius * scale, ImGui.GetColorU32(color with { W = 0.9f }), 48, 2.0f);
        }
    }

    /// <summary>The mark a stack or spread holder has to stand on: supports stack A / spread D, DPS stack C / spread B.</summary>
    private static string? KefkaAssignedMarker(KefkaPlayerRecord player, KefkaStepPlayer stepPlayer)
    {
        if (!stepPlayer.Involved || player.JobId == 0u)
        {
            return null;
        }

        return (stepPlayer.Label, JobIcons.IsSupport(player.JobId)) switch
        {
            ("Stack", true) => "A",
            ("Spread", true) => "D",
            ("Stack", false) => "C",
            ("Spread", false) => "B",
            _ => null,
        };
    }

    /// <summary>Draws a red arrow and mark for every holder who stood too far from the mark they were assigned.</summary>
    private static void DrawKefkaMarkerMistakes(ImDrawListPtr drawList, KefkaSaysRun run, KefkaStepRecord record, Vector2 center, float scale, Vector2 mouse, ref string? hoverText)
    {
        var red = ImGui.GetColorU32(ForsakenMistakeColor);
        foreach (var stepPlayer in record.Players)
        {
            var player = run.Players.FirstOrDefault(candidate => candidate.EntityId == stepPlayer.EntityId);
            if (player is null
                || stepPlayer.X is not float x
                || stepPlayer.Z is not float z
                || KefkaAssignedMarker(player, stepPlayer) is not { } marker)
            {
                continue;
            }

            var index = Array.IndexOf(LimitCutStrategy.RingLabels, marker);
            var expected = new Vector2(LimitCutStrategy.WaymarkPositions[index].X, LimitCutStrategy.WaymarkPositions[index].Z);
            var actual = new Vector2(x, z);
            var distance = Vector2.Distance(actual, expected);
            if (distance <= KefkaMarkerToleranceYalms)
            {
                continue;
            }

            var from = center + ((actual - new Vector2(ArenaCenter)) * scale);
            var to = center + ((expected - new Vector2(ArenaCenter)) * scale);
            var dotRadius = PositionDotRadius + 3.5f;
            var direction = Vector2.Normalize(to - from);
            var tip = to - (direction * dotRadius);
            var side = new Vector2(-direction.Y, direction.X);
            drawList.AddLine(from, tip, red, 2.0f);
            drawList.AddTriangleFilled(tip, tip - (direction * 10.0f) + (side * 5.0f), tip - (direction * 10.0f) - (side * 5.0f), red);
            drawList.AddCircleFilled(to, dotRadius, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)), 20);
            drawList.AddCircleFilled(to, PositionDotRadius + 2.0f, red, 20);

            if (Vector2.Distance(mouse, to) <= PositionDotRadius + 6.0f)
            {
                hoverText = $"{player.Name} should have been here: {marker} for {stepPlayer.Label} ({distance:0.0} yalms off, {KefkaMarkerToleranceYalms:0.0} allowed)";
            }
        }
    }

    /// <summary>
    /// The bosses to draw for a step. During Flood of Naught only Kefka and Neo Exdeath are there: Neo Exdeath moves to
    /// a different spot every pull and stands exactly between its two Antilight clones, so that midpoint is used and
    /// is left out when the run has no recorded casters. Every other step uses the fixed Grand Cross positions.
    /// </summary>
    private static IEnumerable<(string Letter, float X, float Z)> KefkaBossesFor(KefkaStepRecord record, bool isFlood)
    {
        if (!isFlood)
        {
            return KefkaBosses;
        }

        var bosses = new List<(string Letter, float X, float Z)> { KefkaBosses[0] };
        var white = record.Areas.FirstOrDefault(area => area.Colour == nameof(KefkaWound.White));
        var black = record.Areas.FirstOrDefault(area => area.Colour == nameof(KefkaWound.Black));
        if (white is not null && black is not null)
        {
            bosses.Add(("N", (white.CasterX + black.CasterX) * 0.5f, (white.CasterZ + black.CasterZ) * 0.5f));
        }

        return bosses;
    }

    /// <summary>
    /// Draws the two Antilight half-room attacks. Their casters stand 20 yalms out on either side of an axis through
    /// the arena center, so the line through the center along that axis splits the arena and each attack covers the
    /// half its caster is on.
    /// </summary>
    private static void DrawKefkaAntilightAreas(ImDrawListPtr drawList, KefkaStepRecord record, Vector2 center, float scale)
    {
        var white = record.Areas.FirstOrDefault(area => area.Colour == nameof(KefkaWound.White));
        var black = record.Areas.FirstOrDefault(area => area.Colour == nameof(KefkaWound.Black));
        if (white is null || black is null)
        {
            return;
        }

        var middle = (new Vector2(white.CasterX, white.CasterZ) + new Vector2(black.CasterX, black.CasterZ)) * 0.5f;
        var origin = new Vector2(ArenaCenter);
        foreach (var (area, color) in new[] { (white, KefkaWhiteAntilightColor), (black, KefkaBlackAntilightColor) })
        {
            var side = new Vector2(area.CasterX, area.CasterZ) - middle;
            if (side.LengthSquared() < 0.01f)
            {
                continue;
            }

            DrawCone(
                drawList,
                origin,
                Vector2.Normalize(side),
                ArenaRadiusYalms,
                MathF.PI / 2.0f,
                ImGui.GetColorU32(color with { W = 0.22f }),
                ImGui.GetColorU32(color with { W = 0.9f }),
                center,
                scale);
        }
    }

    /// <summary>
    /// Draws the player's Flood of Naught statuses (the wound, then Allagan Field or Beyond Death) in a row right of
    /// their job icon. Returns the name of the status under the mouse, if any.
    /// </summary>
    private string? DrawKefkaFloodStatusIcons(ImDrawListPtr drawList, KefkaPlayerRecord player, Vector2 iconCenter, Vector2 mouse)
    {
        var statuses = new List<(uint StatusId, string Name)>();
        if (player.Wound != KefkaWound.None && player.WoundStatusId != 0u)
        {
            // Only the status as it was applied is shown; runs saved without it only hold the swapped wound from after the hit.
            statuses.Add((player.WoundStatusId, KefkaStatusName(player.WoundStatusId, player.Wound == KefkaWound.White ? "White Wound" : "Black Wound")));
        }

        var flood = player.Debuffs.FirstOrDefault(debuff => debuff.Kind is KefkaDebuffKind.AllaganField or KefkaDebuffKind.BeyondDeath);
        if (flood is not null)
        {
            var reality = flood.Kind == KefkaDebuffKind.AllaganField ? $" ({KefkaSaysRules.RealityName(flood.Reality)})" : string.Empty;
            statuses.Add((flood.StatusId, $"{(flood.Kind == KefkaDebuffKind.AllaganField ? "Allagan Field" : "Beyond Death")}{reality}"));
        }

        string? hovered = null;
        var left = iconCenter.X + (DotRadius * JobIconScale * 0.5f) + 2.0f;
        foreach (var (statusId, name) in statuses)
        {
            var topLeft = new Vector2(left, iconCenter.Y - (KefkaStatusIconSize.Y * 0.5f));
            var bottomRight = topLeft + KefkaStatusIconSize;
            if (TryDrawStatusIcon(drawList, statusId, topLeft, bottomRight))
            {
                if (mouse.X >= topLeft.X && mouse.X <= bottomRight.X && mouse.Y >= topLeft.Y && mouse.Y <= bottomRight.Y)
                {
                    hovered = $"{player.Name}: {name}";
                }

                left += KefkaStatusIconSize.X + 1.0f;
            }
        }

        return hovered;
    }

    /// <summary>The game's name of a status, or the fallback when the sheet is not available.</summary>
    private string KefkaStatusName(uint statusId, string fallback)
    {
        try
        {
            var name = Plugin.DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId)?.Name.ToString();
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not load the name of status {StatusId}.", statusId);
            return fallback;
        }
    }

    /// <summary>Draws a status's game icon (the same source the DMU helper uses); false if it is missing or not loaded yet.</summary>
    private bool TryDrawStatusIcon(ImDrawListPtr drawList, uint statusId, Vector2 topLeft, Vector2 bottomRight)
    {
        if (!kefkaStatusIconIds.TryGetValue(statusId, out var iconId))
        {
            try
            {
                iconId = Plugin.DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId)?.Icon ?? 0u;
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Could not load the icon of status {StatusId}.", statusId);
            }

            kefkaStatusIconIds[statusId] = iconId;
        }

        if (iconId == 0u || Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault() is not { } image)
        {
            return false;
        }

        drawList.AddImage(image.Handle, topLeft, bottomRight);
        return true;
    }

    /// <summary>Green when the Antilight that hit the player is the colour they had to stand in, red when not, gray when unknown.</summary>
    private static Vector4 KefkaFloodDotColor(KefkaPlayerRecord player, KefkaStepPlayer stepPlayer)
    {
        var destination = KefkaSaysRules.FloodPlan(player).Destination;
        if (stepPlayer.Actual is null || destination == KefkaWound.None)
        {
            return ForsakenMissedColor;
        }

        return stepPlayer.Actual == destination.ToString() ? KefkaRealRing : KefkaWrongRing;
    }

    private static string DescribeKefkaFloodDot(KefkaPlayerRecord player, KefkaStepPlayer stepPlayer)
    {
        var name = player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name;
        var destination = KefkaSaysRules.FloodPlan(player).Destination;
        if (stepPlayer.Actual is null)
        {
            return $"{name}: position when the Antilights hit (no hit recorded)";
        }

        var verdict = destination == KefkaWound.None
            ? string.Empty
            : stepPlayer.Actual == destination.ToString() ? ", right half" : $", wrong half (should be {destination})";
        return $"{name}: position when hit by the {stepPlayer.Actual} Antilight{verdict}";
    }

    /// <summary>The ring colour for a player in a step: red when the Flood half was wrong, otherwise by real/fake.</summary>
    private static Vector4 KefkaRingColor(KefkaSaysRun run, KefkaStepRecord record, KefkaPlayerRecord player, KefkaStepPlayer stepPlayer)
    {
        if (record.Step == KefkaStep.FloodOfNaught)
        {
            var destination = KefkaSaysRules.FloodPlan(player).Destination;
            return stepPlayer.Actual is not null && destination != KefkaWound.None && stepPlayer.Actual != destination.ToString()
                ? KefkaWrongRing
                : stepPlayer.Involved ? KefkaRealRing : KefkaNeutralRing;
        }

        if (!stepPlayer.Involved)
        {
            return KefkaNeutralRing;
        }

        var reality = record.Step switch
        {
            KefkaStep.Tsunami => KefkaSaysRules.TsunamiFinal(run),
            KefkaStep.ManaCharge1 or KefkaStep.ManaCharge2 or KefkaStep.ManaRelease => KefkaReality.Unknown,
            _ => KefkaSaysRules.StepReality(run, record.Step),
        };
        return reality switch
        {
            KefkaReality.Real => KefkaRealRing,
            KefkaReality.Fake => KefkaFakeRing,
            _ => KefkaNeutralRing,
        };
    }

    private static string DescribeKefkaPlayer(KefkaPlayerRecord player, KefkaStepPlayer stepPlayer)
    {
        var name = player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name;
        var label = stepPlayer.Label.Length > 0 ? stepPlayer.Label : "-";
        return stepPlayer.Actual is null ? $"{name}: {label}" : $"{name}: {label}, was hit by the {stepPlayer.Actual} Antilight";
    }

    /// <summary>Lists every debuff of every player with its real/fake value and expiry time.</summary>
    private static void DrawKefkaDebuffTable(KefkaSaysRun run)
    {
        if (!ImGui.BeginTable("##KefkaDebuffs", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Debuff");
        ImGui.TableSetupColumn("Expires");
        ImGui.TableHeadersRow();

        foreach (var player in run.Players)
        {
            foreach (var debuff in player.Debuffs.OrderBy(candidate => candidate.ExpiresAtSeconds))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted($"{debuff.Kind} ({KefkaSaysRules.RealityName(debuff.Reality)}): {KefkaSaysRules.Outcome(debuff.Kind, debuff.Reality)}");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted($"{debuff.ExpiresAtSeconds:0.0}s");
            }
        }

        ImGui.EndTable();
    }

    /// <summary>Lists the raw boss tells and Kefka head markers so they can be matched to the mechanics by hand.</summary>
    private static void DrawKefkaSignalTable(KefkaSaysRun run)
    {
        if (run.Signals.Count == 0)
        {
            ImGui.TextDisabled("No signals were recorded.");
            return;
        }

        if (!ImGui.BeginTable("##KefkaSignals", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Source");
        ImGui.TableSetupColumn("Value");
        ImGui.TableHeadersRow();

        foreach (var signal in run.Signals.OrderBy(candidate => candidate.Seconds))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted($"{signal.Seconds:0.0}s");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(signal.Source);
            ImGui.TableSetColumnIndex(2);
            var reality = signal.Source == KefkaSaysRules.KefkaName ? KefkaReality.Unknown : KefkaSaysRules.RealityOfTell(signal.Value);
            ImGui.TextUnformatted(reality == KefkaReality.Unknown
                ? signal.Value.ToString()
                : $"{signal.Value} ({KefkaSaysRules.RealityName(reality)})");
        }

        ImGui.EndTable();
    }
}
