namespace GMS.Tests;

using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Invoices;
using GMS.Application.Services;
using GMS.Core.Models;
using GMS.Infrastructure.Services;

public class InvoiceDocumentHtmlBuilderTests
{
    private static InvoicePdfModel Sample(string? gymName = "Nile Fitness", string? logoUrl = null)
        => InvoicePdfModelFactory.FromDto(
            new InvoiceDto
            {
                InvoiceNumber = "INV-2026-000084",
                Type = "invoice",
                IssuedAt = new DateTime(2026, 8, 17, 14, 10, 0, DateTimeKind.Utc),
                MemberNameSnapshot = "Ahmed Mohamed",
                Lines = new List<InvoiceLineSnapshotDto>
                {
                    new() { Description = "Pre Workout", Qty = 1, UnitPrice = 30m, LineTotal = 30m }
                },
                Subtotal = 30m,
                DiscountAmount = 0m,
                VatRate = 0m,
                VatAmount = 0m,
                Total = 30m,
                Currency = "EGP",
                Status = "issued"
            },
            new TenantSettingsDto
            {
                GymName = gymName ?? "",
                GymNameAr = "نايل فتنس",
                LogoUrl = logoUrl,
                PhoneNumber = "01000000000",
                Address = "Cairo",
                PrimaryColor = "#7ACC00"
            },
            new TaxSettingsDto { TaxRegistrationNumber = "123-TAX" });

    [Fact]
    public void Thermal_UsesTenantName_NotHardcodedGym()
    {
        var html = InvoiceDocumentHtmlBuilder.Build(Sample("Nile Fitness"), InvoiceDocumentLayout.Thermal80mm);
        Assert.Contains("Nile Fitness", html);
        Assert.DoesNotContain("Power Gym", html);
        Assert.Contains("INV-2026-000084", html);
        Assert.Contains("80mm", html);
        Assert.Contains("Ahmed Mohamed", html);
        Assert.Contains("30.00 EGP", html);
    }

    [Fact]
    public void Standard_UsesTenantNameAndHidesMissingLogo()
    {
        var html = InvoiceDocumentHtmlBuilder.Build(Sample("Nile Fitness"), InvoiceDocumentLayout.StandardA4);
        Assert.Contains("Nile Fitness", html);
        Assert.Contains("INVOICE", html);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Thank you for choosing", html);
        Assert.Contains("@page{size:A4", html);
    }

    [Fact]
    public void WalkIn_ShowsWalkInCustomer()
    {
        var model = Sample();
        model.MemberName = "";
        var html = InvoiceDocumentHtmlBuilder.Build(model, InvoiceDocumentLayout.Thermal80mm);
        Assert.Contains("Walk-in Customer", html);
    }

    [Fact]
    public void MissingLogoUrl_DoesNotRenderBrokenImage()
    {
        var html = InvoiceDocumentHtmlBuilder.Build(Sample(logoUrl: "not-a-url"), InvoiceDocumentLayout.Thermal80mm);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PdfRenderer_BrandedModel_ProducesNonEmptyPdf()
    {
        var renderer = new InvoicePdfRenderer();
        var bytes = renderer.Render(Sample());
        Assert.True(bytes.Length > 0);
    }
}
