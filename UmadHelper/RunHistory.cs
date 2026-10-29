using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SamplePlugin;

/// <summary>A recorded mechanic attempt that can be stored in a <see cref="RunHistory{TRun}"/>.</summary>
internal interface IRecordedRun
{
    DateTime StartedUtc { get; }
}

/// <summary>
/// Persists every run of one mechanic as its own JSON file inside a per-mechanic folder, so history
/// survives game restarts and new mechanics only need their own folder name.
/// </summary>
internal sealed class RunHistory<TRun> where TRun : class, IRecordedRun
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string directory;

    public RunHistory(string baseDirectory, string mechanicFolder)
    {
        directory = Path.Combine(baseDirectory, "history", mechanicFolder);
        Load();
    }

    /// <summary>All stored runs, newest first.</summary>
    public List<TRun> Runs { get; private set; } = [];

    /// <summary>Writes the run to its own file and lists it as the newest entry.</summary>
    public void Add(TRun run)
    {
        Runs.Insert(0, run);

        try
        {
            Directory.CreateDirectory(directory);
            var filePath = NextFreePath(run.StartedUtc);
            File.WriteAllText(filePath, JsonSerializer.Serialize(run, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Plugin.Log.Error(ex, "Could not save a run to {Directory}.", directory);
        }
    }

    /// <summary>Deletes every stored run file.</summary>
    public void Clear()
    {
        Runs.Clear();
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Plugin.Log.Error(ex, "Could not delete {File}.", file);
            }
        }
    }

    private void Load()
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var loaded = new List<TRun>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<TRun>(File.ReadAllText(file)) is { } run)
                {
                    loaded.Add(run);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // One damaged file must not hide the other runs or stop the plugin from loading.
                Plugin.Log.Error(ex, "Could not read run file {File}.", file);
            }
        }

        Runs = [.. loaded.OrderByDescending(run => run.StartedUtc)];
    }

    /// <summary>Builds a timestamped file name and avoids overwriting a run that started in the same moment.</summary>
    private string NextFreePath(DateTime startedUtc)
    {
        var baseName = startedUtc.ToString("yyyyMMdd-HHmmss-fff");
        var path = Path.Combine(directory, $"{baseName}.json");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{baseName}-{suffix}.json");
        }

        return path;
    }
}
