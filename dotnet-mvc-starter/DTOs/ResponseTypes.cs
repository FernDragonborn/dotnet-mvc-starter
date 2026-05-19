namespace api.DTOs;

public abstract record ResponseTypes
{
	public record TokensResponse(string AccessToken, string RefreshToken)
	{
		public string AccessToken { get; init; } = AccessToken ?? throw new ArgumentNullException(nameof(AccessToken));

		public string RefreshToken { get; init; } =
			RefreshToken ?? throw new ArgumentNullException(nameof(RefreshToken));
	}

	public record UserRegistrationResponse(string Password, string Id);

	public record UserFileResponse(byte[] Bytes, string ContentType);

	public record PagedResponse<T>(
		List<T> Items,
		int TotalCount,
		int PageNumber,
		int PageSize)
	{
		public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
	}
}