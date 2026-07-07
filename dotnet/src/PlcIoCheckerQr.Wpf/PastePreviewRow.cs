namespace PlcIoCheckerQr.Wpf;

/// <summary>
/// One clipboard row prepared for the paste preview dialog.
/// A row is valid when <see cref="ParsedRow"/> is set, an error row when
/// <see cref="Error"/> is set, and otherwise skipped (blank address).
/// </summary>
internal sealed class PastePreviewRow
{
    public required int LineNumber { get; init; }

    public required string[] Values { get; init; }

    public object? ParsedRow { get; init; }

    public string? Error { get; init; }

    public bool IsValid => ParsedRow is not null && Error is null;
}
