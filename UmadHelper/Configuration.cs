using System;
using Dalamud.Configuration;

namespace SamplePlugin;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public ulong SelectedBuddyContentId { get; set; }

    /// <summary>Persists the selected Forsaken buddy.</summary>
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
