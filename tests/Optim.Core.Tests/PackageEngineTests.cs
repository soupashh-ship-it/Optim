using Optim.Core.Packages;
using Xunit;

namespace Optim.Core.Tests;

public class PackageEngineTests
{
    [Fact]
    public void Parse_skips_headers_and_footers()
    {
        var output =
            "Name              Id                    Version   Available Source\n" +
            "------------------------------------------------------------------\n" +
            "Firefox           Mozilla.Firefox       130.0     131.0     winget\n" +
            "The following packages have upgrades available.\n";
        var result = PackageEngine.ParseUpgradeTable(output);
        var single = Assert.Single(result);
        Assert.Equal("Mozilla.Firefox", single.Id);
        Assert.Equal("Firefox", single.Name);
        Assert.Equal("130.0", single.InstalledVersion);
        Assert.Equal("131.0", single.AvailableVersion);
    }

    [Fact]
    public void Parse_handles_names_with_spaces()
    {
        var output = "Microsoft Visual Studio Code  Microsoft.VisualStudio.Code  1.90.0  1.91.0  winget\n";
        var result = PackageEngine.ParseUpgradeTable(output);
        var single = Assert.Single(result);
        Assert.Equal("Microsoft.VisualStudio.Code", single.Id);
        Assert.Equal("Microsoft Visual Studio Code", single.Name);
    }

    [Fact]
    public void Parse_empty_and_no_upgrade_reports_empty()
    {
        Assert.Empty(PackageEngine.ParseUpgradeTable(""));
        Assert.Empty(PackageEngine.ParseUpgradeTable("No upgrades available.\nNo applicable update found.\n"));
    }

    [Fact]
    public void Parse_survives_missing_source_column()
    {
        // Some winget builds/locale combos print without the trailing Source.
        const string output = "Firefox           Mozilla.Firefox       130.0     131.0\n";
        var single = Assert.Single(PackageEngine.ParseUpgradeTable(output));
        Assert.Equal("Mozilla.Firefox", single.Id);
        Assert.Equal("131.0", single.AvailableVersion);
    }

    [Fact]
    public void Parse_survives_unknown_versions()
    {
        // --include-unknown prints Unknown for versions winget cannot detect.
        const string output = "Some App          Vendor.SomeApp        Unknown   2.0.0     winget\n";
        var single = Assert.Single(PackageEngine.ParseUpgradeTable(output));
        Assert.Equal("Vendor.SomeApp", single.Id);
        Assert.Equal("Unknown", single.InstalledVersion);
    }

    [Fact]
    public void Parse_does_not_mistake_version_numbers_for_ids()
    {
        const string output = "Firefox           Mozilla.Firefox       130.0     131.0     winget\n";
        var single = Assert.Single(PackageEngine.ParseUpgradeTable(output));
        Assert.Equal("Mozilla.Firefox", single.Id);
    }

    [Fact]
    public void Search_parse_handles_match_values_with_spaces()
    {
        var output =
            "Name              Id                         Version  Match       Source\n" +
            "------------------------------------------------------------------------\n" +
            "Firefox           Mozilla.Firefox            131.0    Tag: firefox  winget\n" +
            "VLC media player  VideoLAN.VLC               3.0.20   Tag: vlc      winget\n";
        var result = PackageEngine.ParseSearchTable(output);
        Assert.Equal(2, result.Count);
        Assert.Equal("Mozilla.Firefox", result[0].Id);
        Assert.Equal("Firefox", result[0].Name);
        Assert.Equal("131.0", result[0].Version);
        Assert.Equal("VLC media player", result[1].Name);
    }

    [Fact]
    public void Search_parse_empty_and_no_results_reports_empty()
    {
        Assert.Empty(PackageEngine.ParseSearchTable(""));
        Assert.Empty(PackageEngine.ParseSearchTable("No package found matching input criteria.\n"));
    }
}
