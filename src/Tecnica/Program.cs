namespace Tecnica;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => Core.Program.Run(Core.AppKind.Tecnica, args);
}
