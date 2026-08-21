namespace GMS.Application.Services;

using System.Text.Json;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Invoices;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Models;

/// <summary>
/// Maps invoice + current Gym Identity onto <see cref="InvoicePdfModel"/>.
/// Financial fields come from the invoice snapshot; branding comes from the live tenant.
/// </summary>
public static class InvoicePdfModelFactory
{
    public static InvoicePdfModel FromDto(
        InvoiceDto invoice,
        TenantSettingsDto? settings,
        TaxSettingsDto? tax,
        PaymentReceiptInfoDto? payment = null)
    {
        var model = BaseFromInvoice(
            invoice.InvoiceNumber,
            invoice.Type,
            invoice.IssuedAt,
            invoice.MemberNameSnapshot,
            invoice.MemberPhoneSnapshot,
            invoice.Lines.Select(l => new InvoicePdfLineModel
            {
                Description = l.Description,
                DescriptionAr = l.DescriptionAr,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                LineTotal = l.LineTotal
            }).ToList(),
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.VatRate,
            invoice.VatAmount,
            invoice.Total,
            invoice.Currency,
            invoice.Status);

        ApplyGymIdentity(model, settings, tax);
        ApplyPayment(model, payment);
        return model;
    }

    public static InvoicePdfModel FromEntity(Invoice invoice, Tenant? tenant, PaymentReceiptInfoDto? payment = null)
    {
        var lines = string.IsNullOrWhiteSpace(invoice.LinesSnapshot)
            ? new List<InvoicePdfLineModel>()
            : JsonSerializer.Deserialize<List<InvoicePdfLineModel>>(invoice.LinesSnapshot) ?? new List<InvoicePdfLineModel>();

        var model = BaseFromInvoice(
            invoice.InvoiceNumber,
            invoice.Type,
            invoice.IssuedAt,
            invoice.MemberNameSnapshot,
            invoice.MemberPhoneSnapshot,
            lines,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.VatRate,
            invoice.VatAmount,
            invoice.Total,
            invoice.Currency,
            invoice.Status);

        if (tenant != null)
        {
            model.TenantName = string.IsNullOrWhiteSpace(tenant.Name) ? "GymFlowPro Gym" : tenant.Name.Trim();
            model.TenantNameAr = tenant.NameAr?.Trim() ?? string.Empty;
            model.GymCode = tenant.GymCode ?? string.Empty;
            model.LogoUrl = string.IsNullOrWhiteSpace(tenant.LogoUrl) ? null : tenant.LogoUrl.Trim();
            model.PhoneNumber = NullIfBlank(tenant.PhoneNumber);
            model.Email = NullIfBlank(tenant.Email);
            model.Address = NullIfBlank(tenant.Address);
            model.ShortName = GetSettingString(tenant.Settings, TenantSettingsKeys.ShortName);
            model.Website = GetSettingString(tenant.Settings, TenantSettingsKeys.Website);
            model.TaxRegistrationNumber = GetSettingString(tenant.Settings, TenantSettingsKeys.TaxRegistrationNumber);
            model.FooterText = GetSettingString(tenant.Settings, TenantSettingsKeys.InvoiceFooterText);
            model.FooterTextAr = GetSettingString(tenant.Settings, TenantSettingsKeys.InvoiceFooterTextAr);
            model.PrimaryColor = AccessCardHtmlBuilder.NormalizeHex(
                                     GetSettingString(tenant.Settings, TenantSettingsKeys.BrandPrimaryColor))
                                 ?? BrandingDefaults.PrimaryColor;
            model.AccentColor = AccessCardHtmlBuilder.NormalizeHex(
                                    GetSettingString(tenant.Settings, TenantSettingsKeys.BrandAccentColor))
                                ?? BrandingDefaults.AccentColor;
        }

        model.BillerCodeLabel = "Gym Code";
        model.CustomerLabel = "Member";
        model.CustomerLabelAr = "العضو";
        ApplyPayment(model, payment);
        return model;
    }

    public static void AttachLogo(InvoicePdfModel model, byte[]? bytes, string? logoUrl)
    {
        if (bytes == null || bytes.Length == 0) return;
        model.LogoImageBytes = bytes;
        model.LogoDataUri = ToDataUri(logoUrl, bytes);
    }

    private static InvoicePdfModel BaseFromInvoice(
        string number,
        string type,
        DateTime issuedAt,
        string memberName,
        string memberPhone,
        List<InvoicePdfLineModel> lines,
        decimal subtotal,
        decimal discount,
        decimal vatRate,
        decimal vatAmount,
        decimal total,
        string currency,
        string status) => new()
    {
        InvoiceNumber = number,
        Type = type,
        IssuedAt = issuedAt,
        MemberName = memberName ?? string.Empty,
        MemberPhone = memberPhone ?? string.Empty,
        Lines = lines,
        Subtotal = subtotal,
        DiscountAmount = discount,
        VatRate = vatRate,
        VatAmount = vatAmount,
        Total = total,
        Currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency,
        Status = string.IsNullOrWhiteSpace(status) ? "issued" : status,
        BillerCodeLabel = "Gym Code",
        CustomerLabel = "Member",
        CustomerLabelAr = "العضو"
    };

    private static void ApplyGymIdentity(InvoicePdfModel model, TenantSettingsDto? settings, TaxSettingsDto? tax)
    {
        if (settings != null)
        {
            model.TenantName = string.IsNullOrWhiteSpace(settings.GymName) ? "GymFlowPro Gym" : settings.GymName.Trim();
            model.TenantNameAr = settings.GymNameAr?.Trim() ?? string.Empty;
            model.GymCode = settings.GymCode ?? string.Empty;
            model.ShortName = NullIfBlank(settings.ShortName);
            model.LogoUrl = NullIfBlank(settings.LogoUrl);
            model.PhoneNumber = NullIfBlank(settings.PhoneNumber);
            model.Email = NullIfBlank(settings.Email);
            model.Address = NullIfBlank(settings.Address);
            model.Website = NullIfBlank(settings.Website);
            model.PrimaryColor = AccessCardHtmlBuilder.NormalizeHex(settings.PrimaryColor) ?? BrandingDefaults.PrimaryColor;
            model.AccentColor = AccessCardHtmlBuilder.NormalizeHex(settings.AccentColor) ?? BrandingDefaults.AccentColor;
        }

        if (tax != null)
        {
            model.TaxRegistrationNumber = NullIfBlank(tax.TaxRegistrationNumber);
            model.FooterText = NullIfBlank(tax.InvoiceFooterText);
            model.FooterTextAr = NullIfBlank(tax.InvoiceFooterTextAr);
        }
    }

    private static void ApplyPayment(InvoicePdfModel model, PaymentReceiptInfoDto? payment)
    {
        if (payment == null) return;
        model.PaymentAmount = payment.Amount;
        model.PaidAt = payment.PaidAtUtc;
        model.PaymentMethod = NullIfBlank(payment.Method);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetSettingString(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToDataUri(string? logoUrl, byte[] bytes)
    {
        var ext = Path.GetExtension(logoUrl ?? string.Empty).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
