using FluentValidation;

namespace api.Utils;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
	private static readonly HashSet<string> ReservedUsernames =
	[
		"admin", "administrator", "moderator", "mod", "system", "support", "root"
	];

	public RegisterRequestValidator()
	{
		RuleFor(x => x.Email)
			.NotEmpty()
			.WithMessage("Email cannot be empty.")
			.EmailAddress()
			.WithMessage("Invalid email format.");

		RuleFor(x => x.Username)
			.NotEmpty()
			.WithMessage("Username cannot be empty.")
			.Must(username => !ReservedUsernames.Contains(username.ToLowerInvariant()))
			.WithMessage("This username is reserved. Please choose another.");

		RuleFor(x => x.Password)
			.NotEmpty()
			.WithMessage("Password cannot be empty.")
			.MinimumLength(8)
			.WithMessage("Password must be at least 8 characters long.")
			.Matches(@"[a-zA-Z]")
			.WithMessage("Password must contain at least one letter.")
			.Matches(@"\d")
			.WithMessage("Password must contain at least one digit.");

		RuleFor(x => x.ConfirmPassword)
			.NotEmpty()
			.WithMessage("Password confirmation cannot be empty.")
			.Equal(x => x.Password)
			.WithMessage("Passwords do not match.");
	}
}
