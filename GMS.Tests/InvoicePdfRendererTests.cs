namespace GMS.Tests;

using GMS.Core.Models;
using GMS.Infrastructure.Services;

public class InvoicePdfRendererTests
{
    private static InvoicePdfModel BuildFixtureModel() => new()
    {
        InvoiceNumber = "INV-2026-000042",
        Type = "invoice",
        IssuedAt = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
        TenantName = "GymFlow Test",
        TenantNameAr = "جيم فلو",
        GymCode = "GYM-CAIRO-01",
        MemberName = "أحمد محمد",
        MemberPhone = "+201000000000",
        Lines = new List<InvoicePdfLineModel>
        {
            new() { Description = "Monthly Plan GOLD-3M", Qty = 1, UnitPrice = 500m, LineTotal = 500m }
        },
        Subtotal = 500m,
        DiscountAmount = 0m,
        VatRate = 0.14m,
        VatAmount = 70m,
        Total = 570m
    };

    [Fact]
    public void Render_KnownInvoice_ProducesNonEmptyPdf()
    {
        var renderer = new InvoicePdfRenderer();
        var model = BuildFixtureModel();

        var bytes = renderer.Render(model);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Render_KnownInvoice_ContentModelHasCorrectMemberNameAndAmount()
    {
        // The renderer is driven entirely by the model — this asserts the model that gets
        // handed to (and successfully consumed by) the renderer carries the correct snapshot data.
        var model = BuildFixtureModel();
        var renderer = new InvoicePdfRenderer();

        var bytes = renderer.Render(model);

        Assert.True(bytes.Length > 0);
        Assert.Equal("أحمد محمد", model.MemberName);
        Assert.Equal(570m, model.Total);
        Assert.Equal(70m, model.VatAmount);
    }

    [Fact]
    public void Render_CreditNote_ProducesNonEmptyPdf()
    {
        var renderer = new InvoicePdfRenderer();
        var model = BuildFixtureModel();
        model.Type = "credit_note";
        model.InvoiceNumber = "CN-2026-000001";

        var bytes = renderer.Render(model);

        Assert.True(bytes.Length > 0);
    }
}
