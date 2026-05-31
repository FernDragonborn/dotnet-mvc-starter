using api.Identity;
using api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

/// <summary>
///     Controller responsible for authentication: login, token renewal, and role-based access checks.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
	// --- LOGIN ---
	/// <summary>
	///     Authenticate a user and receive a JWT Access/Refresh token pair.
	/// </summary>
	/// <remarks>
	///     Accepts either Email or Username together with Password.
	///     Returns 400 with a generic error on any failure (do not leak which field was wrong).
	/// </remarks>
	/// <param name="request">The login payload. Provide Password and at least one of Email or Username.</param>
	/// <returns>A JSON object containing the authentication tokens.</returns>
	/// <response code="200">Successfully authenticated. Returns the token pair.</response>
	/// <response code="400">Invalid credentials or missing required fields.</response>
	[ProducesResponseType(typeof(ResponseTypes.TokensResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpPost("login")]
	public async Task<ObjectResult> Login([FromBody] LoginRequest request)
	{
		var result = await authService.LoginAsync(request);
		if (result.IsSuccess)
			return Ok(new { data = result.Value });
		return BadRequest(new { error = result.Error });
	}

	// --- RENEW TOKEN ---
	/// <summary>
	///     Exchange a valid Refresh token for a fresh Access/Refresh pair.
	/// </summary>
	/// <remarks>
	///     The current Access token does not need to be valid. Only the Refresh token is checked.
	///     The old Refresh token is not invalidated server-side — rotation is the client's responsibility.
	/// </remarks>
	/// <param name="request">The refresh-token payload.</param>
	/// <returns>A JSON object containing a new Access/Refresh token pair.</returns>
	/// <response code="200">Successfully renewed. Returns the new token pair.</response>
	/// <response code="400">Refresh token is missing, malformed, expired, or for a non-existent user.</response>
	[ProducesResponseType(typeof(ResponseTypes.TokensResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpPost("renewToken")]
	public async Task<ObjectResult> RenewToken([FromBody] RenewTokenRequest request)
	{
		var result = await authService.RenewTokenAsync(request.RefreshToken);
		if (result.IsSuccess)
			return Ok(new { data = result.Value });
		return BadRequest(new { error = result.Error });
	}

	// --- TEST AUTHORIZATION (ADMIN ONLY) ---
	/// <summary>
	///     Smoke-test endpoint to verify Admin-policy enforcement.
	/// </summary>
	/// <remarks>
	///     Reachable only with a valid Access token whose role satisfies <c>IdentityData.PolicyAdmin</c>.
	///     Useful for verifying JWT plumbing and role claims end-to-end.
	/// </remarks>
	/// <returns>A trivial greeting string.</returns>
	/// <response code="200">Caller is authenticated and authorized as Admin.</response>
	/// <response code="401">No token or token is invalid.</response>
	/// <response code="403">Token is valid but the caller lacks the Admin role.</response>
	[Authorize(Policy = IdentityData.PolicyAdmin)]
	[ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(StatusCodes.Status403Forbidden)]
	[HttpGet("testAuthorization")]
	public IActionResult TestSuperAdmin() => Ok("oh, hi...");
}
