using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Windows;

public partial class MainWindow
{
    private static readonly Vector4 KefkaRealColor = new(0.55f, 0.9f, 0.6f, 1.0f);
    private static readonly Vector4 KefkaFakeColor = new(1.0f, 0.62f, 0.35f, 1.0f);
    private static readonly Vector4 KefkaDieColor = new(1.0f, 0.45f, 0.45f, 1.0f);

    // Mana Charge 1, Mana Charge 2 and Mana Release have no debuff to read a time from; these are the observed
    // (first charge, release) or estimated (second charge) times in seconds after the first Grand Cross debuff.
    private static readonly float?[] KefkaFallbackSeconds = [null, null, null, 51.0f, null, null, null, 76.0f, null, null, null, 90.0f];

    private KefkaSaysRun? kefkaPreviewRun;
    private int kefkaPreviewResolved;

    /// <summary>Shows the live Kefka Says state of the local player.</summary>
    private void DrawKefkaSaysLive(KefkaSaysTracker tracker)
    {
        if (tracker.CurrentRun is { } run)
        {
            DrawKefkaSaysStatus(run, tracker.CurrentSeconds);
        }
    }

    /// <summary>Draws the group tells, the local player's own tasks and the resolve-order timeline.</summary>
    private static void DrawKefkaSaysStatus(KefkaSaysRun run, float now)
    {
        var local = run.Players.FirstOrDefault(player => player.IsLocalPlayer);
        if (local is not null)
        {
            DrawKefkaOwnDebuffs(local, now);
        }

        DrawKefkaTells(run);
        ImGui.Spacing();

        ImGui.TextUnformatted("You");
        if (local is null)
        {
            ImGui.TextDisabled("You are not in the recorded party.");
        }
        else
        {
            DrawKefkaPersonalCard(run, local, now);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Resolve order");
        DrawKefkaTimeline(run, local, now);
    }

    /// <summary>One row with the real/fake values everybody needs to know: both gaze rounds, Inferno, Tsunami and the two Mana Release elements.</summary>
    private static void DrawKefkaTells(KefkaSaysRun run)
    {
        (string Title, KefkaReality Reality, string Meaning)[] tells =
        [
            ("Gaze 1", KefkaSaysRules.StepReality(run, KefkaStep.ShortGazes), "The Gaze 1 value"),
            ("Gaze 2", KefkaSaysRules.StepReality(run, KefkaStep.LongGazes), "The Gaze 2 value"),
            ("Inferno", KefkaSaysRules.StepReality(run, KefkaStep.Inferno), "The Inferno (entropy) value"),
            ("Tsunami", KefkaSaysRules.TsunamiFinal(run), "The Dynamic Fluid value"),
            ("Ice", KefkaSaysRules.ReleaseFinal(run, KefkaSaysRules.IceElement), "Mana Release ice: Kefka's marker combined with the ice Mana Charge"),
            ("Thunder", KefkaSaysRules.ReleaseFinal(run, KefkaSaysRules.LightningElement), "Mana Release thunder: Kefka's marker combined with the thunder Mana Charge"),
        ];

        if (!ImGui.BeginTable("##KefkaTells", tells.Length, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextRow();
        for (var column = 0; column < tells.Length; column++)
        {
            ImGui.TableSetColumnIndex(column);
            var (title, reality, meaning) = tells[column];
            ImGui.TextDisabled(title);
            ImGui.TextColored(KefkaRealityColor(reality), KefkaSaysRules.RealityName(reality));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{meaning}: {KefkaSaysRules.RealityName(reality)}.");
            }
        }

        ImGui.EndTable();
    }

    /// <summary>Lists the steps the local player takes part in, with what to do and when.</summary>
    private static void DrawKefkaPersonalCard(KefkaSaysRun run, KefkaPlayerRecord local, float now)
    {
        if (!ImGui.BeginTable("##KefkaPersonal", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Do", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthFixed, 44.0f);

        var any = false;
        foreach (var step in Enum.GetValues<KefkaStep>())
        {
            if (step == KefkaStep.ManaRelease)
            {
                continue;
            }

            var label = KefkaSaysRules.LabelFor(run, local, step, out var involved);
            if (!involved)
            {
                continue;
            }

            any = true;
            DrawKefkaRow(run, step, label, now, KefkaLabelColor(run, local, step));
        }

        if (!any)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled("Waiting for your debuffs...");
        }

        ImGui.EndTable();
    }

    /// <summary>Lists all twelve steps, dimming finished ones and highlighting the next one.</summary>
    private static void DrawKefkaTimeline(KefkaSaysRun run, KefkaPlayerRecord? local, float now)
    {
        if (!ImGui.BeginTable("##KefkaTimeline", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Mine", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthFixed, 44.0f);

        foreach (var step in Enum.GetValues<KefkaStep>())
        {
            var label = local is null ? string.Empty : KefkaSaysRules.LabelFor(run, local, step, out _);
            var color = local is null ? Vector4.One : KefkaLabelColor(run, local, step);
            DrawKefkaRow(run, step, label, now, color);
        }

        ImGui.EndTable();
    }

    /// <summary>Draws one step row: name and its result, what to do, and a countdown.</summary>
    private static void DrawKefkaRow(KefkaSaysRun run, KefkaStep step, string label, float now, Vector4 labelColor)
    {
        var isDone = run.Steps.Any(record => record.Step == step);
        var isNext = !isDone && step == NextKefkaStep(run);
        var dim = isDone ? 0.45f : 1.0f;

        ImGui.TableNextRow();
        if (isNext)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.38f, 0.29f, 0.12f, 1.0f)));
        }

        ImGui.TableSetColumnIndex(0);
        ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, dim), KefkaSaysRules.StepName(step));
        var result = KefkaSaysRules.ResultFor(run, step);
        if (ImGui.IsItemHovered() && result.Length > 0)
        {
            ImGui.SetTooltip(result);
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.TextColored(labelColor with { W = dim }, label.Length > 0 ? label : "-");

        ImGui.TableSetColumnIndex(2);
        if (isDone)
        {
            ImGui.TextDisabled("done");
        }
        else if (KefkaStepSeconds(run, step) is float seconds)
        {
            var remaining = seconds - now;
            ImGui.TextUnformatted(remaining <= 0.0f ? "now" : $"{remaining:0}s");
        }
    }

    /// <summary>The first step that has not resolved yet.</summary>
    private static KefkaStep? NextKefkaStep(KefkaSaysRun run)
    {
        foreach (var step in Enum.GetValues<KefkaStep>())
        {
            if (run.Steps.All(record => record.Step != step))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>When a step is expected to resolve: its debuffs' expiry, else a known fixed time, else unknown.</summary>
    private static float? KefkaStepSeconds(KefkaSaysRun run, KefkaStep step)
    {
        var expiries = run.Players
            .SelectMany(player => player.Debuffs)
            .Where(debuff => debuff.Kind != KefkaDebuffKind.BeyondDeath
                && debuff.Kind != KefkaDebuffKind.AllaganField
                && KefkaSaysRules.StepOf(debuff.Kind, debuff.IsLong) == step)
            .Select(debuff => (float?)debuff.ExpiresAtSeconds)
            .ToList();

        return expiries.Count > 0 ? expiries.Min() : KefkaFallbackSeconds[(int)step];
    }

    /// <summary>Colours a task by whether it is real or fake; Flood of Naught by whether the holder has to die.</summary>
    private static Vector4 KefkaLabelColor(KefkaSaysRun run, KefkaPlayerRecord player, KefkaStep step)
    {
        if (step == KefkaStep.FloodOfNaught)
        {
            return KefkaSaysRules.FloodPlan(player).MustDie == true ? KefkaDieColor : KefkaRealColor;
        }

        var reality = step switch
        {
            KefkaStep.Tsunami => KefkaSaysRules.TsunamiFinal(run),
            KefkaStep.ManaCharge1 or KefkaStep.ManaCharge2 or KefkaStep.ManaRelease => KefkaReality.Unknown,
            _ => KefkaSaysRules.StepReality(run, step),
        };

        return KefkaRealityColor(reality);
    }

    private static Vector4 KefkaRealityColor(KefkaReality reality)
    {
        return reality switch
        {
            KefkaReality.Real => KefkaRealColor,
            KefkaReality.Fake => KefkaFakeColor,
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
        };
    }

    /// <summary>Steps through a scripted run, resolving one mechanic per click.</summary>
    private void DrawKefkaSaysPreview()
    {
        kefkaPreviewRun ??= KefkaSaysSample.Create();
        var stepCount = Enum.GetValues<KefkaStep>().Length;

        ImGui.Text("Kefka Says preview");
        DrawKefkaSaysStatus(
            KefkaSaysSample.Truncate(kefkaPreviewRun, kefkaPreviewResolved),
            KefkaSaysSample.SecondsBefore(kefkaPreviewResolved));

        ImGui.Spacing();
        if (kefkaPreviewResolved < stepCount)
        {
            if (ImGui.Button($"Resolve: {KefkaSaysRules.StepName((KefkaStep)kefkaPreviewResolved)}"))
            {
                kefkaPreviewResolved++;
            }
        }
        else
        {
            ImGui.TextDisabled("Mechanic complete");
        }

        if (ImGui.Button("Reset"))
        {
            ResetKefkaSaysPreview();
        }

        ImGui.SameLine();
        if (ImGui.Button("Close preview"))
        {
            previewActive = false;
        }
    }

    /// <summary>Resets the simulated Kefka Says state.</summary>
    private void ResetKefkaSaysPreview()
    {
        kefkaPreviewResolved = 0;
        kefkaPreviewRun = null;
    }
}
