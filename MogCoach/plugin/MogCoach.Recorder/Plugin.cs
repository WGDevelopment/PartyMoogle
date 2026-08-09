using System;
using Dalamud.Game.Command;
using Dalamud.Plugin;

namespace MogCoach.Recorder;

/// <summary>
/// MogCoach capture recorder. Observe-only: samples game state to a .mogcap file for post-session
/// analysis. Never sends input to the game. Use <c>/mogrec</c> to control it.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "MogCoach Recorder";
    private const string CommandName = "/mogrec";

    public static RecorderConfig Config { get; private set; } = new();
    private readonly Sampler _sampler;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        Config = Service.PluginInterface.GetPluginConfig() as RecorderConfig ?? new RecorderConfig();

        var defaultDir = Service.PluginInterface.GetPluginConfigDirectory();
        _sampler = new Sampler(Config, defaultDir);
        _sampler.Enable();

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/mogrec start | stop | status — control MogCoach capture recording.",
        });
    }

    public void Dispose()
    {
        _sampler.Dispose();
        Service.CommandManager.RemoveHandler(CommandName);
        Service.PluginInterface.SavePluginConfig(Config);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "start":
                var path = _sampler.StartManual();
                Service.ChatGui.Print($"[MogCoach] Recording -> {path}");
                break;

            case "stop":
                _sampler.StopManual();
                Service.ChatGui.Print("[MogCoach] Recording stopped.");
                break;

            case "status":
            case "":
                Service.ChatGui.Print(_sampler.IsRecording
                    ? $"[MogCoach] Recording -> {_sampler.CurrentPath}"
                    : "[MogCoach] Idle. Auto-record " +
                      (Config.AutoRecordInCombat ? "ON (starts on combat)." : "OFF — use /mogrec start."));
                break;

            default:
                Service.ChatGui.Print("[MogCoach] Usage: /mogrec start | stop | status");
                break;
        }
    }
}
