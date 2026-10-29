using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

/// <summary>Shows current and past runs of each mechanic, one tab per mechanic.</summary>
public partial class HistoryWindow : Window, IDisposable
{
    private const float IconSize = 26.0f;
    private const string LiveRunLabel = "Live";

    private readonly Plugin plugin;
    private readonly string markerImageDirectory;
    private Mechanic? tabToSelect;

    public HistoryWindow(Plugin plugin, string markerImageDirectory)
        : base("Umad Helper History###UmadHelperHistory")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        this.markerImageDirectory = markerImageDirectory;
    }

    public void Dispose() { }

    /// <summary>Leaves preview mode so reopening the window shows real history.</summary>
    public override void OnClose()
    {
        forsakenPreviewActive = false;
        limitCutPreviewActive = false;
        kefkaPreviewActive = false;
        decisivePreviewActive = false;
    }

    /// <summary>Switches a mechanic's tab to a sample run so it can be tested without recorded data.</summary>
    public void StartPreview(Mechanic mechanic)
    {
        tabToSelect = mechanic;
        switch (mechanic)
        {
            case Mechanic.Forsaken:
                forsakenPreviewActive = true;
                break;
            case Mechanic.KefkaSays:
                kefkaPreviewActive = true;
                break;
            case Mechanic.DecisiveBattle:
                decisivePreviewActive = true;
                break;
            default:
                limitCutPreviewActive = true;
                break;
        }
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##HistoryTabs"))
        {
            return;
        }

        if (BeginMechanicTab("Forsaken", Mechanic.Forsaken))
        {
            DrawForsakenTab();
            ImGui.EndTabItem();
        }

        if (BeginMechanicTab("Limit Cut", Mechanic.LimitCut))
        {
            DrawLimitCutTab();
            ImGui.EndTabItem();
        }

        if (BeginMechanicTab("Kefka Says", Mechanic.KefkaSays))
        {
            DrawKefkaSaysTab();
            ImGui.EndTabItem();
        }

        if (BeginMechanicTab("Decisive Battle", Mechanic.DecisiveBattle))
        {
            DrawDecisiveBattleTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    /// <summary>Starts a tab, forcing it open when a preview for that mechanic was just requested.</summary>
    private bool BeginMechanicTab(string label, Mechanic mechanic)
    {
        var flags = ImGuiTabItemFlags.None;
        if (tabToSelect == mechanic)
        {
            flags = ImGuiTabItemFlags.SetSelected;
            tabToSelect = null;
        }

        return ImGui.BeginTabItem(label, flags);
    }

    /// <summary>Draws the run dropdown and returns the (possibly changed) selected index.</summary>
    private static int DrawRunPicker<TRun>(
        string id,
        IReadOnlyList<TRun> runs,
        Func<TRun, string> describe,
        int selectedIndex)
    {
        selectedIndex = Math.Clamp(selectedIndex, 0, runs.Count - 1);
        if (ImGui.BeginCombo($"Run##{id}", describe(runs[selectedIndex])))
        {
            for (var index = 0; index < runs.Count; index++)
            {
                if (ImGui.Selectable($"{describe(runs[index])}##{id}{index}", index == selectedIndex))
                {
                    selectedIndex = index;
                }
            }

            ImGui.EndCombo();
        }

        return selectedIndex;
    }

    /// <summary>Clears one mechanic's saved runs, guarded by holding Ctrl.</summary>
    private static bool DrawClearButton(string id)
    {
        var cleared = ImGui.Button($"Clear history##{id}") && ImGui.GetIO().KeyCtrl;
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hold Ctrl and click to delete all saved runs.");
        }

        return cleared;
    }

    /// <summary>Draws a waymark or marker image at the history icon size.</summary>
    private void DrawImage(string imageName)
    {
        var texture = Plugin.TextureProvider.GetFromFile(System.IO.Path.Combine(markerImageDirectory, imageName));
        if (texture.GetWrapOrDefault() is { } image)
        {
            ImGui.Image(image.Handle, new Vector2(IconSize, IconSize));
        }
    }
}
