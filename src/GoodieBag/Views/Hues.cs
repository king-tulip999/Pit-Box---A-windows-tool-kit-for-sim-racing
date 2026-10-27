namespace GoodieBag.Views;

internal static class Hues
{
    public static readonly (string Soft, string Deep) Accent = ("AccentSoft", "AccentText");

    private static readonly (string Soft, string Deep)[] Pairs =
        { Accent, ("SlateSoft", "SlateDeep"), ("SageSoft", "SageDeep"), ("CoralSoft", "CoralDeep"), ("PlumSoft", "PlumDeep"), ("GoldSoft", "GoldDeep") };

    public static (string Soft, string Deep) At(int i) => Pairs[((i % Pairs.Length) + Pairs.Length) % Pairs.Length];

    public static (string Soft, string Deep) Named(string name) => name switch
    {
        "Slate" => Pairs[1], "Sage" => Pairs[2], "Coral" => Pairs[3], "Plum" => Pairs[4], "Gold" => Pairs[5], _ => Accent,
    };
}
