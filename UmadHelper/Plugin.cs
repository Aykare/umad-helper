using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/pmycommand";

    internal GameEventMonitor GameEventMonitor { get; }
    internal P2ForsakenTracker P2ForsakenTracker { get; }
    internal LimitCutTracker LimitCutTracker { get; }
    internal KefkaSaysTracker KefkaSaysTracker { get; }
    internal DecisiveBattleTracker DecisiveBattleTracker { get; }
    internal RunHistory<ForsakenRun> ForsakenHistory { get; }
    internal RunHistory<LimitCutRun> LimitCutHistory { get; }
    internal RunHistory<KefkaSaysRun> KefkaSaysHistory { get; }
    internal RunHistory<DecisiveBattleRun> DecisiveBattleHistory { get; }
    internal Configuration Configuration { get; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private HistoryWindow HistoryWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.SeedDefaultPositions();
        ForsakenHistory = new RunHistory<ForsakenRun>(PluginInterface.GetPluginConfigDirectory(), "forsaken");
        LimitCutHistory = new RunHistory<LimitCutRun>(PluginInterface.GetPluginConfigDirectory(), "limit-cut");
        KefkaSaysHistory = new RunHistory<KefkaSaysRun>(PluginInterface.GetPluginConfigDirectory(), "kefka-says");
        DecisiveBattleHistory = new RunHistory<DecisiveBattleRun>(PluginInterface.GetPluginConfigDirectory(), "decisive-battle");
        GameEventMonitor = new GameEventMonitor();
        P2ForsakenTracker = new P2ForsakenTracker(GameEventMonitor, Configuration, ForsakenHistory);
        LimitCutTracker = new LimitCutTracker(GameEventMonitor, LimitCutHistory);
        KefkaSaysTracker = new KefkaSaysTracker(GameEventMonitor, KefkaSaysHistory);
        DecisiveBattleTracker = new DecisiveBattleTracker(DecisiveBattleHistory);

        var markerImageDirectory = PluginInterface.AssemblyLocation.Directory?.FullName!;

        ConfigWindow = new ConfigWindow(this);
        HistoryWindow = new HistoryWindow(this, markerImageDirectory);
        MainWindow = new MainWindow(this, markerImageDirectory);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(HistoryWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        P2ForsakenTracker.Dispose();
        LimitCutTracker.Dispose();
        KefkaSaysTracker.Dispose();
        DecisiveBattleTracker.Dispose();
        GameEventMonitor.Dispose();

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        HistoryWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleHistoryUi() => HistoryWindow.Toggle();

    /// <summary>Opens the history window showing sample data for a mechanic.</summary>
    public void StartHistoryPreview(Mechanic mechanic)
    {
        HistoryWindow.StartPreview(mechanic);
        HistoryWindow.IsOpen = true;
    }

    /// <summary>Opens the main window in preview mode for a mechanic.</summary>
    public void StartPreview(Mechanic mechanic)
    {
        MainWindow.StartPreview(mechanic);
        MainWindow.IsOpen = true;
    }

    public void ToggleMainUi() => MainWindow.Toggle();
}
