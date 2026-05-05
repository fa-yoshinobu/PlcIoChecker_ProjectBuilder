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
            TrapsText: "DM100,Change,,true",
            BlockDisplayDensity: "Detailed"),
            nowEpochMs: 123);

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectQrJsonBytes(project));
        Assert.Contains("\"vendor\":\"KEYENCE\"", json);
        Assert.Contains("\"mode\":\"DEMO_MOCK\"", json);
        Assert.Contains("\"deviceMode\":\"NORMAL\"", json);
        Assert.Contains("\"timeChart\":[{\"address\":\"R000\",\"dataType\":\"BIT\"}]", json);
        Assert.DoesNotContain("blockDisplayDensity", json);
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
        KeyenceDeviceMode: "Normal",
        TransportMode: "Tcp",
        Network: 0,
        Station: 255,
        ModuleIo: 1023,
        Multidrop: 0,
        DevicesText: DevicesText,
        WatchText: WatchText,
        TrapsText: TrapsText);
}
