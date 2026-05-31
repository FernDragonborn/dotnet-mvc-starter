using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Security.Cryptography;
using api.Identity;
using api.Services.IServices;
using api.Services.Storage;
using api.Utils;
using AutoMapper;
using static api.DTOs.ResponseTypes;

// ReSharper disable ClassWithVirtualMembersNeverInherited.Global

namespace api.Services;

public class UserService(IUnitOfWork unitOfWork, IMapper mapper, IFileStorage fileStorage) : IUserService
{
	private const string PasswordSymbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
	private const long MaxAvatarBytes = 5 * 1024 * 1024;
	private static readonly string[] AllowedAvatarExtensions = [".jpg", ".jpeg", ".png", ".webp"];

	public virtual async Task<Result> RegisterUserAsync(RegisterRequest dto)
	{
		RegisterRequestValidator validator = new();
		var validationResult = await validator.ValidateAsync(dto);
		if (!validationResult.IsValid)
		{
			var errorMsg = string.Join("; \n", validationResult.Errors);
			return Result.Fail<TokensResponse>(errorMsg);
		}

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(x => x.Email == dto.Email);
		if (userFetchRes.IsSuccess)
			return Result.Fail<TokensResponse>("This email is already taken.");

		var usernameFetchRes = await unitOfWork.UserRepository.GetOneAsync(x => x.Username == dto.Username);
		if (usernameFetchRes.IsSuccess)
			return Result.Fail<TokensResponse>("This username is already taken.");

		User user = new(BCrypt.Net.BCrypt.GenerateSalt())
		{
			Email = dto.Email,
			Username = dto.Username,
			Role = UserRole.User,
		};
		user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, user.PasswordSalt);

		var res = await unitOfWork.UserRepository.AddAsync(user);
		if (res.IsFailure) return Result.Fail<TokensResponse>(res.Error);

		return Result.Ok();
	}

	public async Task<Result<UserDto>> GetUserByUsernameAsync(string targetUsername, string? userId)
	{
		var fetchTargetRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == targetUsername, "Followers,Following");
		if (fetchTargetRes.IsFailure)
			return Result.Fail<UserDto>($"User @\'{targetUsername}\' was not found");

		var dto = mapper.Map<UserDto>(fetchTargetRes.Value);

		return Result.Ok(dto);
	}

	[SuppressMessage("Performance", "CA1862:Use the \'StringComparison\' method overloads to perform case-insensitive string comparisons")]
	public async Task<Result<PagedResponse<UserDto>>> GetUsersPagedAsync(UserFilterDto filterDto)
	{
		Expression<Func<User, bool>>? filter = null;

		if (!string.IsNullOrWhiteSpace(filterDto.SearchTerm))
		{
			var term = filterDto.SearchTerm.ToLower().Trim();
			filter = u => u.Username.ToLower().Contains(term)
			              || u.Email.ToLower().Contains(term);
		}

		var pagedUsersResult = await unitOfWork.UserRepository.GetPagedAsync(
			filter,
			q => q.OrderByDescending(u => u.CreatedAt),
			filterDto.PageNumber,
			filterDto.PageSize
		);

		if (pagedUsersResult.IsFailure)
			return Result.Fail<PagedResponse<UserDto>>(pagedUsersResult.Error);

		var (users, totalCount) = pagedUsersResult.Value;
		var userDtos = mapper.Map<List<UserDto>>(users);

		var resultDto = new PagedResponse<UserDto>(
			userDtos,
			totalCount,
			filterDto.PageNumber,
			filterDto.PageSize
		);

		return Result.Ok(resultDto);
	}

	public async Task<Result> RedactProfilePictureAsync(Guid userId, IFormFile file)
	{
		var saveRes = await SaveAvatarAsync(file, userId);
		return saveRes.IsFailure ? Result.Fail(saveRes.Error) : Result.Ok();
	}

	public virtual async Task<Result<string>> SaveAvatarAsync(IFormFile file, Guid userGuidId)
	{
		if (file is null || file.Length == 0) return Result.Fail<string>("File for ProfilePic cannot be empty");
		if (file.Length > MaxAvatarBytes) return Result.Fail<string>("File too large. Max 5 MB.");

		var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (!AllowedAvatarExtensions.Contains(extension))
			return Result.Fail<string>("Invalid file type. Allowed: jpg, jpeg, png, webp.");

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == userGuidId);
		if (userFetchRes.IsFailure) return Result.Fail<string>("User not found.");

		await using var inputStream = file.OpenReadStream();
		if (!await IsValidImageMagicBytesAsync(inputStream, extension))
			return Result.Fail<string>("File content does not match expected image format.");
		inputStream.Position = 0;

		// Remove old avatars first to avoid orphan files/rows
		var existingRes = await unitOfWork.UserImageRepository.GetAsync(img => img.UserId == userGuidId);
		if (existingRes.IsSuccess)
		{
			foreach (var old in existingRes.Value)
			{
				await fileStorage.DeleteAsync(old.FileName);
				await unitOfWork.UserImageRepository.Remove(old);
			}
		}

		var key = $"avatars/{userGuidId}/{Guid.NewGuid()}{extension}";
		var contentType = ContentTypeFromExtension(extension);

		var saveRes = await fileStorage.SaveAsync(key, inputStream, contentType);
		if (saveRes.IsFailure) return Result.Fail<string>(saveRes.Error);

		var userImage = new UserImage(userGuidId, key, DateTime.UtcNow);
		var entityRes = await unitOfWork.UserImageRepository.AddAsync(userImage);
		if (entityRes.IsFailure) return Result.Fail<string>(entityRes.Error);

		userFetchRes.Value.ProfilePicUrl = key;
		await unitOfWork.UserRepository.Update(userFetchRes.Value);

		return Result.Ok(key);
	}

	public async Task<Result> DeleteAvatarAsync(Guid userId)
	{
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == userId);
		if (userFetchRes.IsFailure)
			return Result.Fail("User not found");

		var imageRes = await unitOfWork.UserImageRepository.GetAsync(img => img.UserId == userId);
		if (imageRes.IsFailure || imageRes.Value.Count == 0)
			return Result.Fail("User does not have an avatar");

		foreach (var img in imageRes.Value)
		{
			await fileStorage.DeleteAsync(img.FileName);
			await unitOfWork.UserImageRepository.Remove(img);
		}

		var user = userFetchRes.Value;
		user.ProfilePicUrl = null;
		await unitOfWork.UserRepository.Update(user);

		return Result.Ok();
	}

	private static string ContentTypeFromExtension(string ext) => ext switch
	{
		".jpg" or ".jpeg" => "image/jpeg",
		".png" => "image/png",
		".webp" => "image/webp",
		_ => "application/octet-stream"
	};

	private static async Task<bool> IsValidImageMagicBytesAsync(Stream stream, string ext)
	{
		var header = new byte[12];
		var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
		if (read < 4) return false;

		return ext switch
		{
			".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
			".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
			          && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
			".webp" => read >= 12
			           && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
			           && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
			_ => false
		};
	}

	public async Task<Result> UpdateUserAsync(UpdateUserRequest request, string? currentUsername, bool isAdmin)
	{
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == currentUsername);
		if (userFetchRes.IsFailure)
		{
			if (!isAdmin)
				return Result.Fail($"User '{currentUsername}' was not found.");
			if (string.IsNullOrWhiteSpace(request.Username))
				return Result.Fail("Username is required to locate target user.");
			userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == request.Username);
			if (userFetchRes.IsFailure)
				return Result.Fail($"User '{request.Username}' was not found.");
		}

		var isNotSameUser = currentUsername != userFetchRes.Value.Username;
		if (isNotSameUser && !isAdmin)
			return Result.Fail("Insufficient permissions to edit this profile.");

		if (!string.IsNullOrWhiteSpace(request.Email) &&
		    !string.Equals(request.Email, userFetchRes.Value.Email, StringComparison.OrdinalIgnoreCase))
		{
			var emailTaken = await unitOfWork.UserRepository.GetOneAsync(u => u.Email == request.Email);
			if (emailTaken.IsSuccess)
				return Result.Fail("This email is already in use.");
		}

		if (!string.IsNullOrWhiteSpace(request.Username) &&
		    !string.Equals(request.Username, userFetchRes.Value.Username, StringComparison.OrdinalIgnoreCase))
		{
			var usernameTaken = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == request.Username);
			if (usernameTaken.IsSuccess)
				return Result.Fail("This username is already taken.");
		}

		var userToUpdate = userFetchRes.Value;
		if (!string.IsNullOrWhiteSpace(request.Username)) userToUpdate.Username = request.Username;
		if (!string.IsNullOrWhiteSpace(request.Email)) userToUpdate.Email = request.Email;
		if (request.DisplayName is not null) userToUpdate.DisplayName = request.DisplayName;
		if (request.Gender.HasValue) userToUpdate.Gender = request.Gender.Value;

		await unitOfWork.UserRepository.Update(userToUpdate);
		return Result.Ok();
	}

	public async Task<Result> UpdateUserRoleAsync(UpdateRoleRequest updateRoleRequest, string changerEmail)
	{
		if (string.IsNullOrWhiteSpace(updateRoleRequest.TargetUsername))
			return Result.Fail("Target`s username was null, empty or whitespace");
		
		if (!Enum.IsDefined(updateRoleRequest.NewRole))
			return Result.Fail($"Role with id \'{(int)updateRoleRequest.NewRole}\' does not exist in the system.");
		
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == updateRoleRequest.TargetUsername);
		if (userFetchRes.IsFailure)
			return Result.Fail($"User with email \'{updateRoleRequest.TargetUsername}\' was not found.");

		if (userFetchRes.Value.Email.Equals(changerEmail))
			return Result.Fail("You cannot change your own role. (Self-destruct protection).");
		
		if (userFetchRes.Value.Role.Equals(updateRoleRequest.NewRole))
			return Result.Ok();
		
		var userToUpdate = userFetchRes.Value;
		userToUpdate.Role = updateRoleRequest.NewRole;
		
		await unitOfWork.UserRepository.Update(userToUpdate);
		return Result.Ok();
	}
	
	public async Task<Result> DeleteUserByUsernameAsync(string username)
	{
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == username);
		if (userFetchRes.IsFailure)
			return Result.Fail($"User {username} was not found");

		await unitOfWork.UserRepository.Remove(userFetchRes.Value);
		return Result.Ok();
	}
	
	public virtual async Task<Result<UserFileResponse>> GetAvatarAsync(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
			return Result.Fail<UserFileResponse>("File name cannot be empty.");

		var extension = Path.GetExtension(key).ToLowerInvariant();
		if (!AllowedAvatarExtensions.Contains(extension))
			return Result.Fail<UserFileResponse>("Invalid file type.");

		var fetchRes = await fileStorage.GetAsync(key);
		if (fetchRes.IsFailure)
			return Result.Fail<UserFileResponse>(fetchRes.Error);

		await using var stream = fetchRes.Value.Content;
		using var ms = new MemoryStream();
		await stream.CopyToAsync(ms);

		return Result.Ok(new UserFileResponse(ms.ToArray(), fetchRes.Value.ContentType));
	}

	public virtual async Task<Result<string>> ResetPasswordAsync(string userId)
	{
		var isSuccess = Guid.TryParse(userId, out var guid);
		if (!isSuccess) return Result.Fail<string>("Id is not a Guid formatted");

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == guid);
		if (userFetchRes.IsFailure) return Result.Fail<string>($"User with ID \"{userId}\" doesn't exist");

		userFetchRes.Value.PasswordSalt = BCrypt.Net.BCrypt.GenerateSalt();
		var generatedPassword = RandomNumberGenerator.GetString(
			PasswordSymbols,
			10);
		userFetchRes.Value.PasswordHash = BCrypt.Net.BCrypt.HashPassword(generatedPassword, userFetchRes.Value.PasswordSalt);

		await unitOfWork.UserRepository.Update(userFetchRes.Value);

		return Result.Ok(generatedPassword);
	}

	public virtual async Task<Result> UpdatePasswordAsync(string id, string newPassword, string currentPassword)
	{
		if (string.IsNullOrEmpty(id)) return Result.Fail($"{nameof(id)} was null or empty");

		var isSuccess = Guid.TryParse(id, out var guid);
		if (!isSuccess) return Result.Fail("Id is not a Guid formatted");

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == guid);
		if (userFetchRes.IsFailure) return Result.Fail($"User with ID \"{id}\" doesn't exist");
		if (AuthService.NotCorrectPassword(userFetchRes.Value, currentPassword))
			return Result.Fail("Current password is incorrect.");

		userFetchRes.Value.PasswordSalt = BCrypt.Net.BCrypt.GenerateSalt();
		userFetchRes.Value.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, userFetchRes.Value.PasswordSalt);

		await unitOfWork.UserRepository.Update(userFetchRes.Value);

		return Result.Ok();
	}

	public virtual async Task<Result> DeleteAnyUserAsync(string userId)
	{
		var resGuid = GuidParser.TryParseGuid(userId);
		if (!resGuid.IsSuccess) return Result.Fail(resGuid.Error);

		var userToDeleteFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == resGuid.Value);
		if (userToDeleteFetchRes.IsFailure) return Result.Fail($"User with Id: \"{userId}\" doesn't exist");

		if (userToDeleteFetchRes.Value.Role == IdentityData.ClaimAdmin) return Result.Fail("can't delete admin");

		await unitOfWork.UserRepository.Remove(userToDeleteFetchRes.Value);

		return Result.Ok();
	}
}