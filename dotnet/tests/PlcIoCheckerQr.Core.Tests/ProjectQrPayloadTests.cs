using System.Text;
using System.Text.Json;
using PlcIoCheckerQr.Core;

namespace PlcIoCheckerQr.Core.Tests;

public sealed class ProjectQrPayloadTests
{
    [Fact]
    public void EncodedChunksRoundTripToAndroidImportJsonShape()
    {
        var project = SampleProject();
        var chunks = ProjectQrPayload.EncodeProjectChunks(project, chunkSize: 220);

        Assert.All(chunks, chunk =>
        {
            Assert.StartsWith("PLCIOC3|ZSTD|", chunk.Text);
            Assert.Equal(chunk, ProjectQrPayload.ParseChunkText(chunk.Text));
        });
        Assert.True(chunks.Count >= 1);

        var decoded = ProjectQrPayload.DecodeChunks(chunks);
        Assert.Equal(ProjectQrPayload.ProjectQrJsonBytes(project), decoded);

        using var document = JsonDocument.Parse(decoded);
        var root = document.RootElement;
        Assert.Equal("plc-io-checker-project", root.GetProperty("schema").GetString());
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("unit-project-123", root.GetProperty("projectId").GetString());
        Assert.Equal("MELSEC", root.GetProperty("plc").GetProperty("vendor").GetString());
        Assert.Equal("GREATER_OR_EQUAL", root.GetProperty("traps")[0].GetProperty("condition").GetString());
        Assert.Equal("Start input", root.GetProperty("deviceList")[0].GetProperty("comment").GetString());
        Assert.False(root.GetProperty("deviceList")[0].TryGetProperty("watch", out _));
        Assert.False(root.TryGetProperty("settings", out _));
    }

    [Fact]
    public void ProjectFactoryUsesAndroidEnumNames()
    {
        var project = ProjectFactory.MakeProject(new ProjectInput(
            Name: "Keyence Sample",
            Vendor: "Keyence",
            ConnectionMode: "DemoMock",
            Host: "192.168.250.100",
            Port: 8501,
            MonitorIntervalMs: 500,
            TimeoutMs: 2000,
            MachineLabel: "KV-8000",
            KeyenceDeviceMode: "Normal",
            TransportMode: "Tcp",
            Network: 0,
            Station: 255,
            ModuleIo: 1023,
            Multidrop: 0,
            DevicesText: "R000\r\nDM100,UInt16",
            WatchText: "R000",
            TrapsText: "DM100,Change,,true"),
            nowEpochMs: 123);

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectQrJsonBytes(project));
        Assert.Contains("\"vendor\":\"KEYENCE\"", json);
        Assert.Contains("\"mode\":\"DEMO_MOCK\"", json);
        Assert.Contains("\"deviceMode\":\"NORMAL\"", json);
        Assert.Contains("\"timeChart\":[{\"address\":\"R000\",\"dataType\":\"BIT\"}]", json);
        Assert.DoesNotContain("\"melsec\"", json);
    }

    [Fact]
    public void LegacyDeflateQrFormatIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ProjectQrPayload.ParseChunkText("PLCIOC2D|session|1|1|0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef|payload"));
    }

    [Fact]
    public void ProjectFactoryCommonizesDeviceCommentsByAddress()
    {
        var project = ProjectFactory.MakeProject(TestInput(
            DevicesText: "D100,Int16,Speed word\r\nD100,UInt16,Ignored duplicate\r\nD101,Alarm word",
            WatchText: "",
            TrapsText: ""));

        Assert.Equal(["Speed word", "Speed word", "Alarm word"], project.Devices.Select(device => device.Comment).ToArray());

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        var deviceList = document.RootElement.GetProperty("deviceList");
        Assert.Equal("Speed word", deviceList[0].GetProperty("comment").GetString());
        Assert.Equal("Speed word", deviceList[1].GetProperty("comment").GetString());
        Assert.Equal("Alarm word", deviceList[2].GetProperty("comment").GetString());
    }

    [Fact]
    public void ProjectFactoryLimitsTimeChartTargetsToAndroidMaximum()
    {
        var input = TestInput(
            DevicesText: "",
            WatchText: string.Join("\n", Enumerable.Range(0, ProjectFactory.MaxTimeChartTargets + 1).Select(index => $"D{index}")),
            TrapsText: "");

        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(input));
        Assert.Contains($"最大 {ProjectFactory.MaxTimeChartTargets}", exception.Message);
    }

    [Fact]
    public void ProjectFactoryUsesVendorAwareTrapConditions()
    {
        var melsecProject = ProjectFactory.MakeProject(TestInput(
            Vendor: "Melsec",
            DevicesText: "R100",
            WatchText: "",
            TrapsText: "R100,GreaterOrEqual,1,true"));

        Assert.Equal("Int16", melsecProject.Devices.Single().DataType);
        Assert.Equal("GreaterOrEqual", melsecProject.Traps.Single().Condition);
        Assert.Equal(1, melsecProject.Traps.Single().Threshold);

        var keyenceProject = ProjectFactory.MakeProject(TestInput(
            Vendor: "Keyence",
            DevicesText: "R100",
            WatchText: "",
            TrapsText: "R100,Rise,,true"));

        Assert.Equal("Bit", keyenceProject.Devices.Single().DataType);
        Assert.Equal("Rise", keyenceProject.Traps.Single().Condition);
        Assert.Null(keyenceProject.Traps.Single().Threshold);

        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            Vendor: "Keyence",
            DevicesText: "R100",
            WatchText: "",
            TrapsText: "R100,GreaterOrEqual,1,true")));
    }

    [Fact]
    public void ProjectFactoryRequiresThresholdOnlyForNumericTrapConditions()
    {
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            DevicesText: "D100",
            WatchText: "",
            TrapsText: "D100,GreaterOrEqual,,true")));

        var project = ProjectFactory.MakeProject(TestInput(
            DevicesText: "D100",
            WatchText: "",
            TrapsText: "D100,Change,123,true"));

        Assert.Equal("Change", project.Traps.Single().Condition);
        Assert.Null(project.Traps.Single().Threshold);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "iQ-R", "XFF", "Bit")]
    [InlineData("Melsec", "Normal", "iQ-R", "SWFF", "Int16")]
    [InlineData("Melsec", "Normal", "iQ-F", "X77", "Bit")]
    [InlineData("Keyence", "Normal", "KV-8000", "R015", "Bit")]
    [InlineData("Keyence", "Normal", "KV-8000", "DM100", "Int16")]
    [InlineData("Keyence", "Xym", "KV-8000", "X39F", "Bit")]
    [InlineData("Keyence", "Xym", "KV-8000", "Y1999F", "Bit")]
    [InlineData("Keyence", "Xym", "KV-8000", "D100", "Int16")]
    public void ProjectFactoryAcceptsDeviceAddressStringsSupportedByMobileApps(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address,
        string expectedDataType)
    {
        var condition = expectedDataType == "Bit" ? "Rise" : "GreaterOrEqual";
        var threshold = expectedDataType == "Bit" ? "" : "1";
        var project = ProjectFactory.MakeProject(TestInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: machineLabel,
            DevicesText: address,
            WatchText: address,
            TrapsText: $"{address},{expectedDataType},{condition},{threshold},true"));

        Assert.Equal(address, project.Devices.Single().Address);
        Assert.Equal(expectedDataType, project.Devices.Single().DataType);
        Assert.Equal(address, project.TimeChart.Single().Address);
        Assert.Equal(expectedDataType, project.TimeChart.Single().DataType);
        Assert.Equal(address, project.Traps.Single().Address);
        Assert.Equal(expectedDataType, project.Traps.Single().DataType);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "DM100")]
    [InlineData("Melsec", "Normal", "TS0")]
    [InlineData("Melsec", "Normal", "CN0")]
    [InlineData("Melsec", "Normal", @"U3E0\G10")]
    [InlineData("Keyence", "Normal", "D100")]
    [InlineData("Keyence", "Normal", "X0")]
    [InlineData("Keyence", "Normal", "VB0")]
    [InlineData("Keyence", "Xym", "DM100")]
    [InlineData("Keyence", "Xym", "MR000")]
    [InlineData("Keyence", "Xym", "R100")]
    public void ProjectFactoryRejectsDeviceNamesUnsupportedByMobileApps(
        string vendor,
        string keyenceDeviceMode,
        string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            DevicesText: address,
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Unsupported device", exception.Message);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "iQ-R", "DFFFF")]
    [InlineData("Melsec", "Normal", "iQ-F", "X78")]
    [InlineData("Keyence", "Normal", "KV-8000", "R016")]
    [InlineData("Keyence", "Normal", "KV-8000", "CR7916")]
    [InlineData("Keyence", "Xym", "KV-8000", "X3A0")]
    [InlineData("Keyence", "Xym", "KV-8000", "Y19A0")]
    public void ProjectFactoryRejectsInvalidDeviceAddressNumberFormats(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: machineLabel,
            DevicesText: address,
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Unsupported device", exception.Message);
    }

    [Fact]
    public void ProjectFactoryValidatesDeviceAddressStringsInAllInputSections()
    {
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            DevicesText: "DFFFF",
            WatchText: "",
            TrapsText: "")));
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            DevicesText: "",
            WatchText: "DFFFF",
            TrapsText: "")));
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            DevicesText: "",
            WatchText: "",
            TrapsText: "DFFFF,Change,,true")));
    }

    [Fact]
    public void ProjectFactoryReportsDeviceNamesSupportedByMobileApps()
    {
        Assert.Equal(
            ["X", "Y", "M", "D", "L", "F", "B", "SB", "SM", "STC", "TC", "CC", "W", "SW", "R", "ZR", "SD"],
            ProjectFactory.SupportedDeviceNames("Melsec"));
        Assert.Equal(
            ["R", "B", "MR", "LR", "CR", "DM", "EM", "FM", "ZF", "W", "TM", "CM"],
            ProjectFactory.SupportedDeviceNames("Keyence", "Normal"));
        Assert.Equal(
            ["B", "CR", "ZF", "W", "TM", "CM", "X", "Y", "M", "L", "D", "E", "F"],
            ProjectFactory.SupportedDeviceNames("Keyence", "Xym"));
    }

    [Theory]
    [InlineData("Keyence", "Normal", "DM100", "Int16")]
    [InlineData("Keyence", "Xym", "D100", "Int16")]
    [InlineData("Keyence", "Xym", "X0", "Bit")]
    public void ProjectFactoryAllowsDeviceFamiliesSupportedBySelectedKeyenceMode(
        string vendor,
        string keyenceDeviceMode,
        string address,
        string expectedDataType)
    {
        var project = ProjectFactory.MakeProject(TestInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            DevicesText: address,
            WatchText: address,
            TrapsText: ""));

        Assert.Equal(expectedDataType, project.Devices.Single().DataType);
        Assert.Equal(address, project.TimeChart.Single().Address);
    }

    [Fact]
    public void ProjectFactoryBuildsSequentialDeviceBlocks()
    {
        Assert.Equal(
            ["D00", "D01", "D02", "D03", "D04", "D05", "D06", "D07", "D08", "D09"],
            ProjectFactory.BuildDeviceBlock("D00", 10, "Melsec"));
        Assert.Equal(
            ["X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8", "X9", "XA", "XB", "XC", "XD", "XE", "XF"],
            ProjectFactory.BuildDeviceBlock("X0", 16, "Melsec"));
        Assert.Equal(
            ["X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X10"],
            ProjectFactory.BuildDeviceBlock("X0", 9, "Melsec", "Normal", "iQ-F"));
        Assert.Equal(
            ["R015", "R100"],
            ProjectFactory.BuildDeviceBlock("R015", 2, "Keyence", "Normal"));
        Assert.Equal(
            ["X00", "X01", "X02", "X03", "X04", "X05", "X06", "X07", "X08", "X09", "X0A", "X0B", "X0C", "X0D", "X0E", "X0F", "X10"],
            ProjectFactory.BuildDeviceBlock("X0", 17, "Keyence", "Xym"));
    }

    [Theory]
    [InlineData("D100,Bit")]
    [InlineData("X000,Int16")]
    public void ProjectFactoryRejectsDataTypesThatDoNotMatchDeviceKind(string devicesText)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(TestInput(
            DevicesText: devicesText,
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Invalid device data type", exception.Message);
    }

    [Fact]
    public void ProjectFactoryReportsAvailableDataTypesByDeviceKind()
    {
        Assert.Equal(["Bit"], ProjectFactory.DeviceDataTypesForAddress("X0", "Melsec"));
        Assert.DoesNotContain("Bit", ProjectFactory.DeviceDataTypesForAddress("D0", "Melsec"));
    }

    private static PlcProject SampleProject() => ProjectFactory.MakeProject(new ProjectInput(
        Name: "Unit Project",
        Vendor: "Melsec",
        ConnectionMode: "Real",
        Host: "192.168.250.100",
        Port: 1025,
        MonitorIntervalMs: 500,
        TimeoutMs: 2000,
        MachineLabel: "iQ-R",
        KeyenceDeviceMode: "Normal",
        TransportMode: "Tcp",
        Network: 0,
        Station: 255,
        ModuleIo: 1023,
        Multidrop: 0,
        DevicesText: "X000,Bit,Start input\r\nD100,Int16,Speed word",
        WatchText: "X000\r\nD100",
        TrapsText: "D100,GreaterOrEqual,100,true"),
        nowEpochMs: 123);

    private static ProjectInput TestInput(
        string Vendor = "Melsec",
        string KeyenceDeviceMode = "Normal",
        string? MachineLabel = null,
        string DevicesText = "D100",
        string WatchText = "",
        string TrapsText = "") => new(
        Name: "Unit Project",
        Vendor: Vendor,
        ConnectionMode: "Real",
        Host: "192.168.250.100",
        Port: Vendor == "Keyence" ? 8501 : 1025,
        MonitorIntervalMs: 500,
        TimeoutMs: 2000,
        MachineLabel: MachineLabel ?? (Vendor == "Keyence" ? "KV-8000" : "iQ-R"),
        KeyenceDeviceMode: KeyenceDeviceMode,
        TransportMode: "Tcp",
        Network: 0,
        Station: 255,
        ModuleIo: 1023,
        Multidrop: 0,
        DevicesText: DevicesText,
        WatchText: WatchText,
        TrapsText: TrapsText);
}
