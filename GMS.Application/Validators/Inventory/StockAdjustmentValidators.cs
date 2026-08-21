namespace GMS.Application.Validators.Inventory;

using FluentValidation;
using GMS.Application.DTOs.Inventory;
using GMS.Core.Constants;

public class CreateStockAdjustmentRequestValidator : AbstractValidator<CreateStockAdjustmentRequest>
{
    public CreateStockAdjustmentRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ReasonCode).NotEmpty()
            .Must(r => StockAdjustmentReasonCodes.All.Contains(r))
            .WithMessage("Invalid reason code / سبب غير صالح");
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note != null);
        RuleFor(x => x.Note).NotEmpty()
            .When(x => string.Equals(x.ReasonCode, StockAdjustmentReasonCodes.Other, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Note is required for reason 'other' / الملاحظة مطلوبة لسبب «أخرى»");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.QtyDelta).NotEqual(0);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).When(l => l.UnitCost.HasValue);
        });
    }
}
