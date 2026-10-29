namespace PitBox.Config;

internal static class LegacyName
{
    public const string Code = "GoodieBag";
    public const string Task = "Goodie Bag";
    public const string ExportHeader = "goodieBagExport";
    public const string LapHeader = "goodieBagLap";
    public const string LaunchSetting = "launchWithGoodieBag";
    public const string AppMutex = @"Local\" + Code + ".SingleInstance";
    public const string ShowEvent = @"Local\" + Code + ".ShowWindow";
    public const string FanMutex = @"Local\" + Code + ".FanService";
    public const string FanStopEvent = @"Local\" + Code + ".FanService.Stop";
}
