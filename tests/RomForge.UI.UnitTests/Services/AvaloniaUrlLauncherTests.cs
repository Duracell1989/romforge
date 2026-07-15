using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.Services;

namespace RomForge.UI.UnitTests.Services;

[TestOf(typeof(AvaloniaUrlLauncher))]
public sealed class AvaloniaUrlLauncherTests
{
    [TestCase("https://example.com/releases/tag/v2.0.0")]
    [TestCase("http://example.com")]
    public void IsLaunchableHttpUrl_HttpOrHttps_ReturnsTrue(string url)
    {
        AvaloniaUrlLauncher.IsLaunchableHttpUrl(url).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("not a url")]
    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/x")]
    [TestCase("javascript:alert(1)")]
    public void IsLaunchableHttpUrl_MalformedOrNonHttpScheme_ReturnsFalse(string url)
    {
        AvaloniaUrlLauncher.IsLaunchableHttpUrl(url).Should().BeFalse();
    }
}
