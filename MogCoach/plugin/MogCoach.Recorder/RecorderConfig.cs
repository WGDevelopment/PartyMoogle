using Dalamud.Configuration;

namespace MogCoach.Recorder;

[Serializable]
public sealed class RecorderConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Snapshot rate in Hz (throttled off IFramework.Update). 10 is ample for movement/positioning.</summary>
    public int SampleHz { get; set; } = 10;

    /// <summary>Where .mogcap captures are written. Empty ⇒ the plugin config directory.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Only record while in a duty (recommended — avoids capturing hub-city idling).</summary>
    public bool OnlyInDuty { get; set; } = true;

    /// <summary>Automatically start/stop recording as you enter/leave combat within a duty.</summary>
    public bool AutoRecordInCombat { get; set; } = true;

    /// <summary>Include nearby enemies in snapshots (needed for telegraphs/positioning). Party is always included.</summary>
    public bool IncludeEnemies { get; set; } = true;

    /// <summary>Max distance (yalms) from the player to include an enemy actor. Bounds capture size.</summary>
    public float EnemyRadiusYalms { get; set; } = 60f;
}
