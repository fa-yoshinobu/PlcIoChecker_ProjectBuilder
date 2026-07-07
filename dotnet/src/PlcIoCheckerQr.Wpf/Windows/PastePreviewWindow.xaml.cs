using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PlcIoCheckerQr.Wpf.Localization;

namespace PlcIoCheckerQr.Wpf.Windows;

internal sealed partial class PastePreviewWindow : Window
{
    public PastePreviewWindow(
        LanguageCatalog language,
        string sectionTitle,
        IReadOnlyList<string> valueHeaders,
        IReadOnlyList<PastePreviewRow> rows)
    {
        InitializeComponent();

        var validCount = rows.Count(row => row.IsValid);
        var errorCount = rows.Count(row => row.Error is not null);
        var skippedCount = rows.Count - validCount - errorCount;

        Title = language.Format("paste.title", sectionTitle);
        TitleTextBlock.Text = Title;
        OkCountText.Text = $"{language.Text("paste.status.ok")} {validCount}";
        ErrorCountText.Text = $"{language.Text("paste.status.error")} {errorCount}";
        SkipCountText.Text = $"{language.Text("paste.status.skipped")} {skippedCount}";
        HintTextBlock.Text = language.Text("paste.hint");
        CancelButton.Content = language.Text("paste.cancel");
        ImportButton.Content = language.Format("paste.import", validCount);
        ImportButton.IsEnabled = validCount > 0;

        BuildColumns(language, valueHeaders);
        PreviewGrid.ItemsSource = rows
            .Select(row => new PastePreviewItem(
                row.LineNumber,
                language.Text(StatusKey(row)),
                row.IsValid ? "ok" : row.Error is not null ? "error" : "skip",
                row.Values,
                row.Error ?? ""))
            .ToList();
    }

    private static string StatusKey(PastePreviewRow row) =>
        row.IsValid
            ? "paste.status.ok"
            : row.Error is not null
                ? "paste.status.error"
                : "paste.status.skipped";

    private void BuildColumns(LanguageCatalog language, IReadOnlyList<string> valueHeaders)
    {
        var lineColumn = TextColumn(language.Text("paste.column.line"), nameof(PastePreviewItem.LineNumber), 52);
        lineColumn.ElementStyle = (Style)FindResource("LineCellTextStyle");
        PreviewGrid.Columns.Add(lineColumn);

        PreviewGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = language.Text("paste.column.status"),
            CellTemplate = (DataTemplate)FindResource("StatusCellTemplate"),
            Width = DataGridLength.Auto,
            MinWidth = 82,
        });

        for (var index = 0; index < valueHeaders.Count; index++)
        {
            var valueColumn = TextColumn(valueHeaders[index], $"{nameof(PastePreviewItem.Values)}[{index}]", width: null);
            valueColumn.MinWidth = 90;
            PreviewGrid.Columns.Add(valueColumn);
        }

        var errorColumn = TextColumn(language.Text("paste.column.error"), nameof(PastePreviewItem.Error), width: null);
        errorColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        errorColumn.MinWidth = 120;
        errorColumn.ElementStyle = (Style)FindResource("ErrorCellTextStyle");
        PreviewGrid.Columns.Add(errorColumn);
    }

    private static DataGridTextColumn TextColumn(string header, string path, double? width) =>
        new()
        {
            Header = header,
            Binding = new Binding(path),
            Width = width is null ? DataGridLength.Auto : new DataGridLength(width.Value),
            MinWidth = 44,
        };

    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record PastePreviewItem(int LineNumber, string Status, string Kind, string[] Values, string Error);
