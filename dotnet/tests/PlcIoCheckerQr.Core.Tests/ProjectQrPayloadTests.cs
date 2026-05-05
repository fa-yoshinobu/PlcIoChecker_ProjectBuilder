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

        Assert.All(chunks, chunk => Assert.StartsWith("PLCIOC2D|", chunk.Text));
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
        Assert.False(root.GetProperty("deviceList")[0].TryGetProperty("comment", out _));
        Assert.False(root.GetProperty("deviceList")[0].TryGetProperty("watch", out _));
        Assert.False(root.TryGetProperty("settings", out _));
        Assert.False(root.TryGetProperty("watchItems", out _));
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
    [InlineData("Melsec", "Normal", "DM100")]
    [InlineData("Keyence", "Normal", "D100")]
    [InlineData("Keyence", "Normal", "X0")]
    [InlineData("Keyence", "Normal", "R016")]
    [InlineData("Keyence", "Xym", "DM100")]
    [InlineData("Keyence", "Xym", "R100")]
    public void ProjectFactoryRejectsDeviceFamiliesUnsupportedBySelectedVendorAndMode(
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
        DevicesText: "X000,Bit\r\nD100,Int16",
        WatchText: "X000\r\nD100",
        TrapsText: "D100,GreaterOrEqual,100,true"),
        nowEpochMs: 123);

    private static ProjectInput TestInput(
        string Vendor = "Melsec",
        string KeyenceDeviceMode = "Normal",
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
        MachineLabel: Vendor == "Keyence" ? "KV-8000" : "iQ-R",
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
