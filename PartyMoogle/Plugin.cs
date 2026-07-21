using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PartyMoogle.Impl;
using PartyMoogle.Reply;
using PartyMoogle.Util;
using PartyMoogle.Windows;

namespace PartyMoogle;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "PartyMoogle";
    private const string CommandName = "/partymoogle";

    private IDalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }

    // This *is* used.
#pragma warning disable CS8618
    public static Configuration Configuration { get; private set; }
#pragma warning restore

    public WindowSystem WindowSystem = new("PartyMoogle");

    private ConfigWindow ConfigWindow { get; init; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager)
    {
        pluginInterface.Create<Service>();

        PluginInterface = pluginInterface;
        CommandManager = commandManager;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the configuration window."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        CrossWorldPartyListSystem.Start();
        PartyListener.On();
        DutyListener.On();
        ChatListener.On();
        ClientStateListener.On();
        ReplyListener.On();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        CrossWorldPartyListSystem.Stop();
        PartyListener.Off();
        DutyListener.Off();
        ChatListener.Off();
        ClientStateListener.Off();
        ReplyListener.Off();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        if (args == "debugOnlineStatus")
        {
            Service.ChatGui.Print($"OnlineStatus ID = {Service.ObjectTable.LocalPlayer!.OnlineStatus.RowId}");
            return;
        }

        // Hidden dev aid: exercise the reply pipeline without ntfy. Routes through the
        // IDENTICAL ReplySender path (same validation + rate limit), so it is not a second
        // unguarded injection surface. Usage: /partymoogle testreply <index> <text>
        const string testReply = "testreply ";
        if (args.StartsWith(testReply, StringComparison.Ordinal))
        {
            var payload = args.Substring(testReply.Length);
            _ = ReplySender.HandleInbound("debug", payload);
            return;
        }

        ConfigWindow.IsOpen = true;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void DrawConfigUI()
    {
        ConfigWindow.IsOpen = true;
    }
}
