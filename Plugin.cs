using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace GilBanking;

public sealed class Plugin : IDalamudPlugin
{
    public static IPluginLog Log { get; private set; } = null!;
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private const string Command = "/gilbanking";
    private const string ShortCommand = "/gil";

    private readonly ICommandManager commands;
    private readonly IChatGui chatGui;
    private readonly Configuration config;
    private readonly Ledger ledger;
    private readonly GilTracker tracker;
    private readonly ChatTracker chatTracker;
    private readonly UniversalisClient market;
    private readonly ItemTooltip itemTooltip;
    private readonly WindowSystem windows = new("GilBanking");
    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IFramework framework,
        IObjectTable objectTable,
        IDataManager dataManager,
        IGameGui gameGui,
        IPluginLog log)
    {
        PluginInterface = pluginInterface;
        Log = log;
        commands = commandManager;
        this.chatGui = chatGui;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        L.Update(config.Language, pluginInterface.UiLanguage);

        if (config.TooltipUseDataCenter)
        {
            // Frühere Einstellung "ganzes Rechenzentrum" in die neue Auswahl der Preisquelle übernehmen.
            config.TooltipScopeMode = 1;
            config.TooltipUseDataCenter = false;
            config.Save();
        }
        if (config.Version < 2)
        {
            // Ab 0.0.0.4 bleibt der komplette Gil-Verlauf standardmäßig erhalten.
            config.ThinHistory = false;
            config.Version = 2;
            config.Save();
        }
        Worlds.Init(dataManager);

        ledger = new Ledger(pluginInterface.GetPluginConfigDirectory(), config.DataDirectory, config.ExportDirectory)
        {
            SaveIntervalMinutes = config.SaveIntervalMinutes,
        };
        ledger.ProtectSales = config.KeepSalesForever;
        if (config.ThinHistory) ledger.Thin(config.ThinAfterDays);

        tracker = new GilTracker(framework, objectTable, gameGui, dataManager, ledger, config);
        chatTracker = new ChatTracker(chatGui, dataManager, ledger, config, () => tracker.CharacterKey);

        market = new UniversalisClient();
        itemTooltip = new ItemTooltip(gameGui, objectTable, dataManager, market, ledger, config);

        mainWindow = new MainWindow(ledger, tracker, chatTracker, config);
        windows.AddWindow(mainWindow);

        var info = new CommandInfo(OnCommand)
        {
            HelpMessage = L.T("cmd.help"),
        };
        commands.AddHandler(Command, info);
        commands.AddHandler(ShortCommand, info);

        pluginInterface.UiBuilder.Draw += UpdateLanguage; // muss vor den Fenstern laufen
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.Draw += itemTooltip.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        pluginInterface.UiBuilder.OpenConfigUi += mainWindow.ShowSettings;
    }

    /// <summary>Sprache pro Frame prüfen: folgt der Dalamud-Sprache (bei "Automatisch") und der Auswahl in den Einstellungen.</summary>
    private void UpdateLanguage() => L.Update(config.Language, PluginInterface.UiLanguage);

    private void ToggleMain() => mainWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase))
            mainWindow.ShowSettings();
        else if (args.Trim().Equals("tooltip", StringComparison.OrdinalIgnoreCase))
        {
            config.ShowMarketTooltip = !config.ShowMarketTooltip;
            config.Save();
        }
        else
            mainWindow.Toggle();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= UpdateLanguage;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.Draw -= itemTooltip.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.ShowSettings;

        commands.RemoveHandler(Command);
        commands.RemoveHandler(ShortCommand);

        windows.RemoveAllWindows();
        market.Dispose();
        chatTracker.Dispose();
        tracker.Dispose();
        ledger.Flush();
    }
}
