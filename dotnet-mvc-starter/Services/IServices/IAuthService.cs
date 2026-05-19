namespace api.Services.IServices;

public interface IAuthService
{
	Task<Result<ResponseTypes.TokensResponse>> LoginAsync(LoginRequest dto);

	Task<Result<ResponseTypes.TokensResponse>> RenewTokenAsync(string jwtToken);
}