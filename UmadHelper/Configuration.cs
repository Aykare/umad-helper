using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace SamplePlugin;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Raid position by character name.</summary>
    public Dictionary<string, RaidPosition>? PositionsByName { get; set; }

    /// <summary>The <see cref="RaidPositions.DefaultsVersion"/> whose defaults have already been applied.</summary>
    public int DefaultPositionsVersion { get; set; }

    /// <summary>Gets a character's configured raid position, or None.</summary>
    public RaidPosition GetPosition(string name)
        => PositionsByName is not null && PositionsByName.TryGetValue(name, out var position) ? position : RaidPosition.None;

    /// <summary>Sets (or clears, with None) a character's raid position.</summary>
    public void SetPosition(string name, RaidPosition position)
    {
        PositionsByName ??= [];
        if (position == RaidPosition.None)
        {
            PositionsByName.Remove(name);
        }
        else
        {
            PositionsByName[name] = position;
        }
    }

    /// <summary>
    /// Adds default positions for names that have none yet. Runs once per <see cref="RaidPositions.DefaultsVersion"/>,
    /// so later edits and removals are kept but newly added defaults still reach existing configurations.
    /// </summary>
    public void SeedDefaultPositions()
    {
        if (DefaultPositionsVersion >= RaidPositions.DefaultsVersion)
        {
            return;
        }

        foreach (var (name, position) in RaidPositions.Defaults)
        {
            if (GetPosition(name) == RaidPosition.None)
            {
                SetPosition(name, position);
            }
        }

        DefaultPositionsVersion = RaidPositions.DefaultsVersion;
        Save();
    }

    /// <summary>Persists the raid positions.</summary>
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
