using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Core.Tests;

public sealed class ProjectFactoryTests
{
    [Theory]
    [InlineData("Melsec", "Normal", "iQ-R", " d001 ", "D1")]
    [InlineData("Melsec", "Normal", "iQ-R", "x00f", "XF")]
    [InlineData("Melsec", "Normal", "iQ-F", "x010", "X10")]
    [InlineData("Keyence", "Normal", "KV-8000", "r001", "R001")]
    [InlineData("Keyence", "Xym", "KV-8000", "x03f", "X3F")]
    public void NormalizeDeviceAddressFormatsAddressLikeMobileApps(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string input,
        string expected)
    {
        Assert.Equal(expected, ProjectFactory.NormalizeDeviceAddress(input, vendor, keyenceDeviceMode, machineLabel));
    }

    [Fact]
    public void MakeProjectNormalizesAddressesInAllInputSections()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "d001",
            WatchText: "x00f",
            TrapsText: "stc000,Rise,,true",
            CommentsText: "sd000,Int16,System word"));

        Assert.Equal("D1", project.Devices.Single().Address);
        Assert.Equal("XF", project.TimeChart.Single().Address);
        Assert.Equal("STC0", project.Traps.Single().Address);
        Assert.Equal("SD0", project.Comments.Single().Address);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "iQ-R", "XFF", true, "Bit")]
    [InlineData("Melsec", "Normal", "iQ-R", "STC0", true, "Bit")]
    [InlineData("Melsec", "Normal", "iQ-R", "SD0", false, "Int16")]
    [InlineData("Melsec", "Normal", "iQ-R", "SWFF", false, "Int16")]
    [InlineData("Melsec", "Normal", "iQ-F", "X77", true, "Bit")]
    [InlineData("Keyence", "Normal", "KV-8000", "R015", true, "Bit")]
    [InlineData("Keyence", "Normal", "KV-8000", "CM100", false, "Int16")]
    [InlineData("Keyence", "Normal", "KV-8000", "DM100", false, "Int16")]
    [InlineData("Keyence", "Xym", "KV-8000", "X39F", true, "Bit")]
    [InlineData("Keyence", "Xym", "KV-8000", "M100", true, "Bit")]
    [InlineData("Keyence", "Xym", "KV-8000", "D100", false, "Int16")]
    public void GuessDataTypeAndBitDetectionAreVendorModeAndModelAware(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address,
        bool expectedIsBit,
        string expectedDataType)
    {
        Assert.Equal(expectedIsBit, ProjectFactory.IsBitAddress(address, vendor, keyenceDeviceMode, machineLabel));
        Assert.Equal(expectedDataType, ProjectFactory.GuessDataType(address, vendor, keyenceDeviceMode, machineLabel));
    }

    [Fact]
    public void UnknownAddressFamilyFallsBackToWordLikeDataTypeUntilValidated()
    {
        Assert.False(ProjectFactory.IsBitAddress("DFFFF", "Melsec", "Normal", "iQ-R"));
        Assert.Equal("Int16", ProjectFactory.GuessDataType("DFFFF", "Melsec", "Normal", "iQ-R"));
        Assert.Equal(ProjectFactory.DeviceDataTypes, ProjectFactory.DeviceDataTypesForAddress("DFFFF", "Melsec"));
        Assert.Equal(ProjectFactory.DeviceDataTypes, ProjectFactory.DeviceDataTypesForAddress("", "Melsec"));
    }

    [Fact]
    public void TrapConditionListsAndDefaultsFollowAddressKind()
    {
        Assert.Equal(ProjectFactory.TrapConditions, ProjectFactory.TrapConditionsForAddress("", "Melsec"));

        Assert.Equal(ProjectFactory.BitTrapConditions, ProjectFactory.TrapConditionsForAddress("X0", "Melsec"));
        Assert.Equal("Rise", ProjectFactory.DefaultTrapConditionForAddress("X0", "Melsec"));
        Assert.Equal("Rise", ProjectFactory.CoerceTrapConditionForAddress("X0", "GreaterOrEqual", "Melsec"));

        Assert.Equal(ProjectFactory.WordTrapConditions, ProjectFactory.TrapConditionsForAddress("D0", "Melsec"));
        Assert.Equal("GreaterOrEqual", ProjectFactory.DefaultTrapConditionForAddress("D0", "Melsec"));
        Assert.Equal("GreaterOrEqual", ProjectFactory.CoerceTrapConditionForAddress("D0", "Rise", "Melsec"));
    }

    [Fact]
    public void TrapConditionValidationRejectsUnsupportedConditionForAddressKind()
    {
        Assert.Equal("Fall", ProjectFactory.ValidateTrapConditionForAddress("X0", "Fall", "Melsec"));
        Assert.Equal("LessOrEqual", ProjectFactory.ValidateTrapConditionForAddress("D0", "LessOrEqual", "Melsec"));

        var bitException = Assert.Throws<ArgumentException>(() =>
            ProjectFactory.ValidateTrapConditionForAddress("X0", "GreaterOrEqual", "Melsec"));
        Assert.Contains("Invalid trap condition for X0", bitException.Message);

        var wordException = Assert.Throws<ArgumentException>(() =>
            ProjectFactory.ValidateTrapConditionForAddress("D0", "Rise", "Melsec"));
        Assert.Contains("Invalid trap condition for D0", wordException.Message);
    }

    [Theory]
    [InlineData("Rise", false)]
    [InlineData("Fall", false)]
    [InlineData("Change", false)]
    [InlineData("GreaterOrEqual", true)]
    [InlineData("LessOrEqual", true)]
    [InlineData("Equal", true)]
    [InlineData("NotEqual", true)]
    public void TrapConditionThresholdRequirementIsConditionSpecific(string condition, bool requiresThreshold)
    {
        Assert.Equal(requiresThreshold, ProjectFactory.TrapConditionRequiresThreshold(condition));
    }

    [Fact]
    public void SlugifyLowercasesAndKeepsFallbackForEmptySlug()
    {
        Assert.Equal("plc-io-checker-2026", ProjectFactory.Slugify(" PLC IO Checker!! 2026 "));
        Assert.Equal("plc-project", ProjectFactory.Slugify("###"));
        Assert.Equal("custom", ProjectFactory.Slugify("   ", fallback: "custom"));
    }

    [Fact]
    public void MakeProjectNormalizesNameIdAndDistinctWatchAddresses()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Name: "  ",
            DevicesText: "d100,uint16,Speed\r\nx0,Bit,Start",
            WatchText: "D100\r\nd100\r\nX0",
            TrapsText: "D100,GreaterOrEqual,12.5,false"),
            nowEpochMs: 456);

        Assert.Equal("PLC QR Project", project.Name);
        Assert.Equal("plc-qr-project-456", project.Id);
        Assert.Equal(["D100", "X0"], project.TimeChart.Select(target => target.Address).ToArray());
        Assert.Equal("UInt16", project.Devices[0].DataType);
        Assert.Equal(12.5, project.Traps.Single().Threshold);
        Assert.False(project.Traps.Single().Enabled);
    }

}
