using PlcIoCheckerQr.Wpf;
using PlcIoCheckerQr.Wpf.Localization;

namespace PlcIoCheckerQr.Wpf.Tests;

public sealed class UiValueMappingTests
{
    [Fact]
    public void ModuleIoDisplayTextUsesLocalizedMeaningOnly()
    {
        var english = LanguageCatalog.Load("en");
        var japanese = LanguageCatalog.Load("ja");

        Assert.Equal("Own station", UiValueMapping.ModuleIoDisplayText("OwnStation", english));
        Assert.Equal("Control system CPU", UiValueMapping.ModuleIoDisplayText("ControlSystemCpu", english));
        Assert.Equal("自局", UiValueMapping.ModuleIoDisplayText("OwnStation", japanese));
        Assert.Equal("制御系CPU", UiValueMapping.ModuleIoDisplayText("ControlSystemCpu", japanese));
        Assert.DoesNotContain("0x", UiValueMapping.ModuleIoDisplayText("OwnStation", english), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", UiValueMapping.ModuleIoDisplayText("OwnStation", japanese), StringComparison.OrdinalIgnoreCase);
    }
}
