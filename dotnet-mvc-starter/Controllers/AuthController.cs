using api.Identity;
using api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
	/// <summary>
	///     Log in and get token pair
	/// </summary>
	/// <param name="request">Standard .NET LoginRequest object. Just use Email and Password in request object</param>
	/// <returns>response dto JWT pair</returns>
	/// <response code="200">Successfully logged in</response>
	/// <response code="400">Wrong login or password</response>
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status400BadRequest)]
	[HttpPost("login")]
	public async Task<ObjectResult> Login([FromBody] LoginRequest request)
	{
		var result = await authService.LoginAsync(request);
		if (result.IsSuccess)
			return Ok(new { data = result.Value });
		return BadRequest(new { error = result.Error });
	}

	/// <summary>
	///     Send access token and get UserDto with renewed access and refresh tokens
	/// </summary>
	/// <param></param>
	/// <param name="request"></param>
	/// <returns>UserDto</returns>
	/// <response code="200">Successfully authorized</response>
	/// <response code="400">JWT expired / not correct</response>
	[ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
	[HttpPost("renewToken")]
	public async Task<ObjectResult> RenewToken([FromBody] RenewTokenRequest request)
	{
		var result = await authService.RenewTokenAsync(request.RefreshToken);
		if (result.IsSuccess)
			return Ok(new { data = result.Value });
		return BadRequest(new { error = result.Error });
	}

	[Authorize(Policy = IdentityData.PolicyAdmin)]
	[ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
	[HttpGet("testAuthorization")]
	public IActionResult TestSuperAdmin() => Ok("oh, hi...");
}