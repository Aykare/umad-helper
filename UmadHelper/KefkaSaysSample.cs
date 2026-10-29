using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin;

/// <summary>
/// A scripted Kefka Says run used by the preview buttons. The debuff layout follows the handout seen in real pulls;
/// the positions are made up, because nothing is being recorded.
/// </summary>
internal static class KefkaSaysSample
{
    private const float ArenaCenter = 100.0f;

    // Roughly when each step resolves, in the same order as <see cref="KefkaStep"/>.
    private static readonly float[] StepSeconds = [40.0f, 51.0f, 51.1f, 51.2f, 60.0f, 67.0f, 76.0f, 76.1f, 76.2f, 84.0f, 90.0f, 90.5f];

    // Warrior, dragoon, monk, samurai, bard, black mage, white mage and scholar.
    private static readonly uint[] Jobs = [21, 22, 20, 34, 23, 25, 24, 28];

    /// <summary>Builds a full run in which the first player is the local player.</summary>
    public static KefkaSaysRun Create()
    {
        var run = new KefkaSaysRun { StartedUtc = DateTime.UtcNow };
        for (var index = 0; index < Jobs.Length; index++)
        {
            run.Players.Add(new KefkaPlayerRecord
            {
                EntityId = (uint)(index + 1),
                Name = $"Player {index + 1}",
                IsLocalPlayer = index == 0,
                JobId = Jobs[index],
            });
        }

        // Batch 1 is fake, batch 2 real.
        AddStackSpread(run, 0, KefkaDebuffKind.ForkedLightning, 5544, KefkaReality.Fake, 76.0f);
        AddStackSpread(run, 1, KefkaDebuffKind.CompressedWater, 5545, KefkaReality.Fake, 76.0f);
        AddStackSpread(run, 2, KefkaDebuffKind.ForkedLightning, 5544, KefkaReality.Fake, 76.0f);
        AddStackSpread(run, 3, KefkaDebuffKind.CompressedWater, 5545, KefkaReality.Fake, 76.0f);
        AddDebuff(run, 4, KefkaDebuffKind.AccelerationBomb, 5546, KefkaReality.Fake, 76.0f);
        AddDebuff(run, 5, KefkaDebuffKind.AccelerationBomb, 5546, KefkaReality.Fake, 76.0f);
        foreach (var index in new[] { 6, 7 })
        {
            AddDebuff(run, index, KefkaDebuffKind.CursedShriek, 5543, KefkaReality.Fake, 60.0f);
            AddDebuff(run, index, KefkaDebuffKind.AccelerationBomb, 5546, KefkaReality.Fake, 51.0f);
        }

        foreach (var index in new[] { 4, 5, 6, 7 })
        {
            AddStackSpread(run, index, index % 2 == 0 ? KefkaDebuffKind.CompressedWater : KefkaDebuffKind.ForkedLightning,
                index % 2 == 0 ? 5545u : 5544u, KefkaReality.Real, 51.0f);
        }

        foreach (var index in new[] { 0, 1 })
        {
            AddDebuff(run, index, KefkaDebuffKind.AccelerationBomb, 5546, KefkaReality.Real, 51.0f);
        }

        foreach (var index in new[] { 2, 3 })
        {
            AddDebuff(run, index, KefkaDebuffKind.CursedShriek, 5543, KefkaReality.Real, 84.0f);
            AddDebuff(run, index, KefkaDebuffKind.AccelerationBomb, 5546, KefkaReality.Real, 76.0f);
        }

        foreach (var player in run.Players)
        {
            AddDebuff(run, run.Players.IndexOf(player), KefkaDebuffKind.Entropy, 5547, KefkaReality.Fake, 67.0f);
            AddDebuff(run, run.Players.IndexOf(player), KefkaDebuffKind.DynamicFluid, 5548, KefkaReality.Real, 90.0f);
        }

        // Flood of Naught: wound colour and whether the holder must die.
        (uint StatusId, KefkaReality Reality, KefkaWound Wound)[] flood =
        [
            (1382, KefkaReality.Unknown, KefkaWound.White),
            (5464, KefkaReality.Unknown, KefkaWound.Black),
            (454, KefkaReality.Fake, KefkaWound.White),
            (454, KefkaReality.Fake, KefkaWound.Black),
            (5464, KefkaReality.Unknown, KefkaWound.Black),
            (1382, KefkaReality.Unknown, KefkaWound.White),
            (454, KefkaReality.Fake, KefkaWound.Black),
            (5464, KefkaReality.Unknown, KefkaWound.White),
        ];

        for (var index = 0; index < flood.Length; index++)
        {
            var kind = KefkaSaysRules.KindOf(flood[index].StatusId)!.Value;
            AddDebuff(run, index, kind, flood[index].StatusId, flood[index].Reality, 40.0f);
            run.Players[index].Wound = flood[index].Wound;
            run.Players[index].WoundStatusId = flood[index].Wound == KefkaWound.White ? 5541u : 5542u;
        }

        run.ManaBanks.Add(new KefkaManaBankRecord { Element = KefkaSaysRules.LightningElement, Reality = KefkaReality.Real, Seconds = 54.0f });
        run.ManaBanks.Add(new KefkaManaBankRecord { Element = KefkaSaysRules.IceElement, Reality = KefkaReality.Fake, Seconds = 72.0f });

        // Kefka's Mana markers: the two charges, then the pair that the Mana Release adds them to.
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.KefkaName, Value = 678, Seconds = 54.0f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.KefkaName, Value = 675, Seconds = 72.0f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.KefkaName, Value = 676, Seconds = 83.0f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.KefkaName, Value = 677, Seconds = 83.0f });

        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.NeoExdeathName, Value = 1119, Seconds = -0.3f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.ChaosName, Value = 1122, Seconds = 4.7f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.NeoExdeathName, Value = 1120, Seconds = 14.7f });
        run.Signals.Add(new KefkaSignalRecord { Source = KefkaSaysRules.ChaosName, Value = 1119, Seconds = 19.7f });

        foreach (var step in Enum.GetValues<KefkaStep>())
        {
            run.Steps.Add(CreateStep(run, step));
        }

        return run;
    }

    /// <summary>A copy of the run in which only the first <paramref name="resolved"/> steps have happened yet.</summary>
    public static KefkaSaysRun Truncate(KefkaSaysRun full, int resolved)
    {
        return new KefkaSaysRun
        {
            StartedUtc = full.StartedUtc,
            Players = full.Players,
            Signals = resolved > (int)KefkaStep.ManaRelease
                ? full.Signals
                : full.Signals.Where(signal => signal.Source != KefkaSaysRules.KefkaName || signal.Seconds < 80.0f).ToList(),
            ManaBanks = full.ManaBanks.Take(resolved > (int)KefkaStep.ManaCharge2 ? 2 : resolved > (int)KefkaStep.ManaCharge1 ? 1 : 0).ToList(),
            Steps = full.Steps.Where(step => (int)step.Step < resolved).ToList(),
        };
    }

    /// <summary>The preview clock: shortly before the next step resolves.</summary>
    public static float SecondsBefore(int resolved)
    {
        return resolved >= StepSeconds.Length ? StepSeconds[^1] + 5.0f : Math.Max(30.0f, StepSeconds[resolved] - 6.0f);
    }

    private static void AddStackSpread(
        KefkaSaysRun run, int player, KefkaDebuffKind kind, uint statusId, KefkaReality reality, float expiresAt)
    {
        AddDebuff(run, player, kind, statusId, reality, expiresAt);
    }

    private static void AddDebuff(
        KefkaSaysRun run, int player, KefkaDebuffKind kind, uint statusId, KefkaReality reality, float expiresAt)
    {
        run.Players[player].Debuffs.Add(new KefkaDebuffRecord
        {
            StatusId = statusId,
            Kind = kind,
            Reality = reality,
            IsLong = expiresAt >= KefkaSaysRules.LongTierThresholdSeconds
                && kind is not (KefkaDebuffKind.DynamicFluid or KefkaDebuffKind.Entropy),
            ExpiresAtSeconds = expiresAt,
        });
    }

    private static KefkaStepRecord CreateStep(KefkaSaysRun run, KefkaStep step)
    {
        var record = new KefkaStepRecord { Step = step, Seconds = StepSeconds[(int)step] };
        for (var index = 0; index < run.Players.Count; index++)
        {
            var player = run.Players[index];
            var label = KefkaSaysRules.LabelFor(run, player, step, out var involved);
            var radius = label switch
            {
                "Gaze: stand center" => 2.0f,
                "Gaze: stand out" => 18.5f,
                "Point-blank" => 3.0f,
                _ => 15.0f,
            };

            var angle = index * MathF.PI / 4.0f;
            var stepPlayer = new KefkaStepPlayer
            {
                EntityId = player.EntityId,
                X = ArenaCenter + (radius * MathF.Sin(angle)),
                Z = ArenaCenter - (radius * MathF.Cos(angle)),
                Label = label,
                Involved = involved,
            };

            if (step == KefkaStep.FloodOfNaught)
            {
                var plan = KefkaSaysRules.FloodPlan(player).Destination;
                // One player ends up in the wrong half, so the preview also shows a mistake.
                stepPlayer.Actual = index == 6
                    ? (plan == KefkaWound.White ? nameof(KefkaWound.Black) : nameof(KefkaWound.White))
                    : plan.ToString();

                // The Antilights come from the east edge here; white covers the south half and black the north half.
                var south = stepPlayer.Actual == nameof(KefkaWound.White) ? 1.0f : -1.0f;
                stepPlayer.X = ArenaCenter - 12.0f + (index * 3.5f);
                stepPlayer.Z = ArenaCenter + (south * (5.0f + ((index % 3) * 3.0f)));
            }

            record.Players.Add(stepPlayer);
        }

        if (step == KefkaStep.FloodOfNaught)
        {
            record.Areas.Add(new KefkaAreaRecord { Colour = nameof(KefkaWound.White), CasterX = ArenaCenter + 20.0f, CasterZ = ArenaCenter + 9.5f });
            record.Areas.Add(new KefkaAreaRecord { Colour = nameof(KefkaWound.Black), CasterX = ArenaCenter + 20.0f, CasterZ = ArenaCenter - 9.5f });
        }
        record.Result = KefkaSaysRules.ResultFor(run, step);
        return record;
    }
}
