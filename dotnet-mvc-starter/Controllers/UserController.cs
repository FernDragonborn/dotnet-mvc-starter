using System.Security.Claims;
using api.Identity;
using api.Services.IServices;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

/// <summary>
///     Controller responsible for user profile management, administration, and social interactions.
/// </summary>
[Route("api/profiles")]
[ApiController]
public class UserController(IUserService userService, IAuthService authService) : ControllerBase
{
	// --- REGISTER ---
	/// <summary>
	///     Register a new user and automatically log them in.
	/// </summary>
	/// <remarks>
	///     Creates a new user account with the provided credentials.
	///     If successful, returns a JWT Access/Refresh token pair immediately.
	/// </remarks>
	/// <param name="request">The registration payload containing Email, Username, Password.</param>
	/// <returns>A JSON object containing the authentication tokens.</returns>
	/// <response code="200">Successfully registered and authenticated. Returns tokens.</response>
	/// <response code="400">User already exists, validation failed, or login failed.</response>
	[ProducesResponseType(typeof(ResponseTypes.TokensResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpPost("register")]
	public async Task<IActionResult> RegisterUser([FromBody] RegisterRequest request)
	{
		var registerRes = await userService.RegisterUserAsync(request);
		if (registerRes.IsFailure)
			return BadRequest(new { error = registerRes.Error });

		var loginResult = await authService.LoginAsync(new LoginRequest(request.Email, request.Username, request.Password));
		if (loginResult.IsFailure)
			return BadRequest(new { error = loginResult.Error });

		return Ok(new { data = loginResult.Value });
	}

	// --- GET USER ---
	/// <summary>
	///     Get specific user profile details by username.
	/// </summary>
	/// <param name="username">The unique username of the user to retrieve.</param>
	/// <returns>User DTO containing public profile information.</returns>
	/// <response code="200">User found and returned.</response>
	/// <response code="400">User with the specified username was not found.</response>
	[ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpGet("{username}")]
	public async Task<IActionResult> GetUserById(string username)
	{
		string? currentUserId = null;
		var id = JwtHandler.GetIdFromToken(Request.Headers.Authorization);
		if (id.IsSuccess)
			currentUserId = id.Value;

		var result = await userService.GetUserByUsernameAsync(username, currentUserId);
		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return Ok(new { data = result.Value });
	}
	
	// --- GET ALL PAGED ---
	/// <summary>
	///     Get a paginated list of users with optional search filtering.
	/// </summary>
	/// <remarks>
	///     accessible only to users with the 'Admin' role.
	/// </remarks>
	/// <param name="request">Query parameters for pagination (PageNumber, PageSize) and search (SearchTerm).</param>
	/// <returns>A paged response containing the list of users and metadata.</returns>
	/// <response code="200">Returns the list of users.</response>
	/// <response code="400">Invalid pagination parameters (e.g., PageSize > 100).</response>
	[Authorize(Policy = IdentityData.PolicyAdmin)]
	[ProducesResponseType(typeof(ResponseTypes.PagedResponse<UserDto>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpGet]
	public async Task<IActionResult> GetUsers([FromQuery] UserFilterDto request)
	{
		if (request.PageSize > 100)
			return BadRequest(new { error = "Page size cannot exceed 100" });

		var result = await userService.GetUsersPagedAsync(request);
		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return Ok(new { data = result.Value });
	}

	// --- AVATAR (NEW) ---
	/// <summary>
	///     Retrieve a user's avatar image file.
	/// </summary>
	/// <param name="fileName">The filename of the avatar (stored in UserDto.ProfilePic).</param>
	/// <returns>The image file stream (image/jpeg, image/png, etc.).</returns>
	/// <response code="200">Image found and returned.</response>
	/// <response code="404">Image file not found on server.</response>
	[HttpGet("avatar/{fileName}")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> GetAvatar(string fileName)
	{
		var result = await userService.GetAvatarAsync(fileName);
		if (result.IsFailure)
			return NotFound(new { error = result.Error });

		return File(result.Value.Bytes, result.Value.ContentType);
	}

	// --- REDACT PFP ---
	/// <summary>
	///     Upload or update the profile picture for the currently authenticated user.
	/// </summary>
	/// <param name="pictureDto">Object containing the image file (IFormFile) and the user's email.</param>
	/// <returns>Status 200 OK if successful.</returns>
	/// <response code="204">Profile picture updated.</response>
	/// <response code="400">File is missing, invalid format, or user attempted to update another profile.</response>
	[Authorize]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	[HttpPost("me/redactpfp")]
	public async Task<IActionResult> RedactProfilePicture([FromForm] ProfilePictureDto pictureDto)
	{
		if (User.Identity?.Name != pictureDto.Email)
			return BadRequest(new { error = "You can only update your own profile" });

		var result = await userService.RedactProfilePictureAsync(pictureDto);

		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return NoContent();
	}

	// --- DELETE AVATAR ---
	/// <summary>
	///     Delete the profile picture for the currently authenticated user.
	/// </summary>
	/// <returns>Status 204 No Content if successful.</returns>
	/// <response code="204">Avatar deleted.</response>
	/// <response code="400">User has no avatar or user ID missing from token.</response>
	[Authorize]
	[HttpDelete("me/avatar")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> DeleteAvatar()
	{
		var userId = User.FindFirstValue("id")
		             ?? User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
			return BadRequest(new { error = "User ID claim is missing or invalid." });

		var result = await userService.DeleteAvatarAsync(userGuid);
		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return NoContent();
	}

	// --- UPDATE USER ---
	/// <summary>
	///     Update personal profile information.
	/// </summary>
	/// <remarks>
	///     Users can update their own profile. Admins can update any profile.
	/// </remarks>
	/// <param name="userDto">The user object with updated fields.</param>
	/// <returns>Status 200 OK if successful.</returns>
	/// <response code="204">User updated successfully.</response>
	/// <response code="400">Validation error or unauthorized update attempt.</response>
	[HttpPut("me")]
	[Authorize]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> UpdateUser([FromBody] UserDto userDto)
	{
		var currentEmail = User.Identity?.Name;
		var isAdmin = User.IsInRole(IdentityData.ClaimAdmin.ToString());

		var result = await userService.UpdateUserAsync(userDto, currentEmail, isAdmin);

		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return NoContent();
	}
	
	// --- UPDATE USER ROLE ---
	/// <summary>
	///     Update user role by their email.
	/// </summary>
	/// <remarks>
	///     Only admins are allowed to do it.
	/// </remarks>
	/// <param name="updateRoleRequest">Object contains target email and wished role.</param>
	/// <returns>Status 200 OK if successful.</returns>
	/// <response code="204">User role updated successfully.</response>
	/// <response code="400">Validation error or unauthorized update attempt.</response>
	[HttpPut("updateRole")]
	[Authorize]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> UpdateUserRole([FromBody] UpdateRoleRequest updateRoleRequest)
	{
		var isAdmin = User.IsInRole(IdentityData.ClaimAdmin.ToString());
		if (!isAdmin)
			return BadRequest(new { error = "Only admins are permitted to update roles." });

		var changerEmail = User.Identity?.Name;
		if (string.IsNullOrWhiteSpace(changerEmail))
			return BadRequest(new { error = "Cannot retrieve email from JWT token." });
		var result = await userService.UpdateUserRoleAsync(updateRoleRequest, changerEmail);

		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return NoContent();
	}

	// --- UPDATE PASSWORD (NEW) ---
	/// <summary>
	///     Change the password for the currently authenticated user.
	/// </summary>
	/// <param name="request">Contains CurrentPassword, NewPassword, and NewPasswordConfirmation.</param>
	/// <returns>A success message.</returns>
	/// <response code="204">Password changed successfully.</response>
	/// <response code="400">Passwords do not match, wrong current password, or user ID missing from token.</response>
	[Authorize]
	[HttpPut("me/password")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
	{
		if (request.NewPassword != request.NewPasswordConfirmation)
			return BadRequest(new { error = "Паролі не співпадають." });

		var userId = User.FindFirstValue("id")
		             ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(userId))
			return BadRequest(new { error = "User doesn't have id in JWT" });

		var result = await userService.UpdatePasswordAsync(userId, request.NewPassword, request.CurrentPassword);

		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		return NoContent();
	}

	// --- RESET PASSWORD (NEW - ADMIN) ---
	/// <summary>
	///     Admin only: Force reset a user's password and receive a temporary one.
	/// </summary>
	/// <param name="userId">The Guid (as string) of the user.</param>
	/// <returns>A JSON object containing the new temporary password.</returns>
	/// <response code="200">Password reset successfully. Returns temp password.</response>
	/// <response code="400">User not found or ID invalid.</response>
	[Authorize(Roles = "Admin")]
	[HttpPost("{userId}/reset-password")]
	[ProducesResponseType(typeof(object), StatusCodes.Status200OK)] // { tempPassword = "..." }
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> ResetPassword(string userId)
	{
		var result = await userService.ResetPasswordAsync(userId);

		if (result.IsFailure)
			return BadRequest(new { error = result.Error });

		// Повертаємо згенерований пароль, щоб адмін міг передати його юзеру
		return Ok(new { tempPassword = result.Value });
	}
	
	// --- DELETE BY USERNAME ---
	/// <summary>
	///     Admin only: Delete a user account by their username.
	/// </summary>
	/// <param name="username">The username to delete.</param>
	/// <returns>Status 200 OK.</returns>
	/// <response code="204">User deleted.</response>
	/// <response code="400">User not found.</response>
	[HttpDelete("{username}")]
	[Authorize(Policy = IdentityData.PolicyAdmin)]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> DeleteUser(string username)
	{
		var result = await userService.DeleteUserByUsernameAsync(username);
		if (result.IsFailure)
			return BadRequest(new { error = result.Error });
		return NoContent();
	}

	// --- DELETE BY ID (NEW - Uses DeleteAnyUserAsync) ---
	/// <summary>
	///     Admin only: Delete a user account by their unique ID (Guid).
	/// </summary>
	/// <param name="userId">The unique identifier of the user.</param>
	/// <returns>Status 200 OK.</returns>
	/// <response code="204">User deleted.</response>
	/// <response code="400">User not found or invalid ID format.</response>
	[HttpDelete("id/{userId}")]
	[Authorize(Policy = IdentityData.PolicyAdmin)]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> DeleteUserById(string userId)
	{
		var result = await userService.DeleteAnyUserAsync(userId);
		if (result.IsFailure)
			return BadRequest(new { error = result.Error });
		return NoContent();
	}
}