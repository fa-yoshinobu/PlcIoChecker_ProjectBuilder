using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf;

namespace PlcIoCheckerQr.Wpf.Tests;

public sealed class ClipboardImportTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("  true  ", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ParseClipboardBooleanHandlesCanonicalValues(string input, bool expected)
    {
        Assert.Equal(expected, ClipboardImport.ParseClipboardBoolean(input));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("checked")]
    [InlineData("有効")]
    [InlineData("○")]
    [InlineData("")]
    public void ParseClipboardBooleanRejectsAliases(string input)
    {
        Assert.Throws<ArgumentException>(() => ClipboardImport.ParseClipboardBoolean(input));
    }

    [Fact]
    public void SplitClipboardLinePrefersTabs()
    {
        Assert.Equal(["A", "B", "C"], ClipboardImport.SplitClipboardLine("A\tB\tC"));
        Assert.Equal(["A", "B,C"], ClipboardImport.SplitClipboardLine("A\tB,C"));
        Assert.Equal(["A", "B", "C"], ClipboardImport.SplitClipboardLine("A,B,C"));
    }

    [Fact]
    public void SplitClipboardRowsKeepsQuotedNewlinesInsideField()
    {
        var rows = ClipboardImport.SplitClipboardRows("D00\tBIT\t\"モーター\r\nポンプ\"\r\nD01\tBIT\tOK");

        Assert.Collection(
            rows,
            row => Assert.Equal(["D00", "BIT", "モーター\r\nポンプ"], row),
            row => Assert.Equal(["D01", "BIT", "OK"], row));
    }

    [Fact]
    public void SplitClipboardRowsUnescapesQuotedDoubleQuotes()
    {
        var rows = ClipboardImport.SplitClipboardRows("D00\tBIT\t\"A \"\"quoted\"\" comment\"");

        var row = Assert.Single(rows);
        Assert.Equal(["D00", "BIT", "A \"quoted\" comment"], row);
    }

    [Fact]
    public void FormatClipboardRowsEscapesExcelFields()
    {
        var text = ClipboardImport.FormatClipboardRows(
        [
            ["D00", "BIT", "A\tquoted \"comment\""],
            ["D01", "Int16", "line1\r\nline2"],
        ]);

        Assert.Equal(
            $"D00\tBIT\t\"A\tquoted \"\"comment\"\"\"{Environment.NewLine}D01\tInt16\t\"line1\r\nline2\"",
            text);
        Assert.Collection(
            ClipboardImport.SplitClipboardRows(text),
            row => Assert.Equal(["D00", "BIT", "A\tquoted \"comment\""], row),
            row => Assert.Equal(["D01", "Int16", "line1\r\nline2"], row));
    }

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("line1\r\nline2", "line1  line2")]
    [InlineData("newline\nonly", "newline only")]
    [InlineData("  padded  ", "padded")]
    public void NormalizeDeviceCommentCollapsesLineBreaks(string input, string expected)
    {
        Assert.Equal(expected, ClipboardImport.NormalizeDeviceComment(input));
    }

    [Fact]
    public void NormalizeDeviceCommentRejectsOverMaxComment()
    {
        var comment = new string('あ', ProjectCommentRules.MaxCommentCharacters + 1);

        Assert.Throws<ArgumentException>(() => ClipboardImport.NormalizeDeviceComment(comment));
    }

    [Fact]
    public void DeviceCommentFromFieldsIgnoresTrailingEmptyFields()
    {
        Assert.Equal("モーター", ClipboardImport.DeviceCommentFromFields(["D00", "BIT", "モーター", ""], 2));
    }

    [Fact]
    public void DeviceCommentFromFieldsNormalizesQuotedExcelNewlineComment()
    {
        var row = Assert.Single(ClipboardImport.SplitClipboardRows("D00\tBIT\t\"モーター\r\nポンプ\""));

        Assert.Equal("モーター  ポンプ", ClipboardImport.DeviceCommentFromFields(row, 2));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void FirstValueIndexAfterOptionalDataTypeSkipsBlankDataTypeColumn(bool hasDataType, int expected)
    {
        Assert.Equal(expected, ClipboardImport.FirstValueIndexAfterOptionalDataType(["D00", "", "モーター"], hasDataType));
    }

    [Fact]
    public void FirstValueIndexAfterOptionalDataTypeUsesSecondColumnWhenItIsNotBlank()
    {
        Assert.Equal(1, ClipboardImport.FirstValueIndexAfterOptionalDataType(["D00", "モーター"], hasDataType: false));
    }

    [Theory]
    [InlineData("Address\tData type", true)]
    [InlineData("アドレス\tデータ型", false)]
    [InlineData("D100\tInt16", false)]
    [InlineData("", false)]
    public void IsDeviceClipboardHeaderDetectsHeaders(string line, bool expected)
    {
        var fields = ClipboardImport.SplitClipboardLine(line);
        Assert.Equal(expected, ClipboardImport.IsDeviceClipboardHeader(fields));
    }

    [Fact]
    public void IsDeviceClipboardHeaderDetectsDataTypeColumnAsHeader()
    {
        var fields = ClipboardImport.SplitClipboardLine("X\tData type\tY");
        Assert.True(ClipboardImport.IsDeviceClipboardHeader(fields));
    }

    [Fact]
    public void IsDeviceClipboardHeaderDetectsJapaneseDataTypeColumnAsHeader()
    {
        var fields = ClipboardImport.SplitClipboardLine("X\tデータ型\tY");
        Assert.False(ClipboardImport.IsDeviceClipboardHeader(fields));
    }

    [Theory]
    [InlineData("Address\tComment", true)]
    [InlineData("アドレス\tコメント", false)]
    [InlineData("D100\tSpeed word", false)]
    public void IsCommentClipboardHeaderDetectsHeaders(string line, bool expected)
    {
        var fields = ClipboardImport.SplitClipboardLine(line);
        Assert.Equal(expected, ClipboardImport.IsCommentClipboardHeader(fields));
    }

    [Theory]
    [InlineData("Address", true)]
    [InlineData("アドレス", false)]
    [InlineData("タイムチャート", false)]
    [InlineData("Time chart", true)]
    [InlineData("D100", false)]
    public void IsAddressClipboardHeaderDetectsHeaders(string first, bool expected)
    {
        Assert.Equal(expected, ClipboardImport.IsAddressClipboardHeader([first]));
    }

    [Fact]
    public void IsAddressClipboardHeaderReturnsFalseForEmptyFields()
    {
        Assert.False(ClipboardImport.IsAddressClipboardHeader([]));
    }

    [Fact]
    public void IsTrapClipboardHeaderDetectsHeaderOnFirstOrSecondColumn()
    {
        Assert.True(ClipboardImport.IsTrapClipboardHeader(["Address"]));
        Assert.True(ClipboardImport.IsTrapClipboardHeader(["D100", "Condition"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader(["アドレス"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader(["D100", "検知条件"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader(["D100", "Int16"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader([]));
    }

    [Fact]
    public void NormalizeDeviceDataTypeRejectsUnsupportedValues()
    {
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeDeviceDataType("", "D100", "Melsec", "Normal"));
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeDeviceDataType("Bit", "D100", "Melsec", "Normal"));
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeDeviceDataType("Int16", "X0", "Melsec", "Normal"));
    }

    [Fact]
    public void NormalizeTrapConditionRejectsAliasesAndWrongAddressKinds()
    {
        Assert.Equal("Rise", ClipboardImport.NormalizeTrapCondition("Rise", "X0", "Melsec", "Normal"));
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeTrapCondition("立上り", "X0", "Melsec", "Normal"));
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeTrapCondition("", "X0", "Melsec", "Normal"));
        Assert.Throws<ArgumentException>(() =>
            ClipboardImport.NormalizeTrapCondition("GreaterOrEqual", "X0", "Melsec", "Normal"));
    }
}
