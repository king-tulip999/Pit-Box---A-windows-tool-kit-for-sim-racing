namespace Core.SimRacing.Shakers.Games;

internal sealed record ShakerGame(string Id, string Name, string[] Processes, int Port, string? Setup, Func<int, Action<ShakerInput>, GameReader> Open)
{
    public bool UsesPort => Port > 0;

    public static ShakerGame Memory(string id, string name, string[] processes, Func<IMemoryParser> parser, string? setup = null) =>
        new(id, name, processes, 0, setup, (_, sink) => new MemoryReader(name, parser(), sink));

    public static ShakerGame Packets(string id, string name, string[] processes, int port, string setup, Func<IPacketParser> parser) =>
        new(id, name, processes, port, setup, (p, sink) => new PacketReader(name, p, parser(), sink));
}
