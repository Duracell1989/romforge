namespace RomForge.Core.Models
{
    public sealed record DatHeader
    {
        public string DatName { get; init; } = string.Empty;
        public string System { get; init; } = string.Empty;
        public int DatVersion { get; init; }
        public string? ImFolder { get; init; }
        public int ScreenshotsWidth { get; init; }
        public int ScreenshotsHeight { get; init; }
        public string? NewDatVersionUrl { get; init; }
        public string? NewDatUrl { get; init; }
        public string? NewDatFileName { get; init; }
        public string? NewImUrl { get; init; }
    }
}
