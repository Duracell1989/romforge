using Avalonia.Media;

namespace RomForge.UI.Converters;

internal static class StatusColors
{
    internal static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
    internal static readonly IBrush Verified = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    internal static readonly IBrush Missing = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
    internal static readonly IBrush IncorrectlyNamed = new SolidColorBrush(
        Color.FromRgb(0xFF, 0x98, 0x00)
    );
    internal static readonly IBrush WrongArchiveType = new SolidColorBrush(
        Color.FromRgb(0x21, 0x96, 0xF3)
    );
    internal static readonly IBrush Untrimmed = new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC));
}
