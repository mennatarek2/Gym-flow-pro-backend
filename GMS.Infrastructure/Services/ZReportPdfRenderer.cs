namespace GMS.Infrastructure.Services;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using GMS.Core.Interfaces;
using GMS.Core.Models;

/// <summary>
/// Renders a bilingual (Arabic RTL + English LTR) A4 daily closing Z-Report PDF: payment-method
/// breakdown, sales-by-line-type, discounts, shift reconciliation rows, and outstanding balances.
/// </summary>
public class ZReportPdfRenderer : IZReportPdfRenderer
{
    private const char LeftToRightIsolate = '⁦';
    private const char PopDirectionalIsolate = '⁩';

    static ZReportPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(ZReportPdfModel model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Content().Column(column =>
                {
                    column.Spacing(10);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(header =>
                        {
                            header.Item().Text(model.TenantName).FontSize(16).Bold();
                            header.Item().Text(model.TenantNameAr).FontSize(14).Bold();
                            header.Item().Text($"Gym Code: {Isolate(model.GymCode)}").FontSize(8);
                        });

                        row.RelativeItem().AlignRight().Column(header =>
                        {
                            header.Item().Text(text =>
                            {
                                text.Span("Z-Report").FontSize(14).Bold();
                                text.Span("  إقفال يومي").FontSize(14).Bold();
                            });
                            header.Item().Text($"Date: {model.ReportDate:yyyy-MM-dd}").FontSize(9);
                            header.Item().Text($"Generated: {model.GeneratedAt:yyyy-MM-dd HH:mm} UTC").FontSize(8);
                        });
                    });

                    column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    // Payment method breakdown
                    column.Item().Text("Payment Methods / طرق الدفع").FontSize(11).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Method").Bold();
                            header.Cell().AlignCenter().Text("Count").Bold();
                            header.Cell().AlignRight().Text("Total").Bold();
                            header.Cell().ColumnSpan(3).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        });

                        foreach (var m in model.MethodTotals)
                        {
                            table.Cell().Text(m.Method);
                            table.Cell().AlignCenter().Text(m.Count.ToString());
                            table.Cell().AlignRight().Text($"{m.Total:N2} {model.Currency}");
                        }
                    });

                    // Sales by line type
                    column.Item().Text("Sales by Line Type / المبيعات حسب النوع").FontSize(11).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Line Type").Bold();
                            header.Cell().AlignCenter().Text("Count").Bold();
                            header.Cell().AlignRight().Text("Revenue").Bold();
                            header.Cell().ColumnSpan(3).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        });

                        foreach (var l in model.LineTypeTotals)
                        {
                            table.Cell().Text(l.LineType);
                            table.Cell().AlignCenter().Text(l.Count.ToString());
                            table.Cell().AlignRight().Text($"{l.Revenue:N2} {model.Currency}");
                        }
                    });

                    // Discounts & refunds
                    column.Item().Text("Discounts & Refunds / الخصومات والمرتجعات").FontSize(11).Bold();
                    column.Item().Column(discounts =>
                    {
                        discounts.Item().Text($"Promo discounts: {model.PromoDiscountTotal:N2} {model.Currency}");
                        discounts.Item().Text($"Manual discounts: {model.ManualDiscountTotal:N2} {model.Currency} ({model.ManualDiscountCount} override(s))");
                        discounts.Item().Text($"Refunds: {model.RefundsTotal:N2} {model.Currency}");
                    });

                    // Outstanding & membership revenue
                    column.Item().Column(totals =>
                    {
                        totals.Item().Text($"Outstanding added today: {model.OutstandingAddedToday:N2} {model.Currency}");
                        totals.Item().Text($"Membership revenue today: {model.MembershipRevenueToday:N2} {model.Currency}").Bold();
                    });

                    // Shifts
                    column.Item().Text("Shifts / الورديات").FontSize(11).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Staff").Bold();
                            header.Cell().Text("Opened").Bold();
                            header.Cell().Text("Closed").Bold();
                            header.Cell().AlignRight().Text("Float").Bold();
                            header.Cell().AlignRight().Text("Expected").Bold();
                            header.Cell().AlignRight().Text("Counted").Bold();
                            header.Cell().AlignRight().Text("Variance").Bold();
                            header.Cell().ColumnSpan(7).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        });

                        foreach (var s in model.Shifts)
                        {
                            table.Cell().Text($"{s.UserName} ({s.Status})");
                            table.Cell().Text(s.OpenedAt.ToString("HH:mm"));
                            table.Cell().Text(s.ClosedAt.HasValue ? s.ClosedAt.Value.ToString("HH:mm") : "-");
                            table.Cell().AlignRight().Text($"{s.OpeningFloat:N2}");
                            table.Cell().AlignRight().Text(s.ExpectedCash.HasValue ? $"{s.ExpectedCash:N2}" : "-");
                            table.Cell().AlignRight().Text(s.CountedCash.HasValue ? $"{s.CountedCash:N2}" : "-");
                            table.Cell().AlignRight().Text(s.Variance.HasValue ? $"{s.Variance:N2}" : "-");
                        }
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] RenderShiftClosing(ShiftZReportPdfModel model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(h =>
                        {
                            h.Item().Text(model.GymName).FontSize(14).Bold();
                            if (!string.IsNullOrWhiteSpace(model.GymNameAr))
                                h.Item().Text(model.GymNameAr).FontSize(11);
                            if (!string.IsNullOrWhiteSpace(model.GymCode))
                                h.Item().Text($"Gym code {Isolate(model.GymCode)}").FontSize(8);
                        });
                        row.RelativeItem().AlignRight().Column(h =>
                        {
                            h.Item().Text("Z-Report").FontSize(14).Bold();
                            h.Item().Text("Shift closing").FontSize(9);
                            h.Item().Text(model.StaffName).FontSize(11).Bold();
                            h.Item().Text(ShiftWindow(model)).FontSize(8);
                        });
                    });

                    column.Item().LineHorizontal(1);

                    if (model.IsFinal)
                    {
                        column.Item().Text(text =>
                        {
                            text.Span("Shift closed. This Z-Report is final and cannot be edited. ");
                            if (model.ClosedAt.HasValue)
                                text.Span($"Closed {model.ClosedAt.Value:dd MMM yyyy HH:mm} UTC.");
                        });
                    }

                    KvSection(column, "Shift", new[]
                    {
                        ("Shift", $"{model.StaffName} · {model.OpenedAt:dd MMM yyyy}"),
                        ("Staff", model.StaffName),
                        ("Opened at", model.OpenedAt.ToString("dd MMM yyyy HH:mm") + " UTC"),
                        ("Closed at", model.ClosedAt.HasValue ? model.ClosedAt.Value.ToString("dd MMM yyyy HH:mm") + " UTC" : "Open"),
                        ("Status", StatusLabel(model.Status))
                    });

                    KvSection(column, "Sales summary", new[]
                    {
                        ("Gross sales", Money(model.GrossSales, model.Currency)),
                        ("Discounts", Money(model.Discounts, model.Currency)),
                        ("Refunds", Money(model.Refunds, model.Currency)),
                        ("Net sales", Money(model.NetSales, model.Currency)),
                        ("Transactions", model.TransactionCount.ToString())
                    });

                    column.Item().Text("Payment breakdown").FontSize(11).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Method").Bold();
                            h.Cell().AlignCenter().Text("Count").Bold();
                            h.Cell().AlignRight().Text("Total").Bold();
                        });
                        if (model.Methods.Count == 0)
                        {
                            table.Cell().ColumnSpan(3).Text("None");
                        }
                        else
                        {
                            foreach (var m in model.Methods)
                            {
                                table.Cell().Text(MethodLabel(m.Method));
                                table.Cell().AlignCenter().Text(m.Count.ToString());
                                table.Cell().AlignRight().Text(Money(m.Total, model.Currency));
                            }
                        }
                    });

                    column.Item().Text("Cash reconciliation").FontSize(11).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                        });
                        void Row(string label, string value, bool bold = false)
                        {
                            table.Cell().Text(text => { if (bold) text.Span(label).Bold(); else text.Span(label); });
                            table.Cell().AlignRight().Text(text => { if (bold) text.Span(value).Bold(); else text.Span(value); });
                        }
                        Row("Opening cash", Money(model.OpeningCash, model.Currency));
                        Row("Cash sales", Money(model.CashSales, model.Currency));
                        Row("Cash refunds", Money(model.CashRefunds, model.Currency));
                        Row("Cash expenses", Money(model.CashExpenses, model.Currency));
                        if (model.CashPaidIn != 0) Row("Paid in", Money(model.CashPaidIn, model.Currency));
                        if (model.FloatAdjust != 0) Row("Float adjust", Money(model.FloatAdjust, model.Currency));
                        Row("Expected cash", model.RevealCash && model.ExpectedCash.HasValue ? Money(model.ExpectedCash.Value, model.Currency) : "—", true);
                        Row("Counted cash", model.RevealCash && model.CountedCash.HasValue ? Money(model.CountedCash.Value, model.Currency) : "—", true);
                        Row("Difference", model.RevealCash && model.Difference.HasValue ? Money(model.Difference.Value, model.Currency) : "—", true);
                    });

                    KvSection(column, "Sales breakdown", new[]
                    {
                        ("Memberships", $"{Money(model.Memberships, model.Currency)}  ({model.MembershipCount})"),
                        ("Renewals", $"{Money(model.Renewals, model.Currency)}  ({model.RenewalCount})"),
                        ("Products", $"{Money(model.Products, model.Currency)}  ({model.ProductCount})"),
                        ("Other", $"{Money(model.Other, model.Currency)}  ({model.OtherCount})")
                    });

                    KvSection(column, "Activity", new[]
                    {
                        ("Transactions", model.TransactionCount.ToString()),
                        ("Refunds", model.RefundCount.ToString()),
                        ("Discounts", model.DiscountCount.ToString())
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void KvSection(ColumnDescriptor column, string title, (string Label, string Value)[] rows)
    {
        column.Item().Text(title).FontSize(11).Bold();
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(2);
            });
            foreach (var (label, value) in rows)
            {
                table.Cell().Text(label);
                table.Cell().AlignRight().Text(value);
            }
        });
    }

    private static string ShiftWindow(ShiftZReportPdfModel m)
    {
        var opened = m.OpenedAt.ToString("dd MMM yyyy HH:mm");
        var closed = m.ClosedAt.HasValue ? m.ClosedAt.Value.ToString("HH:mm") : "open";
        return $"{opened} – {closed} UTC";
    }

    private static string StatusLabel(string status) => status switch
    {
        "open" => "Open",
        "approved" => "Approved",
        "closed" => "Closed",
        _ => status
    };

    private static string MethodLabel(string method) => method switch
    {
        "cash" => "Cash",
        "card_paymob" => "Card (Paymob)",
        "fawry" => "Fawry",
        "vodafone" => "Vodafone",
        "instapay" => "Instapay",
        "account_credit" => "Credit",
        _ => method
    };

    private static string Money(decimal n, string currency) => $"{n:N2} {currency}";

    private static string Isolate(string value) => $"{LeftToRightIsolate}{value}{PopDirectionalIsolate}";
}
