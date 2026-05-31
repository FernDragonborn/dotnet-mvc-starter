using api.Services.IServices;
using api.Utils;

// ReSharper disable ClassWithVirtualMembersNeverInherited.Global

namespace api.Services;

public class AuthService(IUnitOfWork unitOfWork) : IAuthService
{
	private const string WrongLoginOrPasswordStr = "Invalid username/email or password.";
	public virtual async Task<Result<ResponseTypes.TokensResponse>> LoginAsync(LoginRequest dto)
	{
		if (string.IsNullOrEmpty(dto.Password)
		    || string.IsNullOrEmpty(dto.Email)
		    && string.IsNullOrEmpty(dto.Username))
			return Result.Fail<ResponseTypes.TokensResponse>(WrongLoginOrPasswordStr);

		var userFetchRes = await unitOfWork.UserRepository.GetOneAsync(x => x.Email.Equals(dto.Email) || x.Username.Equals(dto.Username));

		if (userFetchRes.IsFailure
		    || NotCorrectPassword(userFetchRes.Value, dto.Password))
			return Result.Fail<ResponseTypes.TokensResponse>(WrongLoginOrPasswordStr);

		ResponseTypes.TokensResponse response = new(
			JwtHandler.GenerateAccessToken(userFetchRes.Value),
			JwtHandler.GenerateRefreshToken(userFetchRes.Value)
		);

		return Result.Ok(response);
	}

	public virtual async Task<Result<ResponseTypes.TokensResponse>> RenewTokenAsync(string jwtToken)
	{
		if (string.IsNullOrEmpty(jwtToken))
			return Result.Fail<ResponseTypes.TokensResponse>($"{nameof(jwtToken)} was null or empty.");

		var guidRes = JwtHandler.ValidateRefreshTokenAndGetId(jwtToken);
		if (guidRes.IsFailure) return Result.Fail<ResponseTypes.TokensResponse>(guidRes.Error);

		var userRes = await unitOfWork.UserRepository.GetOneAsync(x => x.Id == guidRes.Value);
		if (userRes.IsFailure) return Result.Fail<ResponseTypes.TokensResponse>($"no entry of {nameof(User)} with provided id found in database.");

		ResponseTypes.TokensResponse response = new(
			JwtHandler.GenerateAccessToken(userRes.Value),
			JwtHandler.GenerateRefreshToken(userRes.Value)
		);

		return Result.Ok(response);
	}

	public static bool NotCorrectPassword(User user, string password) =>
		!user.PasswordHash
			.Equals(BCrypt.Net.BCrypt
				.HashPassword(password, user.PasswordSalt)
			);
}