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
            Assert.StartsWith("PLCIOC1|ZSTD|", chunk.Text);
            Assert.Equal(chunk, ProjectQrPayload.ParseChunkText(chunk.Text));
        });
        Assert.True(chunks.Count >= 1);

        var decoded = ProjectQrPayload.DecodeChunks(chunks);
        Assert.Equal(ProjectQrPayload.ProjectQrJsonBytes(project), decoded);

        using var document = JsonDocument.Parse(decoded);
        var root = document.RootElement;
        Assert.Equal("plc-io-checker-project", root.GetProperty("schema").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var exportInfo = root.GetProperty("exportInfo");
        Assert.Equal("PROJECT_BUILDER", exportInfo.GetProperty("source").GetString());
        Assert.Equal("1.0.0", exportInfo.GetProperty("version").GetString());
        Assert.Equal("unit-project-123", root.GetProperty("projectId").GetString());
        Assert.Equal("MELSEC", root.GetProperty("plc").GetProperty("vendor").GetString());
        Assert.Equal("melsec:iq-r", root.GetProperty("plc").GetProperty("cpuModel").GetString());
        Assert.False(root.GetProperty("plc").TryGetProperty("plcProfile", out _));
        var melsec = root.GetProperty("plc").GetProperty("melsec");
        Assert.Equal(0, melsec.GetProperty("networkNo").GetInt32());
        Assert.Equal(255, melsec.GetProperty("stationNo").GetInt32());
        Assert.Equal("OwnStation", melsec.GetProperty("moduleIo").GetString());
        Assert.False(melsec.TryGetProperty("multidropNo", out _));
        Assert.False(melsec.TryGetProperty("remotePassword", out _));
        Assert.Equal("GREATER_OR_EQUAL", root.GetProperty("traps")[0].GetProperty("condition").GetString());
        Assert.False(root.GetProperty("deviceList")[0].TryGetProperty("dataType", out _));
        Assert.False(root.GetProperty("deviceList")[0].TryGetProperty("comment", out _));
        Assert.Equal("Start input", root.GetProperty("deviceMeta")[0].GetProperty("comment").GetString());
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
            MachineLabel: "KEYENCE KV-8000",
            KeyenceDeviceMode: "Normal",
            TransportMode: "Tcp",
            Network: 0,
            Station: 255,
            ModuleIo: "OwnStation",
            DevicesText: "R000,Bit\r\nDM100,UInt16",
            WatchText: "R000,Bit",
            TrapsText: "DM100,UInt16,Change,,true"),
            nowEpochMs: 123);

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectQrJsonBytes(project));
        Assert.Contains("\"vendor\":\"KEYENCE\"", json);
        Assert.Contains("\"cpuModel\":\"keyence:kv-8000\"", json);
        Assert.DoesNotContain("\"plcProfile\"", json);
        Assert.Contains("\"mode\":\"DEMO_MOCK\"", json);
        Assert.DoesNotContain("\"deviceMode\"", json);
        Assert.Contains("\"timeChart\":[{\"address\":\"R000\"}]", json);
        Assert.Contains("\"deviceMeta\":[{\"address\":\"R000\",\"dataType\":\"BIT\"}", json);
        Assert.DoesNotContain("\"melsec\"", json);
        Assert.DoesNotContain("\"remotePassword\"", json);
    }

    [Fact]
    public void ProjectFactoryRejectsOverMaxComment()
    {
        var comment = new string('あ', ProjectCommentRules.MaxCommentCharacters + 1);

        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Name: "Comment Limit",
            Vendor: "Melsec",
            DevicesText: $"D100,Int16,{comment}")));
    }

    [Fact]
    public void ProjectFactoryRequiresExplicitDataTypesForProjectJsonGeneration()
    {
        Assert.Contains("device data type is required", Assert.Throws<ArgumentException>(() =>
            ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(DevicesText: "D100"))).Message);
        Assert.Contains("time chart data type is required", Assert.Throws<ArgumentException>(() =>
            ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(DevicesText: "", WatchText: "D100"))).Message);
        Assert.Contains("trap data type", Assert.Throws<ArgumentException>(() =>
            ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(DevicesText: "", TrapsText: "D100,GreaterOrEqual,1,true"))).Message);
        Assert.Contains("comment data type", Assert.Throws<ArgumentException>(() =>
            ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(DevicesText: "", CommentsText: "D100,Comment"))).Message);
    }

    [Fact]
    public void ProjectFactoryMapsDisplayModelLabelsToCanonicalJsonLabels()
    {
        Assert.Equal(
            [
                "melsec:iq-r",
                "melsec:iq-r:rj71en71",
                "melsec:iq-f",
                "melsec:iq-l",
                "melsec:mx-r",
                "melsec:mx-r:rj71en71",
                "melsec:mx-f",
                "melsec:qnudv",
                "melsec:qnudv:qj71e71-100",
                "melsec:qnu",
                "melsec:qnu:qj71e71-100",
                "melsec:qcpu:qj71e71-100",
                "melsec:lcpu",
                "melsec:lcpu:lj71e71-100",
            ],
            ProjectFactory.MelsecCpuModels
                .Select(label => ProjectFactory.ToCanonicalMachineLabel("Melsec", label))
                .ToArray());

        Assert.Equal(
            [
                "keyence:kv-nano",
                "keyence:kv-nano-xym",
                "keyence:kv-3000",
                "keyence:kv-3000-xym",
                "keyence:kv-5000",
                "keyence:kv-5000-xym",
                "keyence:kv-7000",
                "keyence:kv-7000-xym",
                "keyence:kv-8000",
                "keyence:kv-8000-xym",
                "keyence:kv-x500",
                "keyence:kv-x500-xym",
            ],
            ProjectFactory.KeyenceCpuModels
                .Select(label => ProjectFactory.ToCanonicalMachineLabel("Keyence", label))
                .ToArray());

        Assert.Throws<ArgumentException>(() => ProjectFactory.ToCanonicalMachineLabel("Melsec", "QCPU"));
    }

    [Fact]
    public void ProjectFactoryMapsCanonicalJsonLabelsToDisplayModelLabels()
    {
        Assert.Equal(
            ProjectFactory.MelsecCpuModels,
            new[]
            {
                "melsec:iq-r",
                "melsec:iq-r:rj71en71",
                "melsec:iq-f",
                "melsec:iq-l",
                "melsec:mx-r",
                "melsec:mx-r:rj71en71",
                "melsec:mx-f",
                "melsec:qnudv",
                "melsec:qnudv:qj71e71-100",
                "melsec:qnu",
                "melsec:qnu:qj71e71-100",
                "melsec:qcpu:qj71e71-100",
                "melsec:lcpu",
                "melsec:lcpu:lj71e71-100",
            }.Select(label => ProjectFactory.ToDisplayMachineLabel("Melsec", label)).ToArray());

        Assert.Equal(
            ProjectFactory.KeyenceCpuModels,
            new[]
            {
                "keyence:kv-nano",
                "keyence:kv-nano-xym",
                "keyence:kv-3000",
                "keyence:kv-3000-xym",
                "keyence:kv-5000",
                "keyence:kv-5000-xym",
                "keyence:kv-7000",
                "keyence:kv-7000-xym",
                "keyence:kv-8000",
                "keyence:kv-8000-xym",
                "keyence:kv-x500",
                "keyence:kv-x500-xym",
            }.Select(label => ProjectFactory.ToDisplayMachineLabel("Keyence", label)).ToArray());

        Assert.Throws<ArgumentException>(() => ProjectFactory.ToDisplayMachineLabel("Melsec", "melsec:qcpu"));
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
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,Int16,Speed word\r\nD100,Int16,Ignored duplicate\r\nD101,Int16,Alarm word",
            WatchText: "",
            TrapsText: ""));

        Assert.Equal(["Speed word", "Speed word", "Alarm word"], project.Devices.Select(device => device.Comment).ToArray());

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        var deviceList = document.RootElement.GetProperty("deviceList");
        Assert.False(deviceList[0].TryGetProperty("comment", out _));
        Assert.False(deviceList[0].TryGetProperty("dataType", out _));
        var deviceMeta = document.RootElement.GetProperty("deviceMeta");
        Assert.Equal("D100", deviceMeta[0].GetProperty("address").GetString());
        Assert.Equal("INT16", deviceMeta[0].GetProperty("dataType").GetString());
        Assert.Equal("Speed word", deviceMeta[0].GetProperty("comment").GetString());
        Assert.Equal("D101", deviceMeta[1].GetProperty("address").GetString());
        Assert.Equal("Alarm word", deviceMeta[1].GetProperty("comment").GetString());
    }

    [Fact]
    public void ProjectFactoryStoresCommentTabRowsInDeviceMeta()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: "D100,Int16",
            TrapsText: "D101,Int16,Change,,true",
            CommentsText: "D100,Int16,Watch comment\r\nD101,Int16,Trap comment"));

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        var deviceMeta = document.RootElement.GetProperty("deviceMeta");
        Assert.Equal("D100", deviceMeta[0].GetProperty("address").GetString());
        Assert.Equal("Watch comment", deviceMeta[0].GetProperty("comment").GetString());
        Assert.Equal("D101", deviceMeta[1].GetProperty("address").GetString());
        Assert.Equal("Trap comment", deviceMeta[1].GetProperty("comment").GetString());
    }

    [Fact]
    public void ProjectFactoryPrefersCommentTabRowsOverLegacyListComments()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,Int16,Legacy comment",
            WatchText: "",
            TrapsText: "",
            CommentsText: "D100,Int16,Comment tab value"));

        Assert.Equal("Comment tab value", project.Devices.Single().Comment);

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Comment tab value", document.RootElement
            .GetProperty("deviceMeta")[0]
            .GetProperty("comment")
            .GetString());
    }

    [Fact]
    public void ProjectFactoryEmitsCommentOnlyRowsAsDeviceMeta()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: "",
            TrapsText: "",
            CommentsText: "D100,Int16,Comment only"));

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        var deviceMeta = document.RootElement.GetProperty("deviceMeta");
        Assert.Single(deviceMeta.EnumerateArray());
        Assert.Equal("D100", deviceMeta[0].GetProperty("address").GetString());
        Assert.Equal("INT16", deviceMeta[0].GetProperty("dataType").GetString());
        Assert.Equal("Comment only", deviceMeta[0].GetProperty("comment").GetString());
    }

    [Fact]
    public void ProjectFactoryFillsMissingDataTypesFromSameAddressOnly()
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,UInt16,Speed word",
            WatchText: "D100",
            TrapsText: "D100,GreaterOrEqual,1,true",
            CommentsText: "D100,Comment from known type"));

        Assert.Equal("UInt16", project.Devices.Single().DataType);
        Assert.Equal("UInt16", project.TimeChart.Single().DataType);
        Assert.Equal("UInt16", project.Traps.Single().DataType);
        Assert.Equal("UInt16", project.Comments.Single().DataType);

        var json = Encoding.UTF8.GetString(ProjectQrPayload.ProjectJsonBytes(project));
        using var document = JsonDocument.Parse(json);
        var deviceMeta = document.RootElement.GetProperty("deviceMeta");
        Assert.Single(deviceMeta.EnumerateArray());
        Assert.Equal("D100", deviceMeta[0].GetProperty("address").GetString());
        Assert.Equal("UINT16", deviceMeta[0].GetProperty("dataType").GetString());
    }

    [Fact]
    public void ProjectFactoryRejectsConflictingExplicitDataTypesForSameAddress()
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,UInt16,Speed word",
            WatchText: "D100,Float32",
            TrapsText: "")));

        Assert.Contains("Conflicting data type for D100", exception.Message);
    }

    [Fact]
    public void ProjectFactoryLimitsTimeChartTargetsToAndroidMaximum()
    {
        var input = ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: string.Join("\n", Enumerable.Range(0, ProjectFactory.MaxTimeChartTargets + 1).Select(index => $"D{index},Int16")),
            TrapsText: "");

        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(input));
        Assert.Contains($"up to {ProjectFactory.MaxTimeChartTargets}", exception.Message);
    }

    [Fact]
    public void ProjectFactoryLimitsTrapDefinitionsToMobileMaximum()
    {
        var input = ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: "",
            TrapsText: string.Join("\n", Enumerable.Range(0, ProjectFactory.MaxTrapDefinitions + 1).Select(index => $"D{index},Int16,Change,,true")));

        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(input));
        Assert.Contains($"up to {ProjectFactory.MaxTrapDefinitions}", exception.Message);
    }

    [Fact]
    public void ProjectFactoryUsesVendorAwareTrapConditions()
    {
        var melsecProject = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: "Melsec",
            DevicesText: "R100,Int16",
            WatchText: "",
            TrapsText: "R100,Int16,GreaterOrEqual,1,true"));

        Assert.Equal("Int16", melsecProject.Devices.Single().DataType);
        Assert.Equal("GreaterOrEqual", melsecProject.Traps.Single().Condition);
        Assert.Equal(1, melsecProject.Traps.Single().Threshold);

        var keyenceProject = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: "Keyence",
            DevicesText: "R100,Bit",
            WatchText: "",
            TrapsText: "R100,Bit,Rise,,true"));

        Assert.Equal("Bit", keyenceProject.Devices.Single().DataType);
        Assert.Equal("Rise", keyenceProject.Traps.Single().Condition);
        Assert.Null(keyenceProject.Traps.Single().Threshold);

        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: "Keyence",
            DevicesText: "R100,Bit",
            WatchText: "",
            TrapsText: "R100,Bit,GreaterOrEqual,1,true")));
    }

    [Fact]
    public void ProjectFactoryRequiresThresholdOnlyForNumericTrapConditions()
    {
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,Int16",
            WatchText: "",
            TrapsText: "D100,Int16,GreaterOrEqual,,true")));

        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "D100,Int16",
            WatchText: "",
            TrapsText: "D100,Int16,Change,123,true"));

        Assert.Equal("Change", project.Traps.Single().Condition);
        Assert.Null(project.Traps.Single().Threshold);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "XFF", "Bit")]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "SWFF", "Int16")]
    [InlineData("Melsec", "Normal", "MELSEC iQ-F (built-in)", "X77", "Bit")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "R015", "Bit")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "DM100", "Int16")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "X39F", "Bit")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "Y1999F", "Bit")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "D100", "Int16")]
    public void ProjectFactoryAcceptsDeviceAddressStringsSupportedByMobileApps(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address,
        string expectedDataType)
    {
        var condition = expectedDataType == "Bit" ? "Rise" : "GreaterOrEqual";
        var threshold = expectedDataType == "Bit" ? "" : "1";
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: machineLabel,
            DevicesText: $"{address},{expectedDataType}",
            WatchText: $"{address},{expectedDataType}",
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
    [InlineData("Melsec", "Normal", "V0")]
    [InlineData("Melsec", "Normal", "DX0")]
    [InlineData("Melsec", "Normal", "TN0")]
    [InlineData("Melsec", "Normal", @"U3E0\G10")]
    [InlineData("Keyence", "Normal", "D100")]
    [InlineData("Keyence", "Normal", "X0")]
    [InlineData("Keyence", "Normal", "VB0")]
    [InlineData("Keyence", "Normal", "Z0")]
    [InlineData("Keyence", "Xym", "DM100")]
    [InlineData("Keyence", "Xym", "MR000")]
    [InlineData("Keyence", "Xym", "R100")]
    [InlineData("Keyence", "Xym", "VB0")]
    public void ProjectFactoryRejectsDeviceNamesUnsupportedByMobileApps(
        string vendor,
        string keyenceDeviceMode,
        string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: vendor == "Keyence" && keyenceDeviceMode == "Xym" ? "KEYENCE KV-8000 (XYM)" : null,
            DevicesText: $"{address},Int16",
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Unsupported device", exception.Message);
    }

    [Theory]
    [InlineData("Melsec", "Normal", "MELSEC iQ-R (built-in)", "DFFFF")]
    [InlineData("Melsec", "Normal", "MELSEC iQ-F (built-in)", "X78")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "R016")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "CR7916")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "DM65535")]
    [InlineData("Keyence", "Normal", "KEYENCE KV-8000", "B8000")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "X3A0")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "Y19A0")]
    [InlineData("Keyence", "Xym", "KEYENCE KV-8000 (XYM)", "X20000")]
    public void ProjectFactoryRejectsInvalidDeviceAddressNumberFormats(
        string vendor,
        string keyenceDeviceMode,
        string machineLabel,
        string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: machineLabel,
            DevicesText: $"{address},Int16",
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Unsupported device", exception.Message);
    }

    [Fact]
    public void ProjectFactoryValidatesDeviceAddressStringsInAllInputSections()
    {
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "DFFFF,Int16",
            WatchText: "",
            TrapsText: "")));
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: "DFFFF,Int16",
            TrapsText: "")));
        Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: "",
            WatchText: "",
            TrapsText: "DFFFF,Int16,Change,,true")));
    }

    [Fact]
    public void ProjectFactoryReportsDeviceNamesSupportedByMobileApps()
    {
        Assert.Equal(
            ["X", "Y", "M", "D", "L", "F", "B", "SB", "SM", "STC", "TC", "CC", "W", "SW", "R", "ZR", "SD"],
            ProjectFactory.SupportedDeviceNames("Melsec"));
        Assert.Equal(
            ["X", "Y", "M", "D", "L", "F", "B", "SB", "SM", "STC", "TC", "CC", "W", "SW", "R", "ZR", "SD"],
            ProjectFactory.SupportedDeviceNames("Melsec", "Normal", "MELSEC iQ-F (built-in)"));
        Assert.Equal(
            ["R", "B", "MR", "LR", "CR", "DM", "EM", "FM", "ZF", "W", "TM", "CM"],
            ProjectFactory.SupportedDeviceNames("Keyence", "Normal"));
        Assert.Equal(
            ["B", "CR", "ZF", "W", "TM", "CM", "X", "Y", "M", "L", "D", "E", "F"],
            ProjectFactory.SupportedDeviceNames("Keyence", "Xym"));
    }

    [Theory]
    [InlineData("Keyence", "Normal", "DM100", "Int16", "DM100")]
    [InlineData("Keyence", "Xym", "D100", "Int16", "D100")]
    [InlineData("Keyence", "Xym", "X0", "Bit", "X0")]
    public void ProjectFactoryAllowsDeviceFamiliesSupportedBySelectedKeyenceMode(
        string vendor,
        string keyenceDeviceMode,
        string address,
        string expectedDataType,
        string expectedAddress)
    {
        var project = ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            Vendor: vendor,
            KeyenceDeviceMode: keyenceDeviceMode,
            MachineLabel: vendor == "Keyence" && keyenceDeviceMode == "Xym" ? "KEYENCE KV-8000 (XYM)" : null,
            DevicesText: $"{address},{expectedDataType}",
            WatchText: $"{address},{expectedDataType}",
            TrapsText: ""));

        Assert.Equal(expectedDataType, project.Devices.Single().DataType);
        Assert.Equal(expectedAddress, project.TimeChart.Single().Address);
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
            ProjectFactory.BuildDeviceBlock("X0", 9, "Melsec", "Normal", "MELSEC iQ-F (built-in)"));
        Assert.Equal(
            ["R015", "R100"],
            ProjectFactory.BuildDeviceBlock("R015", 2, "Keyence", "Normal"));
        Assert.Equal(
            ["X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8", "X9", "XA", "XB", "XC", "XD", "XE", "XF", "X10"],
            ProjectFactory.BuildDeviceBlock("X0", 17, "Keyence", "Xym"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectFactory.BuildDeviceBlock("X1999F", 2, "Keyence", "Xym"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectFactory.BuildDeviceBlock("R199915", 2, "Keyence", "Normal"));
    }

    [Theory]
    [InlineData("D100,Bit")]
    [InlineData("X000,Int16")]
    public void ProjectFactoryRejectsDataTypesThatDoNotMatchDeviceKind(string devicesText)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProjectFactory.MakeProject(ProjectInputBuilder.MakeInput(
            DevicesText: devicesText,
            WatchText: "",
            TrapsText: "")));

        Assert.Contains("Invalid device data type", exception.Message);
    }

    [Fact]
    public void ProjectFactoryReportsAvailableDataTypesByDeviceKind()
    {
        Assert.Equal(["Bit"], ProjectFactory.DeviceDataTypesForAddress("X0", "Melsec"));
        Assert.Equal(["Bit"], ProjectFactory.DeviceDataTypesForAddress("STC0", "Melsec"));
        Assert.Equal(["Bit"], ProjectFactory.DeviceDataTypesForAddress("X39F", "Keyence", "Xym"));
        Assert.DoesNotContain("Bit", ProjectFactory.DeviceDataTypesForAddress("D0", "Melsec"));
        Assert.DoesNotContain("Bit", ProjectFactory.DeviceDataTypesForAddress("SD0", "Melsec"));
    }

    private static PlcProject SampleProject() => ProjectFactory.MakeProject(new ProjectInput(
        Name: "Unit Project",
        Vendor: "Melsec",
        ConnectionMode: "Real",
        Host: "192.168.250.100",
        Port: 1025,
        MonitorIntervalMs: 500,
        TimeoutMs: 2000,
        MachineLabel: "MELSEC iQ-R (built-in)",
        KeyenceDeviceMode: "Normal",
        TransportMode: "Tcp",
        Network: 0,
        Station: 255,
        ModuleIo: "OwnStation",
        DevicesText: "X000,Bit,Start input\r\nD100,Int16,Speed word",
        WatchText: "X000,Bit\r\nD100,Int16",
        TrapsText: "D100,Int16,GreaterOrEqual,100,true"),
        nowEpochMs: 123);

}
