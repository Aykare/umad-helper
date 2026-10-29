using System;
using System.Collections.Generic;

namespace SamplePlugin;

/// <summary>Whether a mechanic is shown as it appears (Real) or does the opposite (Fake).</summary>
internal enum KefkaReality
{
    Unknown,
    Real,
    Fake,
}

/// <summary>The debuff families handed out during Kefka Says.</summary>
internal enum KefkaDebuffKind
{
    CompressedWater,
    ForkedLightning,
    CursedShriek,
    AccelerationBomb,
    DynamicFluid,
    Entropy,
    AllaganField,
    BeyondDeath,
}

/// <summary>The wound a player carries during Flood of Naught, already resolved to the side it points to.</summary>
internal enum KefkaWound
{
    None,
    White,
    Black,
}

/// <summary>The twelve resolving mechanics in the order they are resolved.</summary>
internal enum KefkaStep
{
    FloodOfNaught,
    ShortBomb,
    ShortStackSpread,
    ManaCharge1,
    ShortGazes,
    Inferno,
    LongBomb,
    ManaCharge2,
    LongStackSpread,
    LongGazes,
    Tsunami,
    ManaRelease,
}

/// <summary>One debuff a player received, with the real/fake value that applied to it when it was handed out.</summary>
internal sealed class KefkaDebuffRecord
{
    public uint StatusId { get; set; }

    public KefkaDebuffKind Kind { get; set; }

    public KefkaReality Reality { get; set; }

    /// <summary>True for the debuffs that resolve in the second half (the 76 s bomb, stack or spread, and the 84 s gaze).</summary>
    public bool IsLong { get; set; }

    /// <summary>When the debuff runs out, in seconds after the first Grand Cross debuff was handed out.</summary>
    public float ExpiresAtSeconds { get; set; }
}

/// <summary>One party member's debuffs during a Kefka Says run.</summary>
internal sealed class KefkaPlayerRecord
{
    public uint EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsLocalPlayer { get; set; }

    /// <summary>The player's job ID (see <see cref="JobIcons"/>), or 0 when it was not known.</summary>
    public uint JobId { get; set; }

    /// <summary>The wound this player pointed to when Flood of Naught handed it out, if one was seen.</summary>
    public KefkaWound Wound { get; set; }

    /// <summary>The wound status exactly as it was applied (it can differ between pulls), or 0 when not seen.</summary>
    public uint WoundStatusId { get; set; }

    public List<KefkaDebuffRecord> Debuffs { get; set; } = [];
}

/// <summary>Where one player stood when a step resolved and what they were supposed to do.</summary>
internal sealed class KefkaStepPlayer
{
    public uint EntityId { get; set; }

    public float? X { get; set; }

    public float? Z { get; set; }

    /// <summary>What the player should do in this step, for example "Stack" or "Look away".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True when the player holds the debuff that is resolving (as opposed to only reacting to it).</summary>
    public bool Involved { get; set; }

    /// <summary>What actually happened to the player, when the game showed it (the Antilight colour that hit them).</summary>
    public string? Actual { get; set; }
}

/// <summary>Where one of the Flood of Naught Antilight half-room attacks came from.</summary>
internal sealed class KefkaAreaRecord
{
    /// <summary>"White" or "Black".</summary>
    public string Colour { get; set; } = string.Empty;

    public float CasterX { get; set; }

    public float CasterZ { get; set; }
}

/// <summary>A resolved mechanic: when it happened, the outcome and where everybody stood.</summary>
internal sealed class KefkaStepRecord
{
    public KefkaStep Step { get; set; }

    /// <summary>When the step resolved, in seconds after the first Grand Cross debuff was handed out.</summary>
    public float Seconds { get; set; }

    /// <summary>A short summary such as "Fake" or "Banked Lightning: Real".</summary>
    public string Result { get; set; } = string.Empty;

    public List<KefkaStepPlayer> Players { get; set; } = [];

    /// <summary>The White and Black Antilight casters of the Flood of Naught step; empty for every other step.</summary>
    public List<KefkaAreaRecord> Areas { get; set; } = [];
}

/// <summary>A hidden signal seen before or during the mechanic: a boss tell or a Kefka head marker.</summary>
internal sealed class KefkaSignalRecord
{
    /// <summary>"Neo Exdeath", "Chaos" or "Kefka".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The tell status parameter (1119-1122) or the head marker ID (673-678).</summary>
    public int Value { get; set; }

    /// <summary>When it was seen, in seconds after the first Grand Cross debuff (negative if before).</summary>
    public float Seconds { get; set; }
}

/// <summary>The Real/Fake value a Mana Charge stored for one element, read from Kefka's marker; Mana Release adds it to its own.</summary>
internal sealed class KefkaManaBankRecord
{
    /// <summary>"Lightning" or "Ice".</summary>
    public string Element { get; set; } = string.Empty;

    public KefkaReality Reality { get; set; }

    /// <summary>When Kefka's marker for this charge appeared, in seconds after the first Grand Cross debuff.</summary>
    public float Seconds { get; set; }
}

/// <summary>A single Kefka Says attempt, kept so each resolving mechanic can be reviewed after the pull.</summary>
internal sealed class KefkaSaysRun : IRecordedRun
{
    public DateTime StartedUtc { get; set; }

    public List<KefkaPlayerRecord> Players { get; set; } = [];

    public List<KefkaStepRecord> Steps { get; set; } = [];

    public List<KefkaSignalRecord> Signals { get; set; } = [];

    public List<KefkaManaBankRecord> ManaBanks { get; set; } = [];
}
