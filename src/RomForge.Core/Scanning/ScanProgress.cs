namespace RomForge.Core.Scanning;

public sealed record ScanProgress(int Completed, int Total, string CurrentFile, string Phase = "");
