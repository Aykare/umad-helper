using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Windows;

public partial class HistoryWindow
{
    private static readonly Vector4 DecisiveEpicColor = new(1.0f, 0.5f, 0.35f, 1.0f);
    private static readonly Vector4 DecisiveFatedColor = new(0.35f, 0.7f, 1.0f, 1.0f);

    private int selectedDecisiveIndex;
    private bool decisivePreviewActive;
    private DecisiveBattleRun? decisivePreviewRun;

    private void DrawDecisiveBattleTab()
    {
        if (decisivePreviewActive)
        {
            decisivePreviewRun ??= CreateSampleDecisiveBattleRun();
            ImGui.TextDisabled("Preview: sample data from a real pull. Not saved.");
            DrawDecisiveBattleRun(decisivePreviewRun);

            ImGui.Spacing();
            if (ImGui.Button("Close preview##DecisiveBattle"))
            {
                decisivePreviewActive = false;
            }

            return;
        }

        var runs = GetDecisiveBattleRuns();
        if (runs.Count == 0)
        {
            ImGui.TextDisabled("No Decisive Battle casts recorded yet.");
            return;
        }

        selectedDecisiveIndex = DrawRunPicker("DecisiveBattle", runs, DescribeDecisiveBattleRun, selectedDecisiveIndex);
        DrawDecisiveBattleRun(runs[selectedDecisiveIndex]);

        ImGui.Spacing();
        if (DrawClearButton("DecisiveBattle"))
        {
            plugin.DecisiveBattleHistory.Clear();
            selectedDecisiveIndex = 0;
        }
    }

    /// <summary>Lists the pull in progress first, followed by saved pulls, newest first.</summary>
    private List<DecisiveBattleRun> GetDecisiveBattleRuns()
    {
        var runs = new List<DecisiveBattleRun>();
        if (plugin.DecisiveBattleTracker.CurrentRun is { } liveRun)
        {
            runs.Add(liveRun);
        }

        runs.AddRange(plugin.DecisiveBattleHistory.Runs);
        return runs;
    }

    private string DescribeDecisiveBattleRun(DecisiveBattleRun run)
    {
        return ReferenceEquals(run, plugin.DecisiveBattleTracker.CurrentRun)
            ? LiveRunLabel
            : $"{run.StartedUtc.ToLocalTime():g} ({run.Casts.Count} casts)";
    }

    /// <summary>One table per cast.</summary>
    private void DrawDecisiveBattleRun(DecisiveBattleRun run)
    {
        ImGui.TextDisabled("Players are listed in the order their status was seen. Statuses that arrive in the same frame follow party list order.");
        for (var index = 0; index < run.Casts.Count; index++)
        {
            var cast = run.Casts[index];
            var time = run.StartedUtc.ToLocalTime().AddSeconds(cast.Seconds);
            var flags = index == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (ImGui.CollapsingHeader($"Cast {index + 1} ({time:T})##DecisiveCast{index}", flags))
            {
                DrawDecisiveBattleCast(cast, index);
            }
        }
    }

    private void DrawDecisiveBattleCast(DecisiveBattleCastRecord cast, int index)
    {
        DrawDecisiveArena(cast, index);

        if (!ImGui.BeginTable($"##DecisiveTable{index}", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Chaos");
        ImGui.TableSetupColumn("Exdeath");
        ImGui.TableSetupColumn("Status");
        ImGui.TableHeadersRow();

        foreach (var player in cast.Players)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(FormatDistance(player.X, player.Z, cast.ChaosX, cast.ChaosZ));

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(FormatDistance(player.X, player.Z, cast.ExdeathX, cast.ExdeathZ));

            ImGui.TableSetColumnIndex(3);
            DrawHeroStatus(player.Hero);
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// The arena of one cast: Chaos and Exdeath where they stood and a tether from each to the players who got its
    /// hero status (Epic Hero to Chaos, Fated Hero to Exdeath), colored by status.
    /// </summary>
    private void DrawDecisiveArena(DecisiveBattleCastRecord cast, int index)
    {
        var points = new List<(DecisiveBattlePlayerRecord Player, float Chaos, float Exdeath)>();
        foreach (var player in cast.Players)
        {
            if (player.X is float x && player.Z is float z
                && cast.ChaosX is float chaosX && cast.ChaosZ is float chaosZ
                && cast.ExdeathX is float exdeathX && cast.ExdeathZ is float exdeathZ)
            {
                var position = new Vector2(x, z);
                points.Add((player, Vector2.Distance(position, new Vector2(chaosX, chaosZ)), Vector2.Distance(position, new Vector2(exdeathX, exdeathZ))));
            }
        }

        if (points.Count == 0)
        {
            ImGui.TextDisabled("No positions were recorded for this cast.");
            return;
        }

        var drawList = BeginArena($"##DecisiveArena{index}", out var center, out var scale, out var arenaHovered, ForsakenArenaMaxSize);
        Vector2 ToScreen(float x, float z) => center + (new Vector2(x - ArenaCenter, z - ArenaCenter) * scale);

        var bosses = new List<(string Name, string Letter, Vector2 Position, Vector4 Color)>();
        if (cast.ChaosX is float chaosBossX && cast.ChaosZ is float chaosBossZ)
        {
            bosses.Add(("Chaos", "C", ToScreen(chaosBossX, chaosBossZ), DecisiveEpicColor));
        }

        if (cast.ExdeathX is float exdeathBossX && cast.ExdeathZ is float exdeathBossZ)
        {
            bosses.Add(("Exdeath", "E", ToScreen(exdeathBossX, exdeathBossZ), DecisiveFatedColor));
        }

        // Tethers go underneath everything else. Epic Hero belongs to Chaos, Fated Hero to Exdeath.
        foreach (var (player, _, _) in points)
        {
            var bossName = player.Hero == DecisiveBattleHero.Epic ? "Chaos" : "Exdeath";
            var color = player.Hero == DecisiveBattleHero.Epic ? DecisiveEpicColor : DecisiveFatedColor;
            if (bosses.FirstOrDefault(boss => boss.Name == bossName) is { Name: not null } boss)
            {
                drawList.AddLine(boss.Position, ToScreen(player.X!.Value, player.Z!.Value), ImGui.GetColorU32(color with { W = 0.85f }), 2.0f);
            }
        }

        foreach (var (_, letter, position, color) in bosses)
        {
            var half = new Vector2(11.0f);
            drawList.AddRectFilled(position - half, position + half, ImGui.GetColorU32(color with { W = 0.9f }));
            drawList.AddRect(position - half, position + half, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.9f)), 0.0f, ImDrawFlags.None, 1.5f);
            drawList.AddText(position - (ImGui.CalcTextSize(letter) * 0.5f), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)), letter);
        }

        var mouse = ImGui.GetMousePos();
        string? hoverText = null;
        foreach (var (player, chaos, exdeath) in points)
        {
            var color = player.Hero == DecisiveBattleHero.Epic ? DecisiveEpicColor : DecisiveFatedColor;
            var position = ToScreen(player.X!.Value, player.Z!.Value);
            var iconCenter = position + IconOffset;
            drawList.AddLine(position, iconCenter, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.35f)), 1.0f);
            drawList.AddCircleFilled(position, PositionDotRadius + 1.5f, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.85f)), 16);
            drawList.AddCircleFilled(position, PositionDotRadius, ImGui.GetColorU32(color), 16);

            var iconName = JobIcons.ImageName(player.JobId);
            if (iconName is null || !TryDrawIcon(drawList, iconName, iconCenter, DotRadius * JobIconScale))
            {
                drawList.AddCircleFilled(iconCenter, DotRadius, ImGui.GetColorU32(new Vector4(0.25f, 0.3f, 0.4f, 1.0f)), 24);
            }

            if (Vector2.Distance(mouse, position) <= PositionDotRadius + 4.0f || Vector2.Distance(mouse, iconCenter) <= DotRadius + 2.0f)
            {
                var name = player.IsLocalPlayer ? $"{player.Name} (you)" : player.Name;
                hoverText = $"{name}: {(player.Hero == DecisiveBattleHero.Epic ? "Epic Hero" : "Fated Hero")}\nChaos {chaos:0.0}, Exdeath {exdeath:0.0}";
            }
        }

        if (arenaHovered && hoverText is not null)
        {
            ImGui.SetTooltip(hoverText);
        }

        ImGui.TextColored(DecisiveEpicColor, "Chaos / Epic Hero (alpha)");
        ImGui.SameLine();
        ImGui.TextColored(DecisiveFatedColor, "Exdeath / Fated Hero (beta)");
        ImGui.TextDisabled("Dot: where the player stood when the status appeared. Line: the tether from the boss of that status.");
    }

    /// <summary>The flat distance in yalms between two points, or a dash if either position is unknown.</summary>
    private static string FormatDistance(float? x, float? z, float? targetX, float? targetZ)
    {
        if (x is not float px || z is not float pz || targetX is not float tx || targetZ is not float tz)
        {
            return "-";
        }

        return Vector2.Distance(new Vector2(px, pz), new Vector2(tx, tz)).ToString("0.0");
    }

    /// <summary>Alpha icon for Epic Hero, beta icon for Fated Hero, followed by the status name.</summary>
    private void DrawHeroStatus(DecisiveBattleHero hero)
    {
        var imageName = hero == DecisiveBattleHero.Epic ? "alpha.png" : "beta.png";
        var texture = Plugin.TextureProvider.GetFromFile(System.IO.Path.Combine(markerImageDirectory, imageName));
        if (texture.GetWrapOrDefault() is { } image && image.Height > 0)
        {
            // The marker images are not square, so the width follows the source aspect ratio.
            ImGui.Image(image.Handle, new Vector2(IconSize * image.Width / image.Height, IconSize));
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(hero == DecisiveBattleHero.Epic ? "Epic Hero" : "Fated Hero");
    }

    /// <summary>The first cast of a real pull (Chaos at the west, Exdeath at the east).</summary>
    private static DecisiveBattleRun CreateSampleDecisiveBattleRun()
    {
        var run = new DecisiveBattleRun { StartedUtc = DateTime.UtcNow };
        var cast = new DecisiveBattleCastRecord { ChaosX = 92.0f, ChaosZ = 100.0f, ExdeathX = 108.0f, ExdeathZ = 100.0f };
        (string Name, uint Job, DecisiveBattleHero Hero, float X, float Z)[] players =
        [
            ("Celia Nyrell", 23, DecisiveBattleHero.Fated, 110.2f, 100.0f),
            ("M'iriam Yukikazu", 39, DecisiveBattleHero.Epic, 85.8f, 99.3f),
            ("Luna Tancredi", 28, DecisiveBattleHero.Fated, 106.2f, 103.7f),
            ("Nimral Lebanel", 19, DecisiveBattleHero.Fated, 107.4f, 101.5f),
            ("Ranko Mizuki", 34, DecisiveBattleHero.Epic, 89.4f, 97.9f),
            ("Vin Enryu", 25, DecisiveBattleHero.Fated, 109.8f, 100.2f),
            ("Cian Avino", 24, DecisiveBattleHero.Epic, 93.0f, 102.1f),
            ("Selene Celeste", 32, DecisiveBattleHero.Epic, 92.7f, 99.9f),
        ];

        foreach (var (name, job, hero, x, z) in players)
        {
            cast.Players.Add(new DecisiveBattlePlayerRecord
            {
                EntityId = (uint)(cast.Players.Count + 1),
                Name = name,
                JobId = job,
                Hero = hero,
                X = x,
                Z = z,
                IsLocalPlayer = name == "Celia Nyrell",
            });
        }

        run.Casts.Add(cast);
        return run;
    }
}
