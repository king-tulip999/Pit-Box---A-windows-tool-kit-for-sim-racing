namespace Velocita;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => Core.Program.Run(Core.AppKind.Velocita, args);
}
