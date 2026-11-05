namespace PitBox.SessionStats;

internal sealed record ComboReport(string Car, string Track, string Config, int Sessions, long Laps, double DriveS, double? Best, int Races, double? IncidentsPerRace,
    int CleanRaces, double? AverageFinish, DateTime? Last, IReadOnlyList<double> OffTracks, int TracedLaps, SimRacing.TrackMaps.TrackMap? Map)
{
    public const int MostTraces = 400, LossLaps = 30;

    public double? TheoreticalBest { get; init; }
    public OffTrackTrend? OffTrackTrend { get; init; }
    public IReadOnlyList<CornerAverage> Loss { get; init; } = Array.Empty<CornerAverage>();
    public int LossLapCount { get; init; }

    public static ComboReport Read(StatsStore store, string car, string track, string config, int? trackId, string trackName)
    {
        var db = store.Db;
        var row = db.Query("""
            SELECT COUNT(*), SUM(laps), SUM(drive_s), MAX(end_utc),
                SUM(session_type = 'Race' AND start_pos > 0),
                AVG(CASE WHEN session_type = 'Race' AND start_pos > 0 THEN incidents END),
                SUM(session_type = 'Race' AND finished = 1 AND incidents = 0),
                AVG(CASE WHEN session_type = 'Race' AND finished = 1 AND finish_pos > 0 THEN finish_pos END)
            FROM sessions WHERE car = ?1 AND track = ?2 AND track_config = ?3
            """, car, track, config)[0];
        double? best = db.Scalar("SELECT MIN(l.time) FROM laps l JOIN sessions s ON s.id = l.session_id WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3",
            car, track, config) is { } b ? Convert.ToDouble(b) : null;
        var sectors = db.Query("SELECT l.sectors FROM laps l JOIN sessions s ON s.id = l.session_id WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3",
            car, track, config).Select(r => (IReadOnlyList<double>?)StatsStore.ParseSectors(r[0] as string));
        double? theoretical = ComboInsights.TheoreticalBest(sectors);
        var traces = store.Traces;
        var referenceAt = traces?.BestOf(car, track, config);
        var reference = referenceAt is { } at ? traces!.Load(at.Session, at.Lap) : null;
        var recent = traces?.Recent(car, track, config, MostTraces, reference is null ? 0 : LossLaps, referenceAt is { } r0 ? (r0.Session, r0.Lap) : null)
            ?? new RecentTraces(new List<double>(), new List<int>(), new List<LapTrace>());
        var loss = reference is not null && recent.Clean.Count > 0 ? ComboInsights.AverageLoss(recent.Clean, reference) : new List<CornerAverage>();
        SimRacing.TrackMaps.TrackMap? map = null;
        try { map = SimRacing.TrackMaps.TrackMapLibrary.ForSession(trackId, trackName, track, config).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.LogError(ex); }
        return new ComboReport(car, track, config, Convert.ToInt32(row[0] ?? 0L), Convert.ToInt64(row[1] ?? 0L), Convert.ToDouble(row[2] ?? 0d), best,
            Convert.ToInt32(row[4] ?? 0L), row[5] is null ? null : Convert.ToDouble(row[5]), Convert.ToInt32(row[6] ?? 0L), row[7] is null ? null : Convert.ToDouble(row[7]),
            row[3] is null ? null : StatsStore.FromUnix(Convert.ToInt64(row[3])), recent.OffTracks, recent.OffTracksPerLap.Count, map)
        {
            TheoreticalBest = theoretical is { } t && best is { } bl && t <= bl ? t : null,
            OffTrackTrend = ComboInsights.Trend(recent.OffTracksPerLap),
            Loss = loss,
            LossLapCount = loss.Count > 0 ? recent.Clean.Count : 0,
        };
    }
}
