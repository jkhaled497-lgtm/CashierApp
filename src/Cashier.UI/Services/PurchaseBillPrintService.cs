using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cashier.Application;
using Cashier.Domain;
using Cashier.UI.Views;
using ZXing;
using ZXing.Common;

namespace Cashier.UI.Services;

public sealed class PurchaseBillPrintService : IPurchaseBillPrintService
{
    private const double A4Width = 793.7;
    private const double A4Height = 1122.5;
    private const double ThermalWidth = 80 * 96 / 25.4;
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    private static readonly Brush SoftBrush = new SolidColorBrush(Color.FromRgb(246, 247, 249));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(190, 196, 204));
    private static readonly CultureInfo EgyptCulture = CultureInfo.GetCultureInfo("ar-EG");

    public void Print(PurchaseInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var preview = new InvoicePrintPreviewWindow(
            BuildDocument(invoice, isThermal: true),
            invoice.InvoiceNumber,
            isThermal => BuildDocument(invoice, isThermal))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        preview.ShowDialog();
    }

    private static FlowDocument BuildDocument(PurchaseInvoice invoice, bool isThermal)
    {
        var horizontalPadding = isThermal ? 8 : 44;
        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            PageWidth = isThermal ? ThermalWidth : A4Width,
            PageHeight = isThermal ? 1600 : A4Height,
            PagePadding = new Thickness(horizontalPadding, isThermal ? 10 : 44, horizontalPadding, isThermal ? 10 : 44),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new System.Windows.Media.FontFamily(isThermal ? "Segoe UI" : "Arial"),
            FontSize = isThermal ? 9 : 11,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Right
        };

        if (isThermal)
        {
            document.Blocks.Add(BuildThermalHeader(invoice));
            document.Blocks.Add(BuildThermalDetails(invoice));
            document.Blocks.Add(BuildThermalItems(invoice));
            document.Blocks.Add(BuildThermalTotals(invoice));
            if (invoice.Payments.Count > 0)
                document.Blocks.Add(BuildThermalPayments(invoice));
            document.Blocks.Add(BuildThermalFooter(invoice));
            return document;
        }

        var header = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, 16)
        };
        header.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        header.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var headerRow = new TableRow { Background = Brushes.White };
        var sellerCell = new TableCell
        {
            Padding = new Thickness(16, 13, 16, 13),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0.7, 0.7, 0, 2)
        };
        sellerCell.Blocks.Add(new Paragraph(new Run(ReceiptBranding.BusinessName))
        {
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        sellerCell.Blocks.Add(new Paragraph(new Run("فاتورة مشتريات"))
        {
            FontSize = 13,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        sellerCell.Blocks.Add(new Paragraph(new Run($"تليفون: {ReceiptBranding.Phone}"))
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Margin = new Thickness(0)
        });
        headerRow.Cells.Add(sellerCell);
        headerRow.Cells.Add(new TableCell(new Paragraph(new Run($"رقم الفاتورة\n{invoice.InvoiceNumber}"))
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBrush,
            TextAlignment = TextAlignment.Left,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0)
        })
        {
            Padding = new Thickness(14),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0.7, 0.7, 2),
            Background = SoftBrush
        });
        header.RowGroups.Add(new TableRowGroup { Rows = { headerRow } });
        document.Blocks.Add(header);

        document.Blocks.Add(SectionTitle("بيانات الفاتورة"));
        var details = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 12) };
        details.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        details.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var detailsGroup = new TableRowGroup();
        details.RowGroups.Add(detailsGroup);
        AddRow(detailsGroup, ["المورد", invoice.Supplier.Name]);
        AddRow(detailsGroup, ["التاريخ والوقت", BusinessTime.ToEgyptLocal(invoice.CreatedAt)
            .ToString("dd-MM-yyyy hh:mm tt", EgyptCulture)]);
        document.Blocks.Add(details);

        document.Blocks.Add(SectionTitle("تفاصيل الأصناف"));
        var table = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 14) };
        table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.25, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.35, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        AddRow(group, ["المنتج", "سعر الوحدة", "الكمية", "الخصم", "الإجمالي"], bold: true, SoftBrush);
        var rowIndex = 0;
        foreach (var item in invoice.Items)
        {
            var background = rowIndex++ % 2 == 0 ? Brushes.White : SoftBrush;
            AddRow(group,
            [
                item.ProductName,
                FormatMoney(item.UnitPrice),
                item.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                FormatMoney(item.DiscountAmount),
                FormatMoney(item.LineTotal)
            ], fill: background);
        }
        document.Blocks.Add(table);

        document.Blocks.Add(SectionTitle("ملخص الفاتورة"));
        var totals = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 12) };
        totals.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        totals.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var totalsGroup = new TableRowGroup();
        totals.RowGroups.Add(totalsGroup);
        AddRow(totalsGroup, ["إجمالي الأصناف", FormatMoney(invoice.Subtotal)]);
        AddRow(totalsGroup, ["إجمالي الخصم", FormatMoney(invoice.DiscountTotal)]);
        AddRow(totalsGroup, ["الإجمالي المستحق", FormatMoney(invoice.GrandTotal)], bold: true, fill: SoftBrush);
        AddRow(totalsGroup, ["إجمالي المدفوع", FormatMoney(invoice.TotalPaid)]);
        AddRow(totalsGroup, ["المتبقي", FormatMoney(invoice.GrandTotal - invoice.TotalPaid)]);
        document.Blocks.Add(totals);

        if (invoice.Payments.Count > 0)
        {
            document.Blocks.Add(SectionTitle("المدفوعات"));
            var payments = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft };
            payments.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            payments.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var paymentsGroup = new TableRowGroup();
            payments.RowGroups.Add(paymentsGroup);
            AddRow(paymentsGroup, ["طريقة الدفع", "المبلغ"], bold: true, SoftBrush);
            foreach (var payment in invoice.Payments)
                AddRow(paymentsGroup, [ArabicPaymentMethod(payment.Method), FormatMoney(payment.Amount)]);
            document.Blocks.Add(payments);
        }

        return document;
    }

    private static Table BuildThermalHeader(PurchaseInvoice invoice)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(0, 0, 0, 1.2),
            BorderBrush = LineBrush
        };
        cell.Blocks.Add(new Paragraph(new Run(ReceiptBranding.BusinessName))
        {
            FontSize = 16,
            FontWeight = System.Windows.FontWeights.Bold,
            Foreground = AccentBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3)
        });
        cell.Blocks.Add(new Paragraph(new Run($"تليفون: {ReceiptBranding.Phone}"))
        {
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        cell.Blocks.Add(new Paragraph(new Run("فاتورة مشتريات"))
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7)
        });
        cell.Blocks.Add(new Paragraph(new Run($"رقم {invoice.InvoiceNumber}  •  {PurchaseStatusName(invoice.Status)}"))
        {
            FontSize = 10,
            FontWeight = System.Windows.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5)
        });
        cell.Blocks.Add(new Paragraph(new Run(BusinessTime.ToEgyptLocal(invoice.CreatedAt)
            .ToString("dd-MM-yyyy hh:mm tt", EgyptCulture)))
        {
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Margin = new Thickness(0)
        });

        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, 8)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } });
        return table;
    }

    private static Table BuildThermalDetails(PurchaseInvoice invoice)
    {
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, 8)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        AddThermalRow(rows, "المورد", invoice.Supplier.Name);
        if (!string.IsNullOrWhiteSpace(invoice.Supplier.Phone))
            AddThermalRow(rows, "هاتف المورد", invoice.Supplier.Phone);
        return table;
    }

    private static Table BuildThermalItems(PurchaseInvoice invoice)
    {
        var table = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 9) };
        table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });
        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        AddThermalItemRow(rows, ["الصنف", "الكمية", "الإجمالي"], bold: true, SoftBrush);
        foreach (var item in invoice.Items)
            AddThermalItemRow(rows,
                [item.ProductName, item.Quantity.ToString("0.###", CultureInfo.InvariantCulture), FormatMoney(item.LineTotal)]);
        return table;
    }

    private static Table BuildThermalTotals(PurchaseInvoice invoice)
    {
        var table = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        AddThermalRow(rows, "إجمالي الأصناف", FormatMoney(invoice.Subtotal));
        AddThermalRow(rows, "إجمالي الخصم", FormatMoney(invoice.DiscountTotal));
        AddThermalRow(rows, "الإجمالي المستحق", FormatMoney(invoice.GrandTotal), emphasized: true, SoftBrush);
        AddThermalRow(rows, "إجمالي المدفوع", FormatMoney(invoice.TotalPaid));
        AddThermalRow(rows, "المتبقي", FormatMoney(invoice.GrandTotal - invoice.TotalPaid));
        return table;
    }

    private static Table BuildThermalPayments(PurchaseInvoice invoice)
    {
        var table = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 0, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        foreach (var payment in invoice.Payments)
            AddThermalRow(rows, "طريقة الدفع", $"{ArabicPaymentMethod(payment.Method)}  ·  {FormatMoney(payment.Amount)}");
        return table;
    }

    private static Section BuildThermalFooter(PurchaseInvoice invoice)
    {
        var footer = new Section { Margin = new Thickness(0, 8, 0, 0) };
        footer.Blocks.Add(new Paragraph(new Run("شكراً لتعاملكم معنا"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 10,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        footer.Blocks.Add(new Paragraph(new Run("رمز مرجعي للفاتورة"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Margin = new Thickness(4, 4, 4, 0)
        });
        var barcode = CreateCode(invoice.InvoiceNumber);
        footer.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Image
        {
            Source = barcode,
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        }));
        return footer;
    }

    private static void AddThermalRow(TableRowGroup rows, string label, string value, bool emphasized = false, Brush? fill = null)
    {
        var row = new TableRow();
        foreach (var (text, alignment, leftToRight) in new[]
        {
            (label, TextAlignment.Right, false),
            (value, TextAlignment.Left, true)
        })
        {
            var paragraph = new Paragraph(new Run(text))
            {
                Margin = new Thickness(0),
                TextAlignment = alignment,
                FlowDirection = leftToRight ? FlowDirection.LeftToRight : FlowDirection.RightToLeft
            };
            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = new Thickness(2, 3, 2, 3),
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0, 0, 0, 0.6),
                Background = fill ?? Brushes.White,
                FontWeight = emphasized ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal
            });
        }
        rows.Rows.Add(row);
    }

    private static void AddThermalItemRow(TableRowGroup rows, IReadOnlyList<string> values, bool bold = false, Brush? fill = null)
    {
        var row = new TableRow();
        for (var index = 0; index < values.Count; index++)
        {
            var paragraph = new Paragraph(new Run(values[index]))
            {
                Margin = new Thickness(0),
                TextAlignment = index == 0 ? TextAlignment.Right : TextAlignment.Left,
                FlowDirection = index == 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            };
            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = new Thickness(2, 5, 2, 5),
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0, 0, 0, 0.6),
                Background = fill ?? Brushes.White,
                FontWeight = bold ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal
            });
        }
        rows.Rows.Add(row);
    }

    private static BitmapSource CreateCode(string value)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Height = 160, Width = 160, Margin = 1 }
        };
        var pixelData = writer.Write(value);
        return BitmapSource.Create(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgra32, null,
            pixelData.Pixels, pixelData.Width * 4);
    }

    private static string PurchaseStatusName(PurchaseInvoiceStatus status) => status switch
    {
        PurchaseInvoiceStatus.Paid => "مدفوعة",
        PurchaseInvoiceStatus.Partial => "مدفوعة جزئياً",
        _ => "غير مدفوعة"
    };

    private static void AddRow(TableRowGroup group, IReadOnlyList<string> values, bool bold = false, Brush? fill = null)
    {
        var row = new TableRow();
        foreach (var value in values)
        {
            var paragraph = new Paragraph(new Run(value))
            {
                Margin = new Thickness(0),
                TextAlignment = TextAlignment.Right
            };
            if (bold)
                paragraph.FontWeight = System.Windows.FontWeights.Bold;
            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0.5),
                Background = fill ?? Brushes.White
            });
        }
        group.Rows.Add(row);
    }

    private static Table SectionTitle(string title)
    {
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 6, 0, 6)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var cell = new TableCell(new Paragraph(new Run(title))
        {
            Margin = new Thickness(0),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = AccentBrush
        })
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = SoftBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0.5)
        };
        table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } });
        return table;
    }

    private static string FormatMoney(decimal amount) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} ج.م";

    private static string ArabicPaymentMethod(PaymentMethod method) =>
        method switch
        {
            PaymentMethod.Cash => "نقداً",
            PaymentMethod.DebitCard => "بطاقة مدى",
            PaymentMethod.CreditCard => "بطاقة ائتمان",
            PaymentMethod.ClientAccount => "على حساب العميل",
            _ => method.ToString()
        };
}
