using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace SamplePlugin.Windows;

public partial class MainWindow
{
    private const float LimitCutOriginIconSize = 40.0f;
    private const float LimitCutPositionIconSize = 84.0f;

    // Scripted examples taken from real pulls: origin ring index, rotation and limit cut number.
    private static readonly (int Origin, LimitCutRotation Rotation, int Number)[] LimitCutPreviewExamples =
    [
        (5, LimitCutRotation.CounterClockwise, 1),
        (3, LimitCutRotation.Clockwise, 6),
    ];

    private readonly IFontHandle largeFont = Plugin.PluginInterface.UiBuilder.FontAtlas
        .NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis36));

    private int limitCutPreviewExample;
    private int limitCutPreviewStep;

    /// <summary>Shows the live Limit Cut state of the local player.</summary>
    private void DrawLimitCutLive(LimitCutTracker tracker)
    {
        var run = tracker.CurrentRun;
        DrawLimitCutStatus(run?.OriginIndex, run?.Rotation ?? LimitCutRotation.Unknown, tracker.LocalNumber);
    }

    /// <summary>Draws the origin, rotation and number row, then the final position once everything is known.</summary>
    private void DrawLimitCutStatus(int? originIndex, LimitCutRotation rotation, int? number)
    {
        if (originIndex is not int origin)
        {
            ImGui.TextDisabled("Waiting for the clones...");
            return;
        }

        DrawLimitCutInfoRow(origin, rotation, number);
        ImGui.Spacing();

        if (number is int limitCutNumber
            && LimitCutStrategy.TryGetFinalGap(rotation, origin, limitCutNumber, out var gapIndex))
        {
            DrawFinalPosition(gapIndex);
            return;
        }

        ImGui.TextDisabled(rotation == LimitCutRotation.Unknown
            ? "Waiting for the rotation..."
            : "Waiting for your number...");
    }

    /// <summary>One row with the origin waymark, the rotation and the local limit cut number.</summary>
    private void DrawLimitCutInfoRow(int origin, LimitCutRotation rotation, int? number)
    {
        if (!ImGui.BeginTable("##LimitCutInfo", 3, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawCenteredIcon(LimitCutStrategy.ImageName(LimitCutStrategy.RingLabels[origin]), LimitCutOriginIconSize);

        ImGui.TableSetColumnIndex(1);
        DrawCenteredText(LimitCutStrategy.RotationName(rotation), new Vector4(1.0f, 0.88f, 0.55f, 1.0f));

        ImGui.TableSetColumnIndex(2);
        DrawCenteredText(number is int value ? $"#{value}" : "#?", new Vector4(0.72f, 0.9f, 0.78f, 1.0f));

        ImGui.EndTable();
    }

    /// <summary>Shows the two waymarks that border the gap where the player ends up.</summary>
    private void DrawFinalPosition(int gapIndex)
    {
        var (first, second) = LimitCutStrategy.GapLabels(gapIndex);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = (LimitCutPositionIconSize * 2.0f) + spacing;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0.0f, (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f));

        DrawIcon(LimitCutStrategy.ImageName(first), LimitCutPositionIconSize, Vector4.One);
        ImGui.SameLine();
        DrawIcon(LimitCutStrategy.ImageName(second), LimitCutPositionIconSize, Vector4.One);
    }

    /// <summary>Draws large text centered in the current table cell.</summary>
    private void DrawCenteredText(string text, Vector4 color)
    {
        // A real 36px font stays sharp; scaling the small default font only stretches its pixels.
        using (largeFont.Push())
        {
            var width = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0.0f, (ImGui.GetColumnWidth() - width) * 0.5f));
            ImGui.TextColored(color, text);
        }
    }

    /// <summary>Steps through a scripted Limit Cut: first clone, second clone, number, then the end.</summary>
    private void DrawLimitCutPreview()
    {
        ImGui.Text("Limit Cut preview");
        if (limitCutPreviewStep == 0)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Example {limitCutPreviewExample + 1}"))
            {
                limitCutPreviewExample = (limitCutPreviewExample + 1) % LimitCutPreviewExamples.Length;
            }
        }

        var example = LimitCutPreviewExamples[limitCutPreviewExample];
        if (limitCutPreviewStep >= 4)
        {
            ImGui.TextDisabled("Mechanic complete");
        }
        else
        {
            DrawLimitCutStatus(
                limitCutPreviewStep >= 1 ? example.Origin : null,
                limitCutPreviewStep >= 2 ? example.Rotation : LimitCutRotation.Unknown,
                limitCutPreviewStep >= 3 ? example.Number : null);
        }

        ImGui.Spacing();
        if (limitCutPreviewStep < 4)
        {
            var label = limitCutPreviewStep switch
            {
                0 => "First clone",
                1 => "Second clone",
                2 => "Number assigned",
                _ => "Attacks resolve",
            };

            if (ImGui.Button(label))
            {
                limitCutPreviewStep++;
            }
        }

        if (ImGui.Button("Reset"))
        {
            ResetLimitCutPreview();
        }

        ImGui.SameLine();
        if (ImGui.Button("Close preview"))
        {
            previewActive = false;
        }
    }

    /// <summary>Resets the simulated Limit Cut state.</summary>
    private void ResetLimitCutPreview()
    {
        limitCutPreviewStep = 0;
    }
}
