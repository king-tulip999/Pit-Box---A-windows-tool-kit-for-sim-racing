namespace Core.SimRacing.Shakers.Games;

internal static class ShakerGames
{
    public const int F1Port = 20777, CodemastersPort = 20777, WrcPort = 20777, ForzaPort = 5300, BeamPort = 4444, LfsPort = 30000;

    public static readonly IReadOnlyList<ShakerGame> All = new[]
    {
        ShakerGame.Memory("ac", "Assetto Corsa", new[] { "acs.exe" }, () => new KunosParser(KunosGame.Classic)),
        ShakerGame.Memory("acc", "Assetto Corsa Competizione", new[] { "AC2-Win64-Shipping.exe" }, () => new KunosParser(KunosGame.Competizione)),
        ShakerGame.Memory("acevo", "Assetto Corsa EVO", new[] { "AssettoCorsaEVO.exe" }, () => new KunosParser(KunosGame.Evo)),
        ShakerGame.Memory("acrally", "Assetto Corsa Rally", new[] { "acr.exe" }, () => new KunosParser(KunosGame.Rally)),
        ShakerGame.Memory("rf2", "rFactor 2", new[] { "rFactor2.exe" }, () => new RFactor2Parser(),
            "Needs the rF2 shared memory map plugin in Bin64\\Plugins, switched on in CustomPluginVariables.JSON."),
        ShakerGame.Memory("lmu", "Le Mans Ultimate", new[] { "Le Mans Ultimate.exe" }, () => new LmuParser(),
            "Turn on Settings → Gameplay → Enable plugins in the game, then restart it."),
        ShakerGame.Memory("ams2", "Automobilista 2", new[] { "AMS2AVX.exe", "AMS2.exe" }, () => new PcarsParser(),
            "Set Options → System → Shared memory to “Project CARS 2” in the game."),
        ShakerGame.Memory("pcars", "Project CARS 2 and 3", new[] { "pCARS2AVX.exe", "pCARS2.exe", "pCARS3.exe", "pCARS3AVX.exe" }, () => new PcarsParser(),
            "Set shared memory to “Project CARS 2” in the game's options."),
        ShakerGame.Memory("r3e", "RaceRoom", new[] { "RRRE64.exe", "RRRE.exe" }, () => new RaceRoomParser()),
        ShakerGame.Packets("f1", "F1 23, 24 and 25", new[] { "F1_23.exe", "F1_24.exe", "F1_25.exe" }, F1Port,
            "Turn on UDP telemetry in the game's settings, IP 127.0.0.1, the port below, send rate 60 Hz.", () => new F1Parser()),
        ShakerGame.Packets("forza", "Forza Motorsport and Horizon", new[] { "forza_gaming.desktop.x64_release_final.exe", "ForzaHorizon5.exe", "ForzaHorizon4.exe" }, ForzaPort,
            "Turn on Data out in the game's HUD and gameplay settings, IP 127.0.0.1, the port below, Car dash format.", () => new ForzaParser()),
        ShakerGame.Packets("codemasters", "DiRT Rally, DiRT 4 and GRID", new[] { "drt.exe", "dirtrally2.exe", "dirt4.exe", "dirt5.exe", "Grid_dx12.exe", "GridLegends.exe" }, CodemastersPort,
            "In Documents\\My Games\\<game>\\hardwaresettings\\hardware_settings_config.xml set <udp enabled=\"true\" extradata=\"3\" ip=\"127.0.0.1\"> with the port below.", () => new CodemastersParser()),
        ShakerGame.Packets("wrc", "EA SPORTS WRC", new[] { "WRC.exe" }, WrcPort,
            "In Documents\\My Games\\WRC\\telemetry\\config.json set bEnabled to true on the “wrc” session_update packet, with the port below.", () => new WrcParser()),
        ShakerGame.Packets("beamng", "BeamNG.drive", new[] { "BeamNG.drive.x64.exe", "BeamNG.drive.exe" }, BeamPort,
            "Turn on OutGauge and Motion sim under Options → Other → Protocols, both to 127.0.0.1 and the port below.", () => new OutGaugeParser(beam: true)),
        ShakerGame.Packets("lfs", "Live for Speed", new[] { "LFS.exe" }, LfsPort,
            "In cfg.txt set OutGauge and OutSim Mode 1, IP 127.0.0.1, both on the port below, OutSim Opts 0.", () => new OutGaugeParser(beam: false)),
    };

    public static ShakerGame? Find(string id) => All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
}
