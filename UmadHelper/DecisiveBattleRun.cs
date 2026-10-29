using System;
using System.Collections.Generic;

namespace SamplePlugin;

/// <summary>The status a player received from the Decisive Battle: Epic Hero (alpha) or Fated Hero (beta).</summary>
internal enum DecisiveBattleHero
{
    Epic,
    Fated,
}

/// <summary>One player who received a hero status, with where they stood at that moment.</summary>
internal sealed class DecisiveBattlePlayerRecord
{
    public uint EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsLocalPlayer { get; set; }

    public uint JobId { get; set; }

    public DecisiveBattleHero Hero { get; set; }

    public float? X { get; set; }

    public float? Z { get; set; }
}

/// <summary>One cast of the Decisive Battle: where both bosses stood and the players in the order they got their status.</summary>
internal sealed class DecisiveBattleCastRecord
{
    /// <summary>When the statuses were handed out, in seconds after the run's first cast.</summary>
    public float Seconds { get; set; }

    public float? ChaosX { get; set; }

    public float? ChaosZ { get; set; }

    public float? ExdeathX { get; set; }

    public float? ExdeathZ { get; set; }

    /// <summary>In the order the statuses were seen. Statuses that arrive in the same frame cannot be told apart, so they follow party list order.</summary>
    public List<DecisiveBattlePlayerRecord> Players { get; set; } = [];
}

/// <summary>All Decisive Battle casts of one pull.</summary>
internal sealed class DecisiveBattleRun : IRecordedRun
{
    public DateTime StartedUtc { get; set; }

    public List<DecisiveBattleCastRecord> Casts { get; set; } = [];
}
