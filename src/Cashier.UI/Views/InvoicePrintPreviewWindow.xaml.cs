using System.Windows;
using System.Windows.Documents;

namespace Cashier.UI.Views;

public partial class InvoicePrintPreviewWindow : Window
{
    private FlowDocument document;
    private readonly string invoiceNumber;
    private readonly Func<bool, FlowDocument>? documentFactory;
    private bool isThermal;

    public InvoicePrintPreviewWindow(
        FlowDocument document,
        string invoiceNumber,
        Func<bool, FlowDocument>? documentFactory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);

        InitializeComponent();
        this.document = document;
        this.invoiceNumber = invoiceNumber;
        this.documentFactory = documentFactory;
        isThermal = documentFactory is not null && PaperFormatComboBox.SelectedIndex == 1;
        InvoiceDocumentViewer.Document = document;
        if (documentFactory is not null)
        {
            PaperFormatPanel.Visibility = Visibility.Visible;
            PaperFormatHint.Visibility = Visibility.Visible;
        }
    }

    private void PaperFormatComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (documentFactory is null || !IsInitialized)
            return;

        isThermal = PaperFormatComboBox.SelectedIndex == 1;
        document = documentFactory(isThermal);
        InvoiceDocumentViewer.Document = document;
    }

    private void Print_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var pageWidth = printDialog.PrintableAreaWidth;
            var pageHeight = printDialog.PrintableAreaHeight;
            if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
                throw new InvalidOperationException("تعذر الحصول على مقاس الورق من الطابعة المحددة.");

            var layoutWidth = isThermal
                ? Math.Min(pageWidth, 80 * 96 / 25.4)
                : pageWidth;
            var horizontalPadding = isThermal
                ? Math.Min(8, Math.Max(2, (layoutWidth - 120) / 2))
                : Math.Min(48, Math.Max(12, (layoutWidth - 144) / 2));
            document.PageWidth = layoutWidth;
            document.PageHeight = pageHeight;
            document.PagePadding = new Thickness(horizontalPadding, isThermal ? 8 : 24, horizontalPadding, isThermal ? 8 : 24);
            document.ColumnWidth = double.PositiveInfinity;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(layoutWidth, pageHeight);
            printDialog.PrintDocument(paginator, $"فاتورة-{invoiceNumber}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"تعذرت طباعة الفاتورة: {exception.Message}", "تعذر الطباعة",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) => Close();
}
