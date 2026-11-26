using BlogApp.Services;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// BlogApp's isolated test profile switch (<c>--profile</c> followed by a folder), added 2026-11-21
/// so the BlogApp checks of REQ-UI-066 to REQ-UI-071 never read or overwrite the owner's saved
/// live connection.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> pins which command lines turn isolation on. A false "on" would be harmless;
/// a false "off" would let an automated run load the owner's saved connection, so the parsing is
/// tested both ways.</para>
/// <para><b>Dependencies:</b> xUnit; <c>DesktopProfile.cs</c> is linked into this assembly.</para>
/// <para><b>Usage:</b> pure unit tests.</para>
/// </remarks>
public class DesktopProfileTests
{
    private const string Folder = @"C:\repo\tests\.artifacts\blogapp-profile";

    /// <summary>
    /// The two-argument spelling names the folder.
    /// </summary>
    [Fact]
    public void ProfileSwitchNamesTheFolder()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(Folder, DesktopProfile.ReadProfile(new[] { "BlogApp.exe", "--profile", Folder }));
    }

    /// <summary>
    /// The equals spelling names the folder too, with quotes tolerated.
    /// </summary>
    [Fact]
    public void ProfileEqualsSpellingNamesTheFolder()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(Folder, DesktopProfile.ReadProfile(new[] { "BlogApp.exe", "--profile=\"" + Folder + "\"" }));
    }

    /// <summary>
    /// No switch, a switch without a folder, or a relative folder leaves isolation off.
    /// </summary>
    /// <param name="args">The command line.</param>
    [Theory]
    [InlineData("BlogApp.exe")]
    [InlineData("BlogApp.exe", "--profile")]
    [InlineData("BlogApp.exe", "--profile", "relative\\folder")]
    [InlineData("BlogApp.exe", "--other", "C:\\x")]
    public void MissingOrRelativeProfileIsIgnored(params string[] args)
    {
        Assert.Null(DesktopProfile.ReadProfile(args));
    }
}
