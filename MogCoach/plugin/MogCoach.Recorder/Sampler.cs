using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using MogCoach.Recorder.Model;

namespace MogCoach.Recorder;

/// <summary>
/// Samples the object table on the framework thread, throttled to <see cref="RecorderConfig.SampleHz"/>,
/// and writes snapshots + edge events to a .mogcap file. Continuous positions/headings/cast-state/
/// status/gauge are exactly the data IINACT can't provide.
/// <para>
/// NOTE: Dalamud property names (GameObjectId, CastActionId, StatusList, gauge types, …) are pinned to
/// the SDK this builds against. If a name drifts on your SDK, fix it here — this is the one file that
/// touches game state directly. (Same caveat PartyMoogle notes for its listeners.)
/// </para>
/// </summary>
public sealed class Sampler : IDisposable
{
    private readonly RecorderConfig _cfg;
    private readonly string _defaultDir;
    private readonly CaptureWriter _writer = new();

    private long _lastTick;
    private bool _manual;
    private bool _dutyCompleted;
    private readonly Dictionary<ulong, uint> _lastHp = new();

    public bool IsRecording => _writer.IsOpen;
    public string? CurrentPath => _writer.CurrentPath;

    public Sampler(RecorderConfig cfg, string defaultDir)
    {
        _cfg = cfg;
        _defaultDir = defaultDir;
    }

    public void Enable()
    {
        Service.Framework.Update += OnUpdate;
        Service.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Disable()
    {
        Service.Framework.Update -= OnUpdate;
        Service.DutyState.DutyCompleted -= OnDutyCompleted;
        if (IsRecording) EndPull(null);
    }

    public string? StartManual()
    {
        _manual = true;
        StartFile();
        return CurrentPath;
    }

    public void StopManual()
    {
        _manual = false;
        if (IsRecording) EndPull(null);
    }

    private void OnDutyCompleted(object? _, ushort __) => _dutyCompleted = true;

    private void OnUpdate(IFramework framework)
    {
        var lp = Service.ClientState.LocalPlayer;
        if (lp is null)
        {
            if (IsRecording && !_manual) EndPull(null);
            return;
        }

        var inDuty = Service.Condition[ConditionFlag.BoundByDuty];
        var inCombat = Service.Condition[ConditionFlag.InCombat];

        if (_cfg.AutoRecordInCombat && !_manual)
        {
            var wantOpen = inCombat && (!_cfg.OnlyInDuty || inDuty);
            if (wantOpen && !IsRecording) StartFile();
            else if (!wantOpen && IsRecording) EndPull(_dutyCompleted ? true : null);
        }

        if (!IsRecording) return;

        var interval = Math.Max(1, 1000 / Math.Clamp(_cfg.SampleHz, 1, 60));
        var now = Environment.TickCount64;
        if (now - _lastTick < interval) return;
        _lastTick = now;

        try { WriteSnapshot(lp); }
        catch (Exception ex) { Service.Log.Error(ex, "MogCoach snapshot failed"); }
    }

    private void StartFile()
    {
        var dir = string.IsNullOrWhiteSpace(_cfg.OutputDirectory) ? _defaultDir : _cfg.OutputDirectory;
        var path = Path.Combine(dir, $"session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mogcap");
        _writer.Open(path);
        _dutyCompleted = false;
        _lastHp.Clear();

        var lp = Service.ClientState.LocalPlayer;
        _writer.Write(new HeaderRecord
        {
            Ts = Now(),
            Player = lp?.Name.TextValue,
            PlayerId = lp is null ? null : Hex(lp.GameObjectId),
            Job = JobName(lp?.ClassJob.RowId ?? 0),
            SampleHz = _cfg.SampleHz,
        });
        _writer.Write(new EventRecord { Ts = Now(), Type = "PullStart" });
        Service.Log.Information($"MogCoach recording -> {path}");
    }

    private void EndPull(bool? cleared)
    {
        _writer.Write(new EventRecord { Ts = Now(), Type = "PullEnd", Cleared = cleared });
        Service.Log.Information($"MogCoach saved -> {CurrentPath}");
        _writer.Close();
    }

    private void WriteSnapshot(IPlayerCharacter localPlayer)
    {
        var selfId = localPlayer.GameObjectId;
        var actors = new List<ActorRecord>();

        foreach (var obj in Service.ObjectTable)
        {
            if (obj is not IBattleChara chara) continue;

            var kind = Classify(chara, selfId);
            if (kind is null) continue;
            if (kind == "enemy" && Distance2D(localPlayer, chara) > _cfg.EnemyRadiusYalms) continue;

            var rec = new ActorRecord
            {
                Id = Hex(chara.GameObjectId),
                Name = chara.Name.TextValue,
                Kind = kind,
                X = chara.Position.X,
                Z = chara.Position.Z,
                H = chara.Rotation,
                Hp = chara.CurrentHp,
                Hpm = chara.MaxHp,
                Tgt = chara.TargetObjectId != 0 ? Hex(chara.TargetObjectId) : null,
                Cast = BuildCast(chara),
            };

            if (kind == "self")
            {
                rec.St = BuildStatuses(chara);
                rec.Gauge = BuildGauge(localPlayer.ClassJob.RowId);
            }

            actors.Add(rec);
            DetectDeath(chara);
        }

        _writer.Write(new SnapshotRecord { Ts = Now(), Actors = actors });
    }

    private void DetectDeath(IBattleChara chara)
    {
        var id = chara.GameObjectId;
        var hp = chara.CurrentHp;
        if (_lastHp.TryGetValue(id, out var prev) && prev > 0 && hp == 0)
            _writer.Write(new EventRecord { Ts = Now(), Type = "Death", Id = Hex(id), Name = chara.Name.TextValue });
        _lastHp[id] = hp;
    }

    private static CastRecord? BuildCast(IBattleChara chara)
    {
        if (!chara.IsCasting) return null;
        return new CastRecord
        {
            Id = Hex(chara.CastActionId),
            El = chara.CurrentCastTime,
            Tot = chara.TotalCastTime,
            Tgt = chara.CastTargetObjectId != 0 ? Hex(chara.CastTargetObjectId) : null,
        };
    }

    private static List<StatusRecord> BuildStatuses(IBattleChara chara)
    {
        var list = new List<StatusRecord>();
        foreach (var s in chara.StatusList)
        {
            if (s is null || s.StatusId == 0) continue;
            list.Add(new StatusRecord
            {
                Id = Hex(s.StatusId),
                Rem = s.RemainingTime,
                Stk = s.Param,
                Src = s.SourceId != 0 ? Hex(s.SourceId) : null,
            });
        }
        return list;
    }

    /// <summary>Best-effort job gauge. Extend the switch per job as needed (WAR shown as the pattern).</summary>
    private static Dictionary<string, double>? BuildGauge(uint jobRowId)
    {
        try
        {
            switch (jobRowId)
            {
                case 21: // Warrior
                    var war = Service.JobGauges.Get<WARGauge>();
                    return new Dictionary<string, double> { ["beastGauge"] = war.BeastGauge };
                default:
                    return null; // TODO: add remaining jobs
            }
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Gauge read failed for job {Job}", jobRowId);
            return null;
        }
    }

    private static string? Classify(IBattleChara chara, ulong selfId)
    {
        if (chara.GameObjectId == selfId) return "self";
        // Inside a duty the only other players are your party, so any non-self player is "party".
        // (OnlyInDuty defaults true; outside a duty this may include randoms — acceptable.)
        if (chara.ObjectKind == ObjectKind.Player) return "party";
        if (chara is IBattleNpc npc && npc.BattleNpcKind == BattleNpcSubKind.Enemy) return "enemy";
        return null;
    }

    private static float Distance2D(IGameObject a, IGameObject b)
    {
        var dx = a.Position.X - b.Position.X;
        var dz = a.Position.Z - b.Position.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    private static string Hex(ulong v) => v.ToString("X", CultureInfo.InvariantCulture);
    private static string Hex(uint v) => v.ToString("X", CultureInfo.InvariantCulture);

    private static string JobName(uint id) => id switch
    {
        19 => "Paladin", 20 => "Monk", 21 => "Warrior", 22 => "Dragoon", 23 => "Bard",
        24 => "WhiteMage", 25 => "BlackMage", 27 => "Summoner", 28 => "Scholar", 30 => "Ninja",
        31 => "Machinist", 32 => "DarkKnight", 33 => "Astrologian", 34 => "Samurai", 35 => "RedMage",
        37 => "Gunbreaker", 38 => "Dancer", 39 => "Reaper", 40 => "Sage", 41 => "Viper", 42 => "Pictomancer",
        _ => "Unknown",
    };

    public void Dispose() => Disable();
}
