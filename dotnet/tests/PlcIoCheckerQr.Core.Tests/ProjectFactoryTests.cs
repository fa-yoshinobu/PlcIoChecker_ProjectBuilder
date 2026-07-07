using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Core.Tests;

public sealed class ProjectFactoryTests
{
    [Theory]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", " d001 ", "D1")]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "x00f", "XF")]
    [InlineData("Melsec", "Normal", "MELSEC iQ-F (built-in)", "x010", "X10")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "r001", "R001")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "x03f", "X3F")]
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
            DevicesText: "d001,Int16",
            WatchText: "x00f,Bit",
            TrapsText: "stc000,Bit,Rise,,true",
            CommentsText: "sd000,Int16,System word"));

        Assert.Equal("D1", project.Devices.Single().Address);
        Assert.Equal("XF", project.TimeChart.Single().Address);
        Assert.Equal("STC0", project.Traps.Single().Address);
        Assert.Equal("SD0", project.Comments.Single().Address);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "XFF", true)]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "STC0", true)]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "SD0", false)]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "SWFF", false)]
    [InlineData("Melsec", "Normal", "MELSEC iQ-F (built-in)", "X77", true)]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "R015", true)]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "CM100", false)]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "DM100", false)]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "X39F", true)]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "M100", true)]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "D100", false)]
    public void BitDetectionIsVendorModeAndModelAware(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address,
        bool expectedIsBit)
    {
        Assert.Equal(expectedIsBit, ProjectFactory.IsBitAddress(address, vendor, keyenceDeviceMode, machineLabel));
    }

    [Fact]
    public void UnknownAddressFamilyKeepsDataTypeChoicesOpenUntilValidated()
    {
        Assert.False(ProjectFactory.IsBitAddress("DFFFF", "Melsec", "Normal", "MELSEC iQ-R (built-in)"));
        Assert.Equal(ProjectFactory.DeviceDataTypes, ProjectFactory.DeviceDataTypesForAddress("DFFFF", "Melsec"));
        Assert.Equal(ProjectFactory.DeviceDataTypes, ProjectFactory.DeviceDataTypesForAddress("", "Melsec"));
    }

    [Fact]
    public void TrapConditionListsFollowAddressKind()
    {
        Assert.Equal(ProjectFactory.TrapConditions, ProjectFactory.TrapConditionsForAddress("", "Melsec"));

        Assert.Equal(ProjectFactory.BitTrapConditions, ProjectFactory.TrapConditionsForAddress("X0", "Melsec"));

        Assert.Equal(ProjectFactory.WordTrapConditions, ProjectFactory.TrapConditionsForAddress("D0", "Melsec"));
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
            WatchText: "D100,UInt16\r\nd100,UInt16\r\nX0,Bit",
            TrapsText: "D100,UInt16,GreaterOrEqual,12.5,false"),
            nowEpochMs: 456);

        Assert.Equal("PLC QR Project", project.Name);
        Assert.Equal("plc-qr-project-456", project.Id);
        Assert.Equal(["D100", "X0"], project.TimeChart.Select(target => target.Address).ToArray());
        Assert.Equal("UInt16", project.Devices[0].DataType);
        Assert.Equal(12.5, project.Traps.Single().Threshold);
        Assert.False(project.Traps.Single().Enabled);
    }

}
