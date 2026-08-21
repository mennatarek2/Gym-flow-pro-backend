namespace GMS.Application.Validators.Inventory;

using FluentValidation;
using GMS.Application.DTOs.Inventory;

public class CreateProductCategoryRequestValidator : AbstractValidator<CreateProductCategoryRequest>
{
    public CreateProductCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
    }
}

public class UpdateProductCategoryRequestValidator : AbstractValidator<UpdateProductCategoryRequest>
{
    public UpdateProductCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
    }
}

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Barcode).MaximumLength(64).When(x => x.Barcode != null);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description != null);
        RuleFor(x => x.DescriptionAr).MaximumLength(500).When(x => x.DescriptionAr != null);
        RuleFor(x => x.Brand).MaximumLength(100).When(x => x.Brand != null);
        RuleFor(x => x.ImageUrl).MaximumLength(500).When(x => x.ImageUrl != null);
        RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(16);
        RuleFor(x => x.SellPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.VatRatePercent).InclusiveBetween(0, 100).When(x => x.VatRatePercent.HasValue);
        RuleFor(x => x.ReorderMinQty).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => !(x.TrackExpiry && !x.TrackBatch))
            .WithMessage("TrackExpiry requires TrackBatch / تتبع الصلاحية يتطلب تتبع التشغيلة");
        RuleFor(x => x)
            .Must(x => x.TrackStock || (!x.TrackBatch && !x.TrackExpiry))
            .WithMessage("Batch/expiry tracking requires TrackStock / تتبع التشغيلة والصلاحية يتطلب تتبع المخزون");
    }
}

public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Barcode).MaximumLength(64).When(x => x.Barcode != null);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description != null);
        RuleFor(x => x.DescriptionAr).MaximumLength(500).When(x => x.DescriptionAr != null);
        RuleFor(x => x.Brand).MaximumLength(100).When(x => x.Brand != null);
        RuleFor(x => x.ImageUrl).MaximumLength(500).When(x => x.ImageUrl != null);
        RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(16);
        RuleFor(x => x.SellPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.VatRatePercent).InclusiveBetween(0, 100).When(x => x.VatRatePercent.HasValue);
        RuleFor(x => x.ReorderMinQty).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => !(x.TrackExpiry && !x.TrackBatch))
            .WithMessage("TrackExpiry requires TrackBatch / تتبع الصلاحية يتطلب تتبع التشغيلة");
        RuleFor(x => x)
            .Must(x => x.TrackStock || (!x.TrackBatch && !x.TrackExpiry))
            .WithMessage("Batch/expiry tracking requires TrackStock / تتبع التشغيلة والصلاحية يتطلب تتبع المخزون");
    }
}
