namespace api.Services.IServices;

public interface IUserService
{
	Task<Result> RegisterUserAsync(RegisterRequest dto);

	Task<Result<ResponseTypes.PagedResponse<UserDto>>> GetUsersPagedAsync(UserFilterDto filterDto);

	Task<Result<string>> ResetPasswordAsync(string userId);

	Task<Result> UpdatePasswordAsync(string id, string newPassword, string currentPassword);

	Task<Result> DeleteAnyUserAsync(string userId);

	Task<Result<string>> SaveAvatarAsync(IFormFile file, Guid userGuidId);

	Task<Result<ResponseTypes.UserFileResponse>> GetAvatarAsync(string key);

	Task<Result> RedactProfilePictureAsync(Guid userId, IFormFile file);

	Task<Result> DeleteAvatarAsync(Guid userId);

	Task<Result> UpdateUserAsync(UpdateUserRequest request, string? currentUsername, bool isAdmin);
	
	Task<Result> UpdateUserRoleAsync(UpdateRoleRequest updateRoleRequest, string changerEmail);

	Task<Result> DeleteUserByUsernameAsync(string username);

	Task<Result<UserDto>> GetUserByUsernameAsync(string targetUsername, string? userId);
}