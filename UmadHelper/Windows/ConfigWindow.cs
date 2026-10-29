using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    // The ### suffix keeps a constant ImGui window ID if the title ever changes.
    public ConfigWindow(Plugin plugin) : base("Umad Helper Settings###UmadHelperConfig")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 150),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("Raid positions");
        DrawRaidPositions();

        ImGui.Separator();
        ImGui.TextUnformatted("Previews");
        DrawPreviewButtons("Forsaken", Mechanic.Forsaken);
        DrawPreviewButtons("Limit Cut", Mechanic.LimitCut);
        DrawPreviewButtons("Kefka Says", Mechanic.KefkaSays);
        if (ImGui.Button("Decisive Battle history preview"))
        {
            plugin.StartHistoryPreview(Mechanic.DecisiveBattle);
        }

        ImGui.Separator();
        if (ImGui.Button("Open history"))
        {
            plugin.ToggleHistoryUi();
        }
    }

    /// <summary>Draws the live and history preview buttons for one mechanic.</summary>
    private void DrawPreviewButtons(string label, Mechanic mechanic)
    {
        if (ImGui.Button($"{label} preview"))
        {
            plugin.StartPreview(mechanic);
        }

        ImGui.SameLine();
        if (ImGui.Button($"{label} history preview"))
        {
            plugin.StartHistoryPreview(mechanic);
        }
    }

    /// <summary>
    /// Draws a raid position picker for every party member. Positions are saved by character name, so they stay
    /// set when someone leaves and rejoins the party.
    /// </summary>
    private void DrawRaidPositions()
    {
        var members = Plugin.PartyList.Select(member => member.Name.TextValue).ToList();
        if (members.Count == 0)
        {
            ImGui.TextDisabled("Join a party to set raid positions.");
            return;
        }

        foreach (var name in members)
        {
            var current = configuration.GetPosition(name);
            ImGui.SetNextItemWidth(70.0f);
            if (ImGui.BeginCombo($"{name}##Position{name}", current == RaidPosition.None ? "-" : current.ToString()))
            {
                if (ImGui.Selectable("-", current == RaidPosition.None))
                {
                    configuration.SetPosition(name, RaidPosition.None);
                    configuration.Save();
                }

                foreach (var position in RaidPositions.Assignable)
                {
                    if (ImGui.Selectable(position.ToString(), position == current))
                    {
                        configuration.SetPosition(name, position);
                        configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }
        }

        var duplicates = members
            .Select(name => configuration.GetPosition(name))
            .Where(position => position != RaidPosition.None)
            .GroupBy(position => position)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .ToList();
        if (duplicates.Count > 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.4f, 1.0f), $"Assigned more than once: {string.Join(", ", duplicates)}");
        }

        var buddyResolution = RaidPositions.ResolveBuddy(configuration);
        ImGui.TextUnformatted(buddyResolution.Buddy is { } buddy
            ? $"Your Forsaken buddy: {buddy.Name.TextValue} ({configuration.GetPosition(buddy.Name.TextValue)})"
            : buddyResolution.Message);
        ImGui.TextDisabled("MT/OT tanks, H1/H2 healers, M1/M2 melee, R1/R2 ranged. Buddies are fixed pairs: MT-H1, OT-H2, M1-R1, M2-R2. Supports (tanks, healers) take the left side, dps the right. Positions are saved by character name.");
    }
}
