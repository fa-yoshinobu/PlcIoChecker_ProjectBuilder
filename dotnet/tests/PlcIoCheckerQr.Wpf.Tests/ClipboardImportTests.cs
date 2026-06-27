using PlcIoCheckerQr.Core;
using PlcIoCheckerQr.Wpf;

namespace PlcIoCheckerQr.Wpf.Tests;

public sealed class ClipboardImportTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("checked", true)]
    [InlineData("有効", true)]
    [InlineData("○", true)]
    [InlineData("〇", true)]
    [InlineData("  true  ", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void ParseClipboardBooleanHandlesAllAliases(string input, bool expected)
    {
        Assert.Equal(expected, ClipboardImport.ParseClipboardBoolean(input));
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
    [InlineData("アドレス\tデータ型", true)]
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
        Assert.True(ClipboardImport.IsDeviceClipboardHeader(fields));
    }

    [Theory]
    [InlineData("Address\tComment", true)]
    [InlineData("アドレス\tコメント", true)]
    [InlineData("D100\tSpeed word", false)]
    public void IsCommentClipboardHeaderDetectsHeaders(string line, bool expected)
    {
        var fields = ClipboardImport.SplitClipboardLine(line);
        Assert.Equal(expected, ClipboardImport.IsCommentClipboardHeader(fields));
    }

    [Theory]
    [InlineData("Address", true)]
    [InlineData("アドレス", true)]
    [InlineData("タイムチャート", true)]
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
        Assert.True(ClipboardImport.IsTrapClipboardHeader(["アドレス"]));
        Assert.True(ClipboardImport.IsTrapClipboardHeader(["D100", "検知条件"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader(["D100", "Int16"]));
        Assert.False(ClipboardImport.IsTrapClipboardHeader([]));
    }

    [Fact]
    public void LoadImportAliasesIncludesEnglishAndJapaneseAliases()
    {
        var aliases = ClipboardImport.LoadImportAliases();
        Assert.True(aliases.ContainsKey("import.alias.boolean.true"));
        var boolAliases = aliases["import.alias.boolean.true"];
        Assert.Contains("1", boolAliases);
        Assert.Contains("有効", boolAliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("○", boolAliases);
    }
}
