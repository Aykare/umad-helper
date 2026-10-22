using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly uint[] PreviewMarkers = [715, 716, 717];
    private const string TowerSequence = "AAABBBBA";
    private const float TowerIconHeight = 30.0f;
    private const float GroupIconSize = 64.0f;
    private const float MarkerIconSize = 108.0f;

    private readonly string markerImageDirectory;
    private readonly Plugin plugin;
    private bool previewActive;
    private uint previewInitialMarker = PreviewMarkers[0];
    private uint previewBuddyMarker = PreviewMarkers[1];
    private uint previewCurrentMarker = PreviewMarkers[0];
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

    public void Dispose() { }

    public override void Draw()
    {
        if (previewActive)
        {
            DrawPreview();
            return;
        }

        DrawBuddySelector();

        var tracker = plugin.P2ForsakenTracker;
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var buddy = Plugin.PartyList.FirstOrDefault(member =>
            member.ContentId == plugin.Configuration.SelectedBuddyContentId);

        if (!tracker.IsForsakenPhase)
        {
            ImGui.TextDisabled("Waiting for Forsaken.");
        }
        else if (localPlayer is null
                 || buddy is null
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

        ImGui.Separator();
        if (ImGui.Button("Preview"))
        {
            previewActive = true;
            ResetPreview();
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

        DrawTowerTimeline(rounds);
        DrawPhaseTimeline(activePhase, resolve);

        ImGui.Spacing();
        DrawPersonalIcons(isAlpha, markerId);
    }

    /// <summary>Shows all eight towers and highlights only the current tower.</summary>
    private void DrawTowerTimeline(int completedRounds)
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
                ImGui.GetColorU32(isActive
                    ? new Vector4(0.16f, 0.42f, 0.30f, 1.0f)
                    : new Vector4(0.12f, 0.13f, 0.16f, 1.0f)));

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
    private void DrawPersonalIcons(bool isAlpha, uint? markerId)
    {
        if (!ImGui.BeginTable("##ForsakenPersonalIcons", 2, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawCenteredIcon(isAlpha ? "alpha.png" : "beta.png", GroupIconSize);
        ImGui.TableSetColumnIndex(1);
        if (markerId is uint currentMarker && MarkerImageName(currentMarker) is string imageName)
        {
            DrawCenteredIcon(imageName, MarkerIconSize);
        }

        ImGui.EndTable();
    }

    /// <summary>Draws an image centered in the current table cell.</summary>
    private void DrawCenteredIcon(string imageName, float size)
    {
        var width = IconWidth(imageName, size);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0.0f, (ImGui.GetColumnWidth() - width) * 0.5f));
        DrawIcon(imageName, size);
    }

    /// <summary>Draws the selectable initial markers and the compact simulated status.</summary>
    private void DrawPreview()
    {
        ImGui.Text("Preview");
        if (previewCompletedRounds == 0)
        {
            DrawPreviewMarkerSelector("You", "PreviewYou", ref previewInitialMarker);
            ImGui.SameLine();
            DrawPreviewMarkerSelector("Buddy", "PreviewBuddy", ref previewBuddyMarker);
            previewCurrentMarker = previewInitialMarker;
        }

        DrawMechanicStatus(
            previewInitialMarker != previewBuddyMarker,
            previewCurrentMarker,
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

    /// <summary>Lets preview users choose a marker image without changing live state.</summary>
    private void DrawPreviewMarkerSelector(string label, string id, ref uint markerId)
    {
        if (!ImGui.BeginCombo(id, $"{label}: {P2ForsakenTracker.MarkerName(markerId)}"))
        {
            return;
        }

        foreach (var option in PreviewMarkers)
        {
            if (ImGui.Selectable($"{P2ForsakenTracker.MarkerName(option)}##{id}{option}", markerId == option))
            {
                markerId = option;
            }
        }

        ImGui.EndCombo();
    }

    /// <summary>Draws and persists the selected party buddy.</summary>
    private void DrawBuddySelector()
    {
        var buddyContentId = plugin.Configuration.SelectedBuddyContentId;
        var buddyName = Plugin.PartyList
            .FirstOrDefault(member => member.ContentId == buddyContentId)?.Name.TextValue ?? "Select buddy";

        if (!ImGui.BeginCombo("Buddy", buddyName))
        {
            return;
        }

        foreach (var member in Plugin.PartyList)
        {
            if (Plugin.ObjectTable.LocalPlayer is { } localPlayer && member.EntityId == localPlayer.EntityId)
            {
                continue;
            }

            var isSelected = member.ContentId == buddyContentId;
            if (ImGui.Selectable($"{member.Name.TextValue}##{member.ContentId}", isSelected))
            {
                plugin.Configuration.SelectedBuddyContentId = member.ContentId;
                plugin.Configuration.Save();
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    /// <summary>Advances one tower and updates the local simulated marker when this group takes it.</summary>
    private void AdvancePreview()
    {
        var isAlpha = previewInitialMarker != previewBuddyMarker;
        if ((TowerSequence[previewCompletedRounds] == 'A') == isAlpha)
        {
            previewCurrentMarker = NextPreviewMarker(previewCurrentMarker);
        }

        var previousRounds = previewCompletedRounds;
        previewCompletedRounds++;
        previewActivePhase = previewCompletedRounds % 2 == 0 ? 2 : 1;
        if (previewCompletedRounds % 2 == 1)
        {
            previewResolve = null;
        }
    }

    /// <summary>Draws an image centered in the window.</summary>
    private void DrawIcon(string imageName, float height)
    {
        var texture = Plugin.TextureProvider.GetFromFile(System.IO.Path.Combine(markerImageDirectory, imageName));
        var image = texture.GetWrapOrDefault();
        if (image is null)
        {
            ImGui.TextDisabled($"Could not load {imageName}.");
            return;
        }

        ImGui.Image(image.Handle, new Vector2(IconWidth(imageName, height), height));
    }

    /// <summary>Returns an icon's scaled width while preserving its source aspect ratio.</summary>
    private static float IconWidth(string imageName, float height)
    {
        return imageName is "alpha.png" or "beta.png"
            ? height * (48.0f / 64.0f)
            : height;
    }

    /// <summary>Gets the image asset for one observed Forsaken marker.</summary>
    private static string? MarkerImageName(uint markerId)
    {
        return markerId switch
        {
            715 => "stack.png",
            716 => "spread.png",
            717 => "cone.png",
            _ => null,
        };
    }

    /// <summary>Returns the next marker type used after a simulated tower hit.</summary>
    private static uint NextPreviewMarker(uint markerId)
    {
        var index = Array.IndexOf(PreviewMarkers, markerId);
        return PreviewMarkers[(index + 1) % PreviewMarkers.Length];
    }

    /// <summary>Resets all simulated preview state.</summary>
    private void ResetPreview()
    {
        previewCurrentMarker = previewInitialMarker;
        previewCompletedRounds = 0;
        previewResolve = null;
        previewActivePhase = 0;
    }
}
