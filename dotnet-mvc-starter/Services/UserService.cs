using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Security.Cryptography;
using api.Identity;
using api.Services.IServices;
using api.Utils;
using AutoMapper;
using dotenv.net;
using static api.DTOs.ResponseTypes;

// ReSharper disable ClassWithVirtualMembersNeverInherited.Global

namespace api.Services;

public class UserService(IUnitOfWork unitOfWork, IMapper mapper) : IUserService
{
	private const string PasswordSymbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
	private readonly IDictionary<string, string> _env = DotEnv.Read();

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
			return Result.Fail<TokensResponse>("Ця email-адреса вже зайнята");

		var usernameFetchRes = await unitOfWork.UserRepository.GetOneAsync(x => x.Username == dto.Username);
		if (usernameFetchRes.IsSuccess)
			return Result.Fail<TokensResponse>("Цей нікнейм вже зайнятий");

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

	public async Task<Result> RedactProfilePictureAsync(ProfilePictureDto pictureDto)
	{
		if (pictureDto.ProfilePic is null)
			return Result.Fail("File for ProfilePic cannot be null");

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Email == pictureDto.Email);
		if (userFetchRes.IsFailure)
			return Result.Fail($"User {pictureDto.Email} was not found");

		// Генеруємо шлях тут або використовуємо той самий Utils клас
		var path = StaticDetails.GetUserProfileImagePath(userFetchRes.Value.Id);

		// Викликаємо внутрішній метод збереження
		return await SaveAvatarAsync(pictureDto.ProfilePic, userFetchRes.Value.Id, path);
	}

	public virtual async Task<Result<string>> SaveAvatarAsync(IFormFile file, Guid userGuidId, string fileStoragePath)
	{
		if (string.IsNullOrEmpty(fileStoragePath)) return Result.Fail<string>("File storage path is not set.");

		// Ця перевірка трохи надлишкова, якщо ми викликаємо з RedactProfilePictureAsync, але не завадить
		var userFetchRes = await unitOfWork.UserRepository.GetAsync(u => u.Id == userGuidId);
		if (userFetchRes.IsFailure) return Result.Fail<string>("User not found.");

		var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), fileStoragePath);

		if (!Directory.Exists(uploadPath))
			Directory.CreateDirectory(uploadPath);

		string[] allowedExtensions = [".jpg", ".jpeg", ".png"];
		var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

		if (!allowedExtensions.Contains(extension)) return Result.Fail<string>("Invalid file type.");

		var fileName = Guid.NewGuid() + extension;
		var filePath = Path.Combine(uploadPath, fileName);

		await using (var stream = new FileStream(filePath, FileMode.Create))
		{
			await file.CopyToAsync(stream);
		}

		var userImage = new UserImage(userGuidId, fileName, DateTime.UtcNow);

		var entityRes = await unitOfWork.UserImageRepository.AddAsync(userImage);
		if (entityRes.IsFailure) return Result.Fail<string>(entityRes.Error);

		// Update user's profile pic URL
		var userForPicUpdate = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == userGuidId);
		if (userForPicUpdate.IsSuccess)
		{
			userForPicUpdate.Value.ProfilePicUrl = fileName;
			await unitOfWork.UserRepository.Update(userForPicUpdate.Value);
		}

		return Result.Ok(entityRes.Value.FileName);
	}

	public async Task<Result> DeleteAvatarAsync(Guid userId)
	{
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Id == userId);
		if (userFetchRes.IsFailure)
			return Result.Fail("User not found");

		var imageRes = await unitOfWork.UserImageRepository.GetAsync(img => img.UserId == userId);
		if (imageRes.IsFailure || imageRes.Value.Count == 0)
			return Result.Fail("User does not have an avatar");

		var latestImage = imageRes.Value.OrderByDescending(i => i.UploadedAt).First();

		// Delete file from disk
		var filePath = Path.Combine(Directory.GetCurrentDirectory(), StaticDetails.UserProfileImagePath, latestImage.FileName);
		if (File.Exists(filePath))
			File.Delete(filePath);

		// Remove DB record
		await unitOfWork.UserImageRepository.Remove(latestImage);

		// Clear profile pic URL
		var user = userFetchRes.Value;
		user.ProfilePicUrl = null;
		await unitOfWork.UserRepository.Update(user);

		return Result.Ok();
	}

	public async Task<Result> UpdateUserAsync(UserDto userDto, string? currentPrincipalEmail, bool isAdmin)
	{
		// Look up by current JWT identity (username stored in ClaimTypes.Name), not the submitted username.
		// This allows username changes — the old name is used to find the record.
		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == currentPrincipalEmail);
		if (userFetchRes.IsFailure)
		{
			// Admin path: fall back to submitted username if JWT identity not found
			if (!isAdmin)
				return Result.Fail($"User \'{currentPrincipalEmail}\' was not found.");
			userFetchRes = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == userDto.Username);
			if (userFetchRes.IsFailure)
				return Result.Fail($"User \'{userDto.Username}\' was not found.");
		}

		var isNotSameUser = currentPrincipalEmail != userFetchRes.Value.Username;
		if (isNotSameUser && !isAdmin)
			return Result.Fail("Недостатньо прав для редагування цього профілю.");

		if (userFetchRes.Value.Role != userDto.Role)
			return Result.Fail("Зміна ролі неможлива через цей маршрут.");

		// Check email uniqueness if it is being changed
		if (!string.IsNullOrWhiteSpace(userDto.Email) &&
		    !string.Equals(userDto.Email, userFetchRes.Value.Email, StringComparison.OrdinalIgnoreCase))
		{
			var emailTaken = await unitOfWork.UserRepository.GetOneAsync(u => u.Email == userDto.Email);
			if (emailTaken.IsSuccess)
				return Result.Fail("Цей email вже використовується.");
		}

		// Check username uniqueness if it is being changed
		if (!string.IsNullOrWhiteSpace(userDto.Username) &&
		    !string.Equals(userDto.Username, userFetchRes.Value.Username, StringComparison.OrdinalIgnoreCase))
		{
			var usernameTaken = await unitOfWork.UserRepository.GetOneAsync(u => u.Username == userDto.Username);
			if (usernameTaken.IsSuccess)
				return Result.Fail("Цей нікнейм вже зайнятий.");
		}

		var userToUpdate = userFetchRes.Value;
		var savedHash = userToUpdate.PasswordHash;
		var savedSalt = userToUpdate.PasswordSalt;

		mapper.Map(userDto, userToUpdate);

		// Password hash/salt must never be overwritten via profile update
		userToUpdate.PasswordHash = savedHash;
		userToUpdate.PasswordSalt = savedSalt;

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
	
	/// <returns>response dto with JWT pair</returns>
	public virtual async Task<Result<UserFileResponse>> GetAvatarAsync(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
			return Result.Fail<UserFileResponse>("Ім'я файлу не може бути порожнім.");

		string[] allowedExtensions = [".jpg", ".jpeg", ".png"];
		var extension = Path.GetExtension(fileName).ToLowerInvariant();

		var notAllowedExtension = !allowedExtensions.Contains(extension);
		if (notAllowedExtension)
			return Result.Fail<UserFileResponse>("Invalid file type.");

		// Запобігання атакам шляхового обходу
		if (fileName.Contains(".."))
			return Result.Fail<UserFileResponse>("Невірний формат імені файлу.");

		var filePath = Path.Combine(_env["FILE_STORAGE_PATH"], fileName);

		if (!File.Exists(filePath))
			return Result.Fail<UserFileResponse>("Файл не знайдено.");

		// Визначаємо MIME-тип файлу
		var extension1 = Path.GetExtension(filePath).ToLowerInvariant();
		var contentType = extension1 switch
		{
			".jpg" or ".jpeg" => "image/jpeg",
			".png" => "image/png",
			".gif" => "image/gif",
			".bmp" => "image/bmp",
			".webp" => "image/webp",
			_ => "application/octet-stream",
		};

		var bytes = await File.ReadAllBytesAsync(filePath);

		return Result.Ok(new UserFileResponse(bytes, contentType));
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
			return Result.Fail("Невірний поточний пароль.");

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