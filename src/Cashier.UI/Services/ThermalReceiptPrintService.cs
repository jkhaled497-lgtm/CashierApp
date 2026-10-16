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

public sealed class ThermalReceiptPrintService : IPrintService
{
    private const double A4Width = 793.7;
    private const double A4Height = 1122.5;
    private static readonly Brush InkBrush = Brushes.Black;
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(190, 196, 204));
    private static readonly Brush SoftBrush = new SolidColorBrush(Color.FromRgb(246, 247, 249));
    private static readonly Brush TotalBrush = new SolidColorBrush(Color.FromRgb(230, 239, 250));
    private static readonly Brush PaidBrush = new SolidColorBrush(Color.FromRgb(226, 243, 232));
    private static readonly Brush DueBrush = new SolidColorBrush(Color.FromRgb(255, 239, 219));

    public Task PrintReceiptAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        cancellationToken.ThrowIfCancellationRequested();

        var preview = new InvoicePrintPreviewWindow(
            BuildDocument(invoice, isThermal: true),
            invoice.InvoiceNumber,
            isThermal => BuildDocument(invoice, isThermal))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        preview.ShowDialog();
        return Task.CompletedTask;
    }

    private static FlowDocument BuildDocument(Invoice invoice, bool isThermal)
    {
        var pageWidth = isThermal ? 80 * 96 / 25.4 : A4Width;
        var horizontalPadding = isThermal ? 8 : 42;
        var document = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = isThermal ? 1600 : A4Height,
            PagePadding = new Thickness(horizontalPadding, isThermal ? 10 : 36, horizontalPadding, isThermal ? 10 : 36),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = isThermal ? 9 : 11,
            Foreground = InkBrush,
            Background = Brushes.White,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right
        };

        document.Blocks.Add(BuildHeader(invoice, isThermal));
        document.Blocks.Add(BuildInvoiceDetails(invoice, isThermal));
        document.Blocks.Add(BuildItemsTable(invoice, isThermal));
        document.Blocks.Add(BuildTotals(invoice, isThermal));

        if (invoice.Payments.Count > 0)
        {
            document.Blocks.Add(SectionTitle("طرق الدفع"));
            document.Blocks.Add(BuildPaymentsTable(invoice, isThermal));
        }

        document.Blocks.Add(BuildFooter(invoice, isThermal));
        return document;
    }

    private static Table BuildHeader(Invoice invoice, bool isThermal)
    {
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, isThermal ? 10 : 18)
        };
        if (isThermal)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var cell = BorderlessCell();
            cell.Blocks.Add(new Paragraph(new Run(ReceiptBranding.BusinessName))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = AccentBrush,
                Margin = new Thickness(0, 0, 0, 3)
            });
            cell.Blocks.Add(new Paragraph(new Run($"تليفون: {ReceiptBranding.Phone}"))
            {
                FontSize = 8,
                TextAlignment = TextAlignment.Center,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            cell.Blocks.Add(new Paragraph(new Run("فاتورة مبيعات"))
            {
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 0, 7)
            });
            cell.Blocks.Add(new Paragraph(new Run($"رقم {invoice.InvoiceNumber}  •  {StatusName(invoice.Status)}"))
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            });
            cell.Blocks.Add(new Paragraph(new Run(BusinessTime.ToEgyptLocal(invoice.CreatedAt)
                .ToString("dd-MM-yyyy hh:mm tt", CultureInfo.GetCultureInfo("ar-EG"))))
            {
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                Foreground = MutedBrush,
                Margin = new Thickness(0)
            });
            cell.BorderThickness = new Thickness(0, 0, 0, 1.2);
            cell.BorderBrush = LineBrush;
            cell.Padding = new Thickness(0, 0, 0, 8);
            table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } });
            return table;
        }

        table.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var row = new TableRow();
        var brand = BorderlessCell();
        brand.Padding = new Thickness(0, 0, 8, 10);
        brand.BorderThickness = new Thickness(0, 0, 0, 1.2);
        brand.BorderBrush = LineBrush;
        var brandTitle = new Paragraph(new Run(ReceiptBranding.BusinessName))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        brand.Blocks.Add(brandTitle);
        brand.Blocks.Add(new Paragraph(new Run($"تليفون: {ReceiptBranding.Phone}"))
        {
            FontSize = 9,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        brand.Blocks.Add(new Paragraph(new Run("فاتورة مبيعات"))
        {
            FontSize = 13,
            Foreground = MutedBrush,
            Margin = new Thickness(0)
        });

        var invoiceCell = BorderlessCell();
        invoiceCell.Padding = new Thickness(8, 0, 0, 10);
        invoiceCell.BorderThickness = new Thickness(0, 0, 0, 1.2);
        invoiceCell.BorderBrush = LineBrush;
        invoiceCell.Blocks.Add(new Paragraph(new Run("رقم الفاتورة"))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 5)
        });
        invoiceCell.Blocks.Add(new Paragraph(new Run(invoice.InvoiceNumber))
        {
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBrush,
            TextAlignment = TextAlignment.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, 0, 0, 7)
        });
        var statusRun = new Run($"  {StatusName(invoice.Status)}")
        {
            FontWeight = FontWeights.SemiBold,
            Background = StatusBrush(invoice.Status)
        };
        var statusParagraph = new Paragraph(new Run("الحالة: "))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 3)
        };
        statusParagraph.Inlines.Add(statusRun);
        invoiceCell.Blocks.Add(statusParagraph);
        invoiceCell.Blocks.Add(new Paragraph(new Run(BusinessTime.ToEgyptLocal(invoice.CreatedAt)
            .ToString("dd-MM-yyyy hh:mm tt", CultureInfo.GetCultureInfo("ar-EG"))))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            Margin = new Thickness(0)
        });
        row.Cells.Add(invoiceCell);
        row.Cells.Add(brand);
        table.RowGroups.Add(new TableRowGroup { Rows = { row } });
        return table;
    }

    private static Table BuildInvoiceDetails(Invoice invoice, bool isThermal)
    {
        var columnCount = isThermal ? 1 : 2;
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, isThermal ? 8 : 14)
        };
        for (var column = 0; column < columnCount; column++)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var details = new List<(string Label, string Value)>
        {
            ("العميل", invoice.Customer?.FullName ?? "عميل نقدي"),
            ("الكاشير", invoice.CreatedByUser?.Username ?? "—")
        };
        if (!string.IsNullOrWhiteSpace(invoice.Customer?.Phone))
            details.Add(("الهاتف", invoice.Customer.Phone));

        for (var index = 0; index < details.Count; index += columnCount)
        {
            var row = new TableRow();
            for (var column = 0; column < columnCount; column++)
            {
                var detailIndex = index + column;
                row.Cells.Add(detailIndex < details.Count
                    ? InfoCard(details[detailIndex].Label, details[detailIndex].Value, isThermal)
                    : BorderlessCell());
            }
            group.Rows.Add(row);
        }
        return table;
    }

    private static Table BuildItemsTable(Invoice invoice, bool isThermal)
    {
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, isThermal ? 9 : 16)
        };
        if (isThermal)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });
        }
        else
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(4, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1.8, GridUnitType.Star) });
        }

        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        var header = new TableRow { FontWeight = FontWeights.SemiBold };
        var headerPadding = isThermal ? new Thickness(2, 5, 2, 5) : new Thickness(6, 10, 6, 10);
        header.Cells.Add(Cell("الصنف", TextAlignment.Right, emphasized: true, background: SoftBrush, padding: headerPadding));
        header.Cells.Add(Cell("الكمية", TextAlignment.Center, emphasized: true, background: SoftBrush, padding: headerPadding));
        if (!isThermal)
        {
            header.Cells.Add(Cell("سعر الوحدة", TextAlignment.Left, leftToRight: true, emphasized: true, background: SoftBrush, padding: headerPadding));
            header.Cells.Add(Cell("الخصم", TextAlignment.Left, leftToRight: true, emphasized: true, background: SoftBrush, padding: headerPadding));
        }
        header.Cells.Add(Cell("الإجمالي", TextAlignment.Left, leftToRight: true, emphasized: true, background: SoftBrush, padding: headerPadding));
        rows.Rows.Add(header);

        var rowIndex = 0;
        foreach (var item in invoice.Items)
        {
            var background = !isThermal && rowIndex % 2 == 1 ? SoftBrush : Brushes.White;
            rowIndex++;
            var padding = isThermal ? new Thickness(2, 5, 2, 5) : new Thickness(6, 8, 6, 8);
            var row = new TableRow();
            row.Cells.Add(Cell(item.ProductName, TextAlignment.Right, background: background, padding: padding));
            row.Cells.Add(Cell(item.Quantity.ToString("0.###", CultureInfo.InvariantCulture), TextAlignment.Center, true,
                background: background, padding: padding));
            if (!isThermal)
            {
                row.Cells.Add(Cell(FormatMoney(item.UnitPrice), TextAlignment.Left, true, background: background, padding: padding));
                row.Cells.Add(Cell(FormatMoney(item.DiscountAmount), TextAlignment.Left, true, background: background, padding: padding));
            }
            row.Cells.Add(Cell(FormatMoney(item.LineTotal), TextAlignment.Left, true,
                emphasized: true, background: background, padding: padding));
            rows.Rows.Add(row);
        }

        return table;
    }

    private static Table BuildTotals(Invoice invoice, bool isThermal)
    {
        var table = NewTable(isThermal ? 1 : 2.6, isThermal ? 1 : 1.4);
        table.Margin = new Thickness(0, 0, 0, isThermal ? 10 : 16);
        var padding = isThermal ? new Thickness(2, 4, 2, 4) : new Thickness(8, 7, 8, 7);
        AddTotal(table, "المجموع الفرعي", invoice.Subtotal, padding: padding);
        AddTotal(table, "الخصم", -invoice.DiscountTotal, padding: padding);
        if (invoice.TaxTotal != 0)
            AddTotal(table, "الضريبة", invoice.TaxTotal, padding: padding);
        if (invoice.Rounding != 0)
            AddTotal(table, "التقريب", invoice.Rounding, padding: padding);
            AddTotal(table, "الإجمالي النهائي", invoice.GrandTotal, true, TotalBrush, padding);

        var remaining = Math.Max(0, invoice.GrandTotal - invoice.TotalPaid);
        AddTotal(table, "المدفوع", invoice.TotalPaid, badge: remaining == 0 ? PaidBrush : null, padding: padding);
        if (remaining > 0)
            AddTotal(table, "المتبقي على العميل", remaining, badge: DueBrush, padding: padding);
        if (invoice.ChangeReturned > 0)
            AddTotal(table, "الباقي النقدي", invoice.ChangeReturned, padding: padding);
        return table;
    }

    private static Table BuildPaymentsTable(Invoice invoice, bool isThermal)
    {
        var paymentCount = Math.Max(1, Math.Min(invoice.Payments.Count, isThermal ? 1 : 3));
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, isThermal ? 8 : 14)
        };
        for (var i = 0; i < paymentCount; i++)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var row = new TableRow();
        foreach (var payment in invoice.Payments)
        {
            var paymentText = $"{PaymentMethodName(payment.Method)}  ·  {FormatMoney(payment.Amount)}";
            var paragraph = new Paragraph(new Run(paymentText))
            {
                Margin = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                FontSize = isThermal ? 8 : 10,
                TextAlignment = TextAlignment.Center
            };
            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = isThermal ? new Thickness(2, 5, 2, 5) : new Thickness(8, 7, 8, 7),
                Background = SoftBrush,
                BorderThickness = new Thickness(0, 0, 0, 0.7),
                BorderBrush = LineBrush
            });
            if (row.Cells.Count == paymentCount)
            {
                group.Rows.Add(row);
                row = new TableRow();
            }
        }
        if (row.Cells.Count > 0)
            group.Rows.Add(row);
        return table;
    }

    private static Block BuildFooter(Invoice invoice, bool isThermal)
    {
        var footer = new Section { Margin = new Thickness(0, isThermal ? 8 : 14, 0, 0) };
        footer.Blocks.Add(new Paragraph(new Run("شكراً لتعاملكم معنا"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = isThermal ? 10 : 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        footer.Blocks.Add(new Paragraph(new Run($"فاتورة رقم {invoice.InvoiceNumber}"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 9,
            Foreground = MutedBrush,
            Margin = new Thickness(0)
        });
        footer.Blocks.Add(new Paragraph(new Run("رمز مرجعي للفاتورة"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = 8,
            Foreground = MutedBrush,
            Margin = new Thickness(4, 4, 4, 0)
        });
        var barcode = CreateCode(invoice.InvoiceNumber);
        footer.Blocks.Add(new BlockUIContainer(new Image
        {
            Source = barcode,
            Width = isThermal ? 32 : 48,
            Height = isThermal ? 32 : 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        }));
        return footer;
    }

    private static Table NewTable(double firstColumn, double secondColumn)
    {
        var table = new Table { CellSpacing = 0, FlowDirection = FlowDirection.RightToLeft };
        table.Columns.Add(new TableColumn { Width = new GridLength(firstColumn, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(secondColumn, GridUnitType.Star) });
        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static TableCell InfoCard(string label, string value, bool isThermal)
    {
        var cell = BorderlessCell();
        cell.Padding = isThermal ? new Thickness(1, 3, 1, 3) : new Thickness(6, 5, 6, 5);
        cell.Blocks.Add(new Paragraph(new Run(label))
        {
            Margin = new Thickness(0, 0, 0, 2),
            FontSize = isThermal ? 8 : 9,
            Foreground = MutedBrush
        });
        cell.Blocks.Add(new Paragraph(new Run(value))
        {
            Margin = new Thickness(0),
            FontSize = isThermal ? 9 : 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        });
        return cell;
    }

    private static TableCell BorderlessCell() => new()
    {
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Background = Brushes.White
    };

    private static void AddTotal(
        Table table,
        string label,
        decimal amount,
        bool emphasized = false,
        Brush? badge = null,
        Thickness? padding = null)
    {
        var row = new TableRow
        {
            Background = badge ?? (emphasized ? TotalBrush : Brushes.White),
            Foreground = InkBrush,
            FontWeight = emphasized ? FontWeights.Bold : FontWeights.Normal
        };
        var cellPadding = padding ?? new Thickness(8, 7, 8, 7);
        row.Cells.Add(Cell(label, TextAlignment.Right, emphasized: emphasized, background: row.Background, padding: cellPadding));
        row.Cells.Add(MoneyCell(amount, emphasized, InkBrush, row.Background, cellPadding));
        table.RowGroups[0].Rows.Add(row);
    }

    private static TableCell MoneyCell(
        decimal amount,
        bool emphasized = false,
        Brush? foreground = null,
        Brush? background = null,
        Thickness? padding = null) =>
        Cell(FormatMoney(amount), TextAlignment.Left, true, emphasized, foreground, background, padding);

    private static TableCell Cell(
        string text,
        TextAlignment alignment,
        bool leftToRight = false,
        bool emphasized = false,
        Brush? foreground = null,
        Brush? background = null,
        Thickness? padding = null)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            TextAlignment = alignment,
            FlowDirection = leftToRight ? FlowDirection.LeftToRight : FlowDirection.RightToLeft
        };
        return new TableCell(paragraph)
        {
            Padding = padding ?? new Thickness(8, 6, 8, 6),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.6),
            FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground ?? InkBrush,
            Background = background ?? Brushes.White
        };
    }

    private static Table SectionTitle(string text)
    {
        var table = new Table
        {
            CellSpacing = 0,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 10, 0, 5)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = AccentBrush
        };
        var cell = new TableCell(paragraph)
        {
            Background = Brushes.White,
            Padding = new Thickness(0, 4, 0, 5),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.8)
        };
        table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } });
        return table;
    }

    private static string PaymentMethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقداً",
        PaymentMethod.DebitCard => "بطاقة مدى",
        PaymentMethod.CreditCard => "بطاقة ائتمان",
        PaymentMethod.ClientAccount => "على حساب العميل",
        _ => method.ToString()
    };

    private static string StatusName(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => "مدفوعة",
        InvoiceStatus.Partial => "مدفوعة جزئياً",
        InvoiceStatus.Refunded => "مستردة",
        InvoiceStatus.Void => "ملغاة",
        _ => status.ToString()
    };

    private static Brush StatusBrush(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => PaidBrush,
        InvoiceStatus.Partial => DueBrush,
        _ => SoftBrush
    };

    private static string FormatMoney(decimal amount) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} ج.م";

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
}
