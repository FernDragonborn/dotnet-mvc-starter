using FluentValidation;

namespace api.Validators;

public class UserDtoValidator : AbstractValidator<UserDto>
{
    public UserDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .Length(1, 40).WithMessage("Username must be between 3 and 32 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");

        // If you require password for this DTO — keep this rule.
        // If only for registration: wrap into a condition.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid role.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Invalid gender.");

        // Optional image validation
        When(x => x.ProfilePic is not null, () =>
        {
            RuleFor(x => x.ProfilePic!.Length)
                .LessThanOrEqualTo(1024 * 100) // 100 кб
                .WithMessage("Profile picture file must be less than 5MB.");

            RuleFor(x => x.ProfilePic!.ContentType)
                .Must(ct => ct is "image/jpeg" or "image/png")
                .WithMessage("Only JPEG and PNG images are allowed.");
        });
    }
}