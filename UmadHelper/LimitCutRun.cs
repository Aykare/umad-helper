using System;
using System.Collections.Generic;

namespace SamplePlugin;

/// <summary>Direction the Kefka clones' AoEs rotate around the arena.</summary>
internal enum LimitCutRotation
{
    Unknown,
    Clockwise,
    CounterClockwise,
}

/// <summary>One party member's limit cut number during a run.</summary>
internal sealed class LimitCutPlayerRecord
{
    public uint EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsLocalPlayer { get; set; }

    /// <summary>The limit cut number, 1 to 8.</summary>
    public int Number { get; set; }

    /// <summary>The player's job ID (see <see cref="JobIcons"/>), or 0 when it was not known.</summary>
    public uint JobId { get; set; }

    /// <summary>World X where the player stood when their attack resolved, if it was seen.</summary>
    public float? ActualX { get; set; }

    /// <summary>World Z where the player stood when their attack resolved, if it was seen.</summary>
    public float? ActualZ { get; set; }

    /// <summary>World X of the clone whose line attack hit this player, if it was seen.</summary>
    public float? CloneX { get; set; }

    /// <summary>World Z of the clone whose line attack hit this player, if it was seen.</summary>
    public float? CloneZ { get; set; }

    /// <summary>Direction of that clone's line in degrees clockwise from north.</summary>
    public float? AimFacing { get; set; }
}

/// <summary>A single Limit Cut attempt, kept so numbers and resolutions can be reviewed after the pull.</summary>
internal sealed class LimitCutRun : IRecordedRun
{
    public DateTime StartedUtc { get; set; }

    /// <summary>Index into <see cref="LimitCutStrategy.RingLabels"/> where the first clone stood, if seen.</summary>
    public int? OriginIndex { get; set; }

    public LimitCutRotation Rotation { get; set; }

    public List<LimitCutPlayerRecord> Players { get; set; } = [];
}
