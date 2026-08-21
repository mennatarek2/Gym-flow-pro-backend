namespace GMS.Application.Validators;

using FluentValidation;
using GMS.Application.DTOs.Attendance;

public class MemberSearchRequestValidator : AbstractValidator<MemberSearchRequest>
{
    public MemberSearchRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Search query is required / استعلام البحث مطلوب")
            .MinimumLength(2).WithMessage("Search query must be at least 2 characters / يجب أن يكون استعلام البحث حرفين على الأقل")
            .MaximumLength(100).WithMessage("Search query too long / استعلام البحث طويل جداً");
    }
}
