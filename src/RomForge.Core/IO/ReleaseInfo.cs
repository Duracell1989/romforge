namespace RomForge.Core.IO;

/// <summary>
/// The latest release as reported by the release host: its version tag and the page to open.
/// </summary>
public sealed record ReleaseInfo(string TagName, string HtmlUrl);
