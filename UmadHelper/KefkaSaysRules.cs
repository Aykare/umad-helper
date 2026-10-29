using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin;

/// <summary>
/// Status IDs, tell values and resolve rules for Kefka Says. The status IDs and the boss tell mapping come from the
/// DMU Helper plugin and were checked against four logged pulls.
/// </summary>
internal static class KefkaSaysRules
{
    public const string NeoExdeathName = "Neo Exdeath";
    public const string ChaosName = "Chaos";
    public const string KefkaName = "Kefka";

    /// <summary>The element names a Mana Charge bank and a Mana Release marker use.</summary>
    public const string LightningElement = "Lightning";

    public const string IceElement = "Ice";

    /// <summary>Hidden status on Neo Exdeath and Chaos whose parameter says whether the next mechanic is real.</summary>
    public const uint BossTellStatusId = 2056;

    public const uint BeyondDeath1StatusId = 5464;
    public const uint BeyondDeath2StatusId = 1382;

    /// <summary>Kefka's Mana Charge status for lightning. The ice counterpart has not been logged yet.</summary>
    public const uint ThunderChargedStatusId = 1485;

    public const uint IceChargedStatusId = 1484;

    // Every pull handed out debuffs that run out 51, 60, 76 or 84 s after the first one, so 68 s splits short from long.
    public const float LongTierThresholdSeconds = 68.0f;

    /// <summary>Maps a status ID to the debuff it belongs to, or null when the status is not tracked.</summary>
    public static KefkaDebuffKind? KindOf(uint statusId)
    {
        return statusId switch
        {
            5545 => KefkaDebuffKind.CompressedWater,
            5544 => KefkaDebuffKind.ForkedLightning,
            5543 => KefkaDebuffKind.CursedShriek,
            5546 => KefkaDebuffKind.AccelerationBomb,
            5548 => KefkaDebuffKind.DynamicFluid,
            5547 => KefkaDebuffKind.Entropy,
            454 => KefkaDebuffKind.AllaganField,
            BeyondDeath1StatusId or BeyondDeath2StatusId => KefkaDebuffKind.BeyondDeath,
            _ => null,
        };
    }

    /// <summary>
    /// The wound a Wound status points to. White Wound 1 and Black Wound 2 point to black, the other two to white.
    /// </summary>
    public static KefkaWound WoundOf(uint statusId)
    {
        return statusId switch
        {
            4887 or 5542 => KefkaWound.Black,
            4888 or 5541 => KefkaWound.White,
            _ => KefkaWound.None,
        };
    }

    /// <summary>Converts a boss tell parameter into Real or Fake (1119 and 1121 are fake, 1120 and 1122 real).</summary>
    public static KefkaReality RealityOfTell(int param)
    {
        return param switch
        {
            1120 or 1122 => KefkaReality.Real,
            1119 or 1121 => KefkaReality.Fake,
            _ => KefkaReality.Unknown,
        };
    }

    /// <summary>The boss whose tell decides a debuff: Neo Exdeath for Grand Cross and Allagan Field, Chaos for the delayed AoEs.</summary>
    public static string? TellSourceOf(KefkaDebuffKind kind)
    {
        return kind switch
        {
            KefkaDebuffKind.DynamicFluid or KefkaDebuffKind.Entropy => ChaosName,
            KefkaDebuffKind.BeyondDeath => null,
            _ => NeoExdeathName,
        };
    }

    /// <summary>The step in which a debuff resolves; the Flood debuffs resolve during the Flood step.</summary>
    public static KefkaStep StepOf(KefkaDebuffKind kind, bool isLong)
    {
        return kind switch
        {
            KefkaDebuffKind.AccelerationBomb => isLong ? KefkaStep.LongBomb : KefkaStep.ShortBomb,
            KefkaDebuffKind.CompressedWater or KefkaDebuffKind.ForkedLightning =>
                isLong ? KefkaStep.LongStackSpread : KefkaStep.ShortStackSpread,
            KefkaDebuffKind.CursedShriek => isLong ? KefkaStep.LongGazes : KefkaStep.ShortGazes,
            KefkaDebuffKind.Entropy => KefkaStep.Inferno,
            KefkaDebuffKind.DynamicFluid => KefkaStep.Tsunami,
            _ => KefkaStep.FloodOfNaught,
        };
    }

    public static string StepName(KefkaStep step)
    {
        return step switch
        {
            KefkaStep.FloodOfNaught => "Flood of Naught",
            KefkaStep.ShortBomb => "Short Acceleration Bomb",
            KefkaStep.ShortStackSpread => "Short Stack / Spread",
            KefkaStep.ManaCharge1 => "Mana Charge",
            KefkaStep.ShortGazes => "Short gazes",
            KefkaStep.Inferno => "Inferno",
            KefkaStep.LongBomb => "Long Acceleration Bomb",
            KefkaStep.ManaCharge2 => "Mana Charge",
            KefkaStep.LongStackSpread => "Long Stack / Spread",
            KefkaStep.LongGazes => "Long gazes",
            KefkaStep.Tsunami => "Tsunami",
            _ => "Mana Release",
        };
    }

    public static string RealityName(KefkaReality reality)
    {
        return reality switch
        {
            KefkaReality.Real => "Real",
            KefkaReality.Fake => "Fake",
            _ => "?",
        };
    }

    /// <summary>What a debuff asks of its holder once its real/fake value is known.</summary>
    public static string Outcome(KefkaDebuffKind kind, KefkaReality reality)
    {
        if (reality == KefkaReality.Unknown)
        {
            return "?";
        }

        var isReal = reality == KefkaReality.Real;
        return kind switch
        {
            KefkaDebuffKind.CompressedWater => isReal ? "Stack" : "Spread",
            KefkaDebuffKind.ForkedLightning => isReal ? "Spread" : "Stack",
            KefkaDebuffKind.CursedShriek => isReal ? "Look away" : "Look at",
            KefkaDebuffKind.AccelerationBomb => isReal ? "Stand still" : "Keep moving",
            KefkaDebuffKind.Entropy => isReal ? "Point-blank" : "Donut",
            KefkaDebuffKind.DynamicFluid => isReal ? "Donut" : "Point-blank",
            _ => "?",
        };
    }

    /// <summary>Whether a Flood debuff must be allowed to kill its holder; null while the Allagan Field tell is unknown.</summary>
    public static bool? MustDie(KefkaDebuffRecord debuff)
    {
        return debuff.Kind switch
        {
            KefkaDebuffKind.AllaganField => debuff.Reality switch
            {
                KefkaReality.Real => false,
                KefkaReality.Fake => true,
                _ => null,
            },
            KefkaDebuffKind.BeyondDeath => debuff.StatusId == BeyondDeath2StatusId,
            _ => null,
        };
    }

    /// <summary>
    /// The wound colour a player has to stand in for Flood of Naught: their own wound if they must die, the other if not.
    /// </summary>
    public static (KefkaWound Destination, bool? MustDie) FloodPlan(KefkaPlayerRecord player)
    {
        var flood = player.Debuffs.FirstOrDefault(debuff => debuff.Kind is KefkaDebuffKind.AllaganField or KefkaDebuffKind.BeyondDeath);
        if (flood is null || player.Wound == KefkaWound.None)
        {
            return (KefkaWound.None, null);
        }

        var mustDie = MustDie(flood);
        var destination = mustDie switch
        {
            true => player.Wound,
            false => player.Wound == KefkaWound.White ? KefkaWound.Black : KefkaWound.White,
            _ => KefkaWound.None,
        };

        return (destination, mustDie);
    }

    /// <summary>Describes the Flood plan, for example "White half, die".</summary>
    public static string FloodLabel(KefkaPlayerRecord player)
    {
        var (destination, mustDie) = FloodPlan(player);
        if (destination == KefkaWound.None)
        {
            return "?";
        }

        return $"{destination} half, {(mustDie == true ? "die" : "live")}";
    }

    /// <summary>Real if the number of fakes is even, otherwise fake: two fakes cancel out. Unknown if any value is unknown.</summary>
    public static KefkaReality Combine(IEnumerable<KefkaReality> values)
    {
        var list = values.ToList();
        if (list.Count == 0 || list.Contains(KefkaReality.Unknown))
        {
            return KefkaReality.Unknown;
        }

        return list.Count(value => value == KefkaReality.Fake) % 2 == 0 ? KefkaReality.Real : KefkaReality.Fake;
    }

    /// <summary>
    /// Reads one of Kefka's Mana markers: 675 fake ice, 676 real ice, 677 fake thunder, 678 real thunder. Returns null
    /// for every other marker.
    /// </summary>
    public static (string Element, KefkaReality Reality)? ManaMarker(int markerId)
    {
        return markerId switch
        {
            675 => (IceElement, KefkaReality.Fake),
            676 => (IceElement, KefkaReality.Real),
            677 => (LightningElement, KefkaReality.Fake),
            678 => (LightningElement, KefkaReality.Real),
            _ => null,
        };
    }

    /// <summary>The value a Mana Charge banked for an element, or null if that element was not charged (yet).</summary>
    public static KefkaReality? BankedReality(KefkaSaysRun run, string element)
    {
        return run.ManaBanks.FirstOrDefault(bank => bank.Element == element)?.Reality;
    }

    /// <summary>
    /// The value Kefka's Mana Release marker showed for an element. The markers come after the last Mana Charge marker;
    /// the ones before belong to the Grand Cross waves and the charges themselves.
    /// </summary>
    public static KefkaReality ReleaseReality(KefkaSaysRun run, string element)
    {
        if (run.ManaBanks.Count < 2)
        {
            return KefkaReality.Unknown;
        }

        var after = run.ManaBanks.Max(bank => bank.Seconds);
        foreach (var signal in run.Signals.Where(signal => signal.Source == KefkaName && signal.Seconds > after + 0.5f).OrderBy(signal => signal.Seconds))
        {
            if (ManaMarker(signal.Value) is { } marker && marker.Element == element)
            {
                return marker.Reality;
            }
        }

        return KefkaReality.Unknown;
    }

    /// <summary>What an element's Mana Release does: the release marker combined with the value its Mana Charge banked.</summary>
    public static KefkaReality ReleaseFinal(KefkaSaysRun run, string element)
    {
        var release = ReleaseReality(run, element);
        if (release == KefkaReality.Unknown)
        {
            return KefkaReality.Unknown;
        }

        return Combine(run.ManaBanks.Where(bank => bank.Element == element).Select(bank => bank.Reality).Prepend(release));
    }

    /// <summary>The task of a Mana Release element, for example "Stack"; ice is handled like Compressed Water, thunder like Forked Lightning.</summary>
    public static string ReleaseOutcome(KefkaSaysRun run, string element)
    {
        return Outcome(element == IceElement ? KefkaDebuffKind.CompressedWater : KefkaDebuffKind.ForkedLightning, ReleaseFinal(run, element));
    }

    /// <summary>The Dynamic Fluid value the Tsunami resolves with.</summary>
    public static KefkaReality TsunamiFinal(KefkaSaysRun run)
    {
        return StepReality(run, KefkaStep.Tsunami);
    }

    /// <summary>The real/fake value shared by the debuffs that resolve in a step, or unknown when none has been seen.</summary>
    public static KefkaReality StepReality(KefkaSaysRun run, KefkaStep step)
    {
        return run.Players
            .SelectMany(player => player.Debuffs)
            .Where(debuff => debuff.Kind != KefkaDebuffKind.BeyondDeath && StepOf(debuff.Kind, debuff.IsLong) == step)
            .Select(debuff => debuff.Reality)
            .FirstOrDefault(reality => reality != KefkaReality.Unknown);
    }

    /// <summary>Finds the debuff a player holds in a step, if any.</summary>
    public static KefkaDebuffRecord? FindDebuff(KefkaPlayerRecord player, KefkaStep step)
    {
        return player.Debuffs.FirstOrDefault(debuff =>
            debuff.Kind != KefkaDebuffKind.BeyondDeath && StepOf(debuff.Kind, debuff.IsLong) == step);
    }

    /// <summary>
    /// What a player should do in a step. <paramref name="involved"/> is true when the player holds the debuff that
    /// resolves; for gazes everybody else gets the look direction.
    /// </summary>
    public static string LabelFor(KefkaSaysRun run, KefkaPlayerRecord player, KefkaStep step, out bool involved)
    {
        involved = false;
        switch (step)
        {
            case KefkaStep.FloodOfNaught:
                involved = player.Debuffs.Any(debuff => debuff.Kind is KefkaDebuffKind.AllaganField or KefkaDebuffKind.BeyondDeath);
                return FloodLabel(player);
            case KefkaStep.ShortGazes or KefkaStep.LongGazes:
                var gaze = FindDebuff(player, step);
                if (gaze is not null)
                {
                    involved = true;
                    return gaze.Reality switch
                    {
                        KefkaReality.Real => "Gaze: stand out",
                        KefkaReality.Fake => "Gaze: stand center",
                        _ => "?",
                    };
                }

                return StepReality(run, step) switch
                {
                    KefkaReality.Real => "Look away",
                    KefkaReality.Fake => "Look at",
                    _ => string.Empty,
                };
            case KefkaStep.Tsunami:
                involved = true;
                return Outcome(KefkaDebuffKind.DynamicFluid, TsunamiFinal(run));
            case KefkaStep.ManaRelease:
                involved = true;
                return $"Ice {ReleaseOutcome(run, IceElement)}, Thunder {ReleaseOutcome(run, LightningElement)}";
            case KefkaStep.ManaCharge1 or KefkaStep.ManaCharge2:
                return string.Empty;
            default:
                var debuff = FindDebuff(player, step);
                involved = debuff is not null;
                return debuff is null ? string.Empty : Outcome(debuff.Kind, debuff.Reality);
        }
    }

    /// <summary>A short description of how a step turned out, shown next to its name.</summary>
    public static string ResultFor(KefkaSaysRun run, KefkaStep step)
    {
        switch (step)
        {
            case KefkaStep.FloodOfNaught:
                var allagan = run.Players.SelectMany(player => player.Debuffs)
                    .FirstOrDefault(debuff => debuff.Kind == KefkaDebuffKind.AllaganField);
                return allagan is null ? string.Empty : $"Allagan Field {RealityName(allagan.Reality)}";
            case KefkaStep.ManaCharge1:
                return DescribeBank(run, 0);
            case KefkaStep.ManaCharge2:
                return DescribeBank(run, 1);
            case KefkaStep.ManaRelease:
                return $"Ice {DescribeRelease(run, IceElement)}, Thunder {DescribeRelease(run, LightningElement)}";
            default:
                return RealityName(StepReality(run, step));
        }
    }

    /// <summary>For example "Fake + Real = Fake": the release marker, the banked charge and what they add up to.</summary>
    private static string DescribeRelease(KefkaSaysRun run, string element)
    {
        var bank = BankedReality(run, element) is { } banked ? RealityName(banked) : "no charge";
        return $"{RealityName(ReleaseReality(run, element))} + {bank} = {RealityName(ReleaseFinal(run, element))}";
    }

    private static string DescribeBank(KefkaSaysRun run, int index)
    {
        return index < run.ManaBanks.Count
            ? $"Banked {run.ManaBanks[index].Element}: {RealityName(run.ManaBanks[index].Reality)}"
            : "Banking...";
    }
}
