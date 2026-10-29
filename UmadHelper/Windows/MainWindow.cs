using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public partial class MainWindow : Window, IDisposable
{
    private static readonly uint[] AlphaPreviewMarkers = [715, 717, 715, 716, 716, 716, 716, 716, 716];
    private static readonly uint[] BetaPreviewMarkers = [716, 716, 716, 716, 717, 716, 715, 715, 715];
    private const string TowerSequence = "AAABBBBA";
    private const float TowerIconHeight = 30.0f;
    private const float GroupIconSize = 64.0f;
    private const float MarkerIconSize = 108.0f;

    private readonly string markerImageDirectory;
    private readonly Plugin plugin;
    private bool previewActive;
    private Mechanic previewMechanic;
    private Vector2 headerButtonWidths = new(24.0f, 24.0f);
    private bool previewIsAlpha = true;
    private int previewCompletedRounds;
    private string? previewResolve;
    private int previewActivePhase;

    public MainWindow(Plugin plugin, string markerImageDirectory)
        : base("Umad Helper##Main")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.markerImageDirectory = markerImageDirectory;
        this.plugin = plugin;
    }

    public void Dispose()
    {
        largeFont.Dispose();
    }

    public override void Draw()
    {
        DrawHeaderActions();

        if (previewActive)
        {
            DrawPreview();
            return;
        }

        var tracker = plugin.P2ForsakenTracker;
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var buddyResolution = RaidPositions.ResolveBuddy(plugin.Configuration);
        var buddy = buddyResolution.Buddy;

        if (plugin.LimitCutTracker.IsActive)
        {
            DrawLimitCutLive(plugin.LimitCutTracker);
        }
        else if (plugin.KefkaSaysTracker.IsActive)
        {
            DrawKefkaSaysLive(plugin.KefkaSaysTracker);
        }
        else if (!tracker.IsForsakenPhase)
        {
            ImGui.TextDisabled("No active mechanic.");
        }
        else if (buddy is null)
        {
            ImGui.TextDisabled(buddyResolution.Message);
        }
        else if (localPlayer is null
                 || !tracker.TryGetInitialMarker(localPlayer.EntityId, out var localMarker)
                 || !tracker.TryGetInitialMarker(buddy.EntityId, out var buddyMarker))
        {
            ImGui.TextDisabled("Waiting for buddy markers.");
        }
        else
        {
            DrawMechanicStatus(
                localMarker != buddyMarker,
                tracker.LastLocalMarkerId,
                tracker.CompletedTowerRounds,
                tracker.LastResolve,
                tracker.ActivePhaseIndex);
        }
    }

    /// <summary>Switches the window to the simulated preview of a mechanic.</summary>
    public void StartPreview(Mechanic mechanic)
    {
        previewActive = true;
        previewMechanic = mechanic;
        ResetPreview();
    }

    /// <summary>Right-aligns the History and Settings icon buttons in the window's top row.</summary>
    private void DrawHeaderActions()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // Icon button widths depend on the icon font, so reuse the widths measured on the previous frame.
        var totalWidth = headerButtonWidths.X + headerButtonWidths.Y + spacing;
        var rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), rightEdge - totalWidth));

        if (ImGuiComponents.IconButton("##OpenUmadHistory", FontAwesomeIcon.History))
        {
            plugin.ToggleHistoryUi();
        }

        headerButtonWidths.X = ImGui.GetItemRectSize().X;
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open history.");
        }

        ImGui.SameLine(0.0f, spacing);
        if (ImGuiComponents.IconButton("##OpenUmadSettings", FontAwesomeIcon.Cog))
        {
            plugin.ToggleConfigUi();
        }

        headerButtonWidths.Y = ImGui.GetItemRectSize().X;
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open Umad Helper settings.");
        }
    }

    /// <summary>Draws the tower timeline, active mechanic phase, and local icons.</summary>
    private void DrawMechanicStatus(
        bool isAlpha,
        uint? markerId,
        int? completedRounds,
        string? resolve,
        int activePhase)
    {
        if (completedRounds is not int rounds)
        {
            ImGui.TextDisabled("Reading tower set...");
            return;
        }

        var takesTower = TakesTower(isAlpha, rounds);
        DrawTowerTimeline(rounds, takesTower);
        DrawPhaseTimeline(activePhase, resolve);

        ImGui.Spacing();
        DrawPersonalIcons(isAlpha, markerId, takesTower);
    }

    /// <summary>Shows all eight towers and highlights only the current tower.</summary>
    private void DrawTowerTimeline(int completedRounds, bool takesTower)
    {
        if (!ImGui.BeginTable("##ForsakenTowerTimeline", TowerSequence.Length,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();
        for (var tower = 0; tower < TowerSequence.Length; tower++)
        {
            ImGui.TableSetColumnIndex(tower);
            var isActive = tower == completedRounds;
            ImGui.TableSetBgColor(
                ImGuiTableBgTarget.CellBg,
                ImGui.GetColorU32(!isActive
                    ? new Vector4(0.12f, 0.13f, 0.16f, 1.0f)
                    : takesTower
                        ? new Vector4(0.16f, 0.42f, 0.30f, 1.0f)
                        : new Vector4(0.45f, 0.17f, 0.17f, 1.0f)));

            var icon = TowerSequence[tower] == 'A' ? "alpha.png" : "beta.png";
            DrawCenteredIcon(icon, TowerIconHeight);
        }

        ImGui.EndTable();
    }

    /// <summary>Highlights Odd, Even, or the Past/Future resolve phase.</summary>
    private void DrawPhaseTimeline(int activePhase, string? resolve)
    {
        if (!ImGui.BeginTable("##ForsakenPhaseTimeline", 3,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        var labels = new[] { "ODD", "EVEN", "PAST / FUTURE" };
        ImGui.TableNextRow();
        for (var phase = 0; phase < labels.Length; phase++)
        {
            ImGui.TableSetColumnIndex(phase);
            var isActive = phase == activePhase;
            ImGui.TableSetBgColor(
                ImGuiTableBgTarget.CellBg,
                ImGui.GetColorU32(isActive
                    ? new Vector4(0.38f, 0.29f, 0.12f, 1.0f)
                    : new Vector4(0.12f, 0.13f, 0.16f, 1.0f)));

            var displayText = phase == 2 && resolve is not null
                ? (resolve.StartsWith("Past", StringComparison.Ordinal) ? "PAST" : "FUTURE")
                : labels[phase];
            var textSize = ImGui.CalcTextSize(displayText);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0.0f, (ImGui.GetColumnWidth() - textSize.X) * 0.5f));
            ImGui.TextColored(
                isActive ? new Vector4(1.0f, 0.88f, 0.55f, 1.0f) : new Vector4(0.72f, 0.74f, 0.78f, 1.0f),
                displayText);
        }

        ImGui.EndTable();
    }

    /// <summary>Places the local Alpha/Beta icon and current marker image side by side.</summary>
    private void DrawPersonalIcons(bool isAlpha, uint? markerId, bool takesTower)
    {
        if (!ImGui.BeginTable("##ForsakenPersonalIcons", 2, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawCenteredIcon(isAlpha ? "alpha.png" : "beta.png", GroupIconSize);
        ImGui.TableSetColumnIndex(1);
        if (markerId is uint currentMarker && MarkerImageName(currentMarker, takesTower) is string imageName)
        {
            // Stack has no gray asset; it can linger after a group's last tower, so dim it instead.
            var tint = !takesTower && currentMarker == 715
                ? new Vector4(0.4f, 0.4f, 0.4f, 0.8f)
                : Vector4.One;
            DrawCenteredIcon(imageName, MarkerIconSize, tint);
        }

        ImGui.EndTable();
    }

    /// <summary>Draws an image centered in the current table cell.</summary>
    private void DrawCenteredIcon(string imageName, float size, Vector4? tint = null)
    {
        var width = IconWidth(imageName, size);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0.0f, (ImGui.GetColumnWidth() - width) * 0.5f));
        DrawIcon(imageName, size, tint ?? Vector4.One);
    }

    /// <summary>Draws the preview of whichever mechanic was chosen.</summary>
    private void DrawPreview()
    {
        if (previewMechanic == Mechanic.LimitCut)
        {
            DrawLimitCutPreview();
            return;
        }

        if (previewMechanic == Mechanic.KefkaSays)
        {
            DrawKefkaSaysPreview();
            return;
        }

        DrawForsakenPreview();
    }

    /// <summary>Draws the scripted example run and its simulated controls.</summary>
    private void DrawForsakenPreview()
    {
        ImGui.Text("Preview");
        if (previewCompletedRounds == 0)
        {
            ImGui.SameLine();
            if (ImGui.Button(previewIsAlpha ? "Example: Alpha" : "Example: Beta"))
            {
                previewIsAlpha = !previewIsAlpha;
            }
        }

        var scriptedMarkers = previewIsAlpha ? AlphaPreviewMarkers : BetaPreviewMarkers;
        DrawMechanicStatus(
            previewIsAlpha,
            scriptedMarkers[previewCompletedRounds],
            previewCompletedRounds,
            previewResolve,
            previewActivePhase);

        if (previewCompletedRounds >= TowerSequence.Length)
        {
            ImGui.Text("Mechanic complete");
        }
        else if (previewActivePhase == 1
                 && previewCompletedRounds % 2 == 1
                 && previewResolve is null)
        {
            if (ImGui.Button("Start Past cast"))
            {
                previewResolve = "Past's End";
                previewActivePhase = 1;
            }
        }
        else if (previewActivePhase == 2)
        {
            if (ImGui.Button("Clones lock in"))
            {
                previewActivePhase = 0;
            }
        }
        else if (ImGui.Button("Next tower hit"))
        {
            AdvancePreview();
        }

        if (ImGui.Button("Reset"))
        {
            ResetPreview();
        }

        ImGui.SameLine();
        if (ImGui.Button("Close preview"))
        {
            previewActive = false;
        }
    }

    /// <summary>Advances one simulated tower hit.</summary>
    private void AdvancePreview()
    {
        previewCompletedRounds++;
        previewActivePhase = previewCompletedRounds % 2 == 0 ? 2 : 1;
        if (previewCompletedRounds % 2 == 1)
        {
            previewResolve = null;
        }
    }

    /// <summary>Draws an image centered in the window.</summary>
    private void DrawIcon(string imageName, float height, Vector4 tint)
    {
        var texture = Plugin.TextureProvider.GetFromFile(System.IO.Path.Combine(markerImageDirectory, imageName));
        var image = texture.GetWrapOrDefault();
        if (image is null)
        {
            ImGui.TextDisabled($"Could not load {imageName}.");
            return;
        }

        ImGui.Image(image.Handle, new Vector2(IconWidth(imageName, height), height), Vector2.Zero, Vector2.One, tint);
    }

    /// <summary>Returns an icon's scaled width while preserving its source aspect ratio.</summary>
    private static float IconWidth(string imageName, float height)
    {
        return imageName is "alpha.png" or "beta.png"
            ? height * (48.0f / 64.0f)
            : height;
    }

    /// <summary>Gets the image asset for one observed Forsaken marker.</summary>
    private static string? MarkerImageName(uint markerId, bool takesTower)
    {
        return markerId switch
        {
            715 => "stack.png",
            716 => takesTower ? "spread.png" : "spread_gray.png",
            717 => takesTower ? "cone.png" : "cone_gray.png",
            _ => null,
        };
    }

    /// <summary>Returns whether this group stands in the tower that is about to hit.</summary>
    private static bool TakesTower(bool isAlpha, int completedRounds)
    {
        return completedRounds < TowerSequence.Length
            && (TowerSequence[completedRounds] == 'A') == isAlpha;
    }

    /// <summary>Resets all simulated preview state.</summary>
    private void ResetPreview()
    {
        previewCompletedRounds = 0;
        previewResolve = null;
        previewActivePhase = 0;
        ResetLimitCutPreview();
        ResetKefkaSaysPreview();
    }
}
