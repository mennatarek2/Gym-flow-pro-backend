namespace GMS.Application.Validators.Inventory;

using FluentValidation;
using GMS.Application.DTOs.Inventory;

public class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32)
            .Matches(@"^[A-Za-z0-9_-]+$")
            .WithMessage("Code must be alphanumeric (underscore/hyphen allowed) / كود المخزن أحرف وأرقام فقط");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
    }
}

public class UpdateWarehouseRequestValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150).When(x => x.NameAr != null);
    }
}
