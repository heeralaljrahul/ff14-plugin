using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/pmycommand";
    private const string MachinistCommandName = "/pmachinist";
    private const string BurstCommandName = "/pmchburst";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MachinistWindow MachinistWindow { get; init; }
    public MachinistRotation MachinistRotation { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");
        var machinistImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "machinist.png");

        MachinistRotation = new MachinistRotation();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        MachinistWindow = new MachinistWindow(this, machinistImagePath, MachinistRotation);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MachinistWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        CommandManager.AddHandler(MachinistCommandName, new CommandInfo(OnMachinistCommand)
        {
            HelpMessage = "Opens the Machinist job interface window"
        });

        CommandManager.AddHandler(BurstCommandName, new CommandInfo(OnBurstCommand)
        {
            HelpMessage = "Toggle Machinist burst mode on/off (can be used in macros)"
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        MachinistWindow.Dispose();
        MachinistRotation.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(MachinistCommandName);
        CommandManager.RemoveHandler(BurstCommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }

    private void OnMachinistCommand(string command, string args)
    {
        // Toggle the Machinist job interface window
        MachinistWindow.Toggle();
    }

    private void OnBurstCommand(string command, string args)
    {
        // Toggle burst mode - can be used in macros
        // Usage: /pmchburst [on|off] or just /pmchburst to toggle
        var arg = args.Trim().ToLower();
        if (arg == "on")
        {
            MachinistRotation.BurstModeEnabled = true;
            Log.Information("Machinist Burst Mode: Enabled");
        }
        else if (arg == "off")
        {
            MachinistRotation.BurstModeEnabled = false;
            Log.Information("Machinist Burst Mode: Disabled");
        }
        else
        {
            MachinistRotation.ToggleBurstMode();
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleMachinistUi() => MachinistWindow.Toggle();
}
