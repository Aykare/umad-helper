using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;

namespace SamplePlugin.Windows;

public partial class MainWindow
{
    private static readonly Vector4 KefkaBorderReal = new(0.2f, 0.85f, 0.3f, 1.0f);
    private static readonly Vector4 KefkaBorderFake = new(0.9f, 0.2f, 0.2f, 1.0f);
    private static readonly Vector4 KefkaBorderUnknown = new(0.6f, 0.6f, 0.6f, 1.0f);

    // Status icons are 24x32 in the game files.
    private static readonly Vector2 KefkaStripIconSize = new(30.0f, 40.0f);
    private static readonly Vector2 KefkaTableIconSize = new(15.0f, 20.0f);

    private static readonly Dictionary<uint, (uint IconId, string Name)> KefkaStatusCache = [];

    /// <summary>Draws the local player's debuffs as a bordered icon strip (green = real, red = fake) and as a table below it.</summary>
    private static void DrawKefkaOwnDebuffs(KefkaPlayerRecord local, float now)
    {
        var debuffs = local.Debuffs.OrderBy(debuff => debuff.ExpiresAtSeconds).ToList();
        if (debuffs.Count == 0)
        {
            return;
        }

        ImGui.TextUnformatted("Your debuffs");
        DrawKefkaDebuffStrip(debuffs, now);
        ImGui.Spacing();
        DrawKefkaDebuffTable(debuffs, now);
        ImGui.Spacing();
    }

    /// <summary>One bordered status icon per debuff with its remaining time underneath.</summary>
    private static void DrawKefkaDebuffStrip(List<KefkaDebuffRecord> debuffs, float now)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        const float border = 2.0f;

        for (var index = 0; index < debuffs.Count; index++)
        {
            var debuff = debuffs[index];
            var remaining = debuff.ExpiresAtSeconds - now;
            var timer = KefkaTimerText(remaining);
            var width = Math.Max(KefkaStripIconSize.X + (border * 2.0f), ImGui.CalcTextSize(timer).X);
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(width, KefkaStripIconSize.Y + (border * 2.0f) + lineHeight));
            var hovered = ImGui.IsItemHovered();

            var iconMin = origin + new Vector2((width - KefkaStripIconSize.X) * 0.5f, border);
            var iconMax = iconMin + KefkaStripIconSize;
            var dim = remaining <= 0.0f ? 0.4f : 1.0f;
            TryDrawKefkaStatusIcon(drawList, debuff.StatusId, iconMin, iconMax, dim);
            drawList.AddRect(
                iconMin - new Vector2(border * 0.5f),
                iconMax + new Vector2(border * 0.5f),
                ImGui.GetColorU32(KefkaBorderFor(debuff.Reality) with { W = dim }),
                0.0f,
                ImDrawFlags.None,
                border);

            var textPosition = new Vector2(origin.X + ((width - ImGui.CalcTextSize(timer).X) * 0.5f), iconMax.Y + border);
            drawList.AddText(textPosition, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, dim)), timer);

            if (hovered)
            {
                ImGui.SetTooltip($"{KefkaDebuffName(debuff)}: {KefkaSaysRules.RealityName(debuff.Reality)}");
            }

            if (index < debuffs.Count - 1)
            {
                ImGui.SameLine(0.0f, 6.0f);
            }
        }
    }

    /// <summary>Table of debuff icon | real/fake mark | timer.</summary>
    private static void DrawKefkaDebuffTable(List<KefkaDebuffRecord> debuffs, float now)
    {
        if (!ImGui.BeginTable("##KefkaDebuffs", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Debuff", ImGuiTableColumnFlags.WidthFixed, 40.0f);
        ImGui.TableSetupColumn("Real / Fake", ImGuiTableColumnFlags.WidthFixed, 40.0f);
        ImGui.TableSetupColumn("Timer", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var drawList = ImGui.GetWindowDrawList();
        foreach (var debuff in debuffs)
        {
            var remaining = debuff.ExpiresAtSeconds - now;
            var dim = remaining <= 0.0f ? 0.4f : 1.0f;
            ImGui.TableNextRow(ImGuiTableRowFlags.None, KefkaTableIconSize.Y + 4.0f);

            ImGui.TableSetColumnIndex(0);
            var iconMin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(KefkaTableIconSize);
            TryDrawKefkaStatusIcon(drawList, debuff.StatusId, iconMin, iconMin + KefkaTableIconSize, dim);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(KefkaDebuffName(debuff));
            }

            ImGui.TableSetColumnIndex(1);
            var markMin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(KefkaTableIconSize.Y));
            DrawKefkaRealityMark(drawList, markMin, KefkaTableIconSize.Y, debuff.Reality, dim);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(KefkaSaysRules.RealityName(debuff.Reality));
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, dim), KefkaTimerText(remaining));
        }

        ImGui.EndTable();
    }

    /// <summary>A green check for real, a red cross for fake and a gray dash when the value is not known.</summary>
    private static void DrawKefkaRealityMark(ImDrawListPtr drawList, Vector2 topLeft, float size, KefkaReality reality, float dim)
    {
        var color = ImGui.GetColorU32(KefkaBorderFor(reality) with { W = dim });
        var inset = size * 0.15f;
        var min = topLeft + new Vector2(inset);
        var max = topLeft + new Vector2(size - inset);
        var thickness = 2.5f;

        switch (reality)
        {
            case KefkaReality.Real:
                var bottom = new Vector2(topLeft.X + (size * 0.4f), max.Y);
                drawList.AddLine(new Vector2(min.X, topLeft.Y + (size * 0.55f)), bottom, color, thickness);
                drawList.AddLine(bottom, new Vector2(max.X, min.Y), color, thickness);
                break;
            case KefkaReality.Fake:
                drawList.AddLine(min, max, color, thickness);
                drawList.AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, min.Y), color, thickness);
                break;
            default:
                var middle = topLeft.Y + (size * 0.5f);
                drawList.AddLine(new Vector2(min.X, middle), new Vector2(max.X, middle), color, thickness);
                break;
        }
    }

    private static Vector4 KefkaBorderFor(KefkaReality reality)
    {
        return reality switch
        {
            KefkaReality.Real => KefkaBorderReal,
            KefkaReality.Fake => KefkaBorderFake,
            _ => KefkaBorderUnknown,
        };
    }

    private static string KefkaTimerText(float remaining)
    {
        return remaining <= 0.0f ? "now" : $"{remaining:0.0}s";
    }

    /// <summary>The game's name for the debuff's status, or the debuff family when the sheet is not available.</summary>
    private static string KefkaDebuffName(KefkaDebuffRecord debuff)
    {
        var name = KefkaStatusInfo(debuff.StatusId).Name;
        return name.Length > 0 ? name : debuff.Kind.ToString();
    }

    /// <summary>Looks up (and caches) a status's icon ID and name from the game data.</summary>
    private static (uint IconId, string Name) KefkaStatusInfo(uint statusId)
    {
        if (KefkaStatusCache.TryGetValue(statusId, out var cached))
        {
            return cached;
        }

        var info = (IconId: 0u, Name: string.Empty);
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId) is { } row)
            {
                info = (row.Icon, row.Name.ToString());
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not load status {StatusId}.", statusId);
        }

        KefkaStatusCache[statusId] = info;
        return info;
    }

    /// <summary>Draws a status's game icon; false when it is missing or not loaded yet.</summary>
    private static bool TryDrawKefkaStatusIcon(ImDrawListPtr drawList, uint statusId, Vector2 topLeft, Vector2 bottomRight, float alpha)
    {
        var iconId = KefkaStatusInfo(statusId).IconId;
        if (iconId == 0u || Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault() is not { } image)
        {
            return false;
        }

        drawList.AddImage(image.Handle, topLeft, bottomRight, Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, alpha)));
        return true;
    }
}
