namespace api.Services.IServices;

public interface IUserService
{
	Task<Result> RegisterUserAsync(RegisterRequest dto);

	Task<Result<ResponseTypes.PagedResponse<UserDto>>> GetUsersPagedAsync(UserFilterDto filterDto);

	Task<Result<string>> ResetPasswordAsync(string userId);

	Task<Result> UpdatePasswordAsync(string id, string newPassword, string currentPassword);

	Task<Result> DeleteAnyUserAsync(string userId);

	Task<Result<string>> SaveAvatarAsync(IFormFile file, Guid userGuidId, string fileStoragePath);

	Task<Result<ResponseTypes.UserFileResponse>> GetAvatarAsync(string fileName);

	Task<Result> RedactProfilePictureAsync(ProfilePictureDto pictureDto);

	Task<Result> DeleteAvatarAsync(Guid userId);

	Task<Result> UpdateUserAsync(UserDto userDto, string? currentPrincipalEmail, bool isAdmin);
	
	Task<Result> UpdateUserRoleAsync(UpdateRoleRequest updateRoleRequest, string changerEmail);

	Task<Result> DeleteUserByUsernameAsync(string username);

	Task<Result<UserDto>> GetUserByUsernameAsync(string targetUsername, string? userId);
}