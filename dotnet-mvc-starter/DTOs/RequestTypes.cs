namespace api.DTOs;

public abstract record RequestTypes
{
	public record RegisterRequest(
		string Email,
		string Username,
		string Password,
		string ConfirmPassword
	);

	public record LoginRequest(
		string Email,
		string Username,
		string Password
	);

	public record UpdateRoleRequest(
		string TargetUsername,
		UserRole NewRole
	);
	
	public record UpdatePasswordRequest(
		string CurrentPassword,
		string NewPassword,
		string NewPasswordConfirmation);

	public record ResetPasswordRequest(string UserId);

	public record RenewTokenRequest(string RefreshToken);

	public record UpdateUserRequest(
		string? Username,
		string? Email,
		string? DisplayName,
		Gender? Gender
	);
	
	public class UserFilterDto
	{
		public string? SearchTerm { get; set; } // Пошук по імені/email
		public int PageNumber { get; set; } = 1;
		public int PageSize { get; set; } = 10;
	}
}