namespace api.DTOs;

public class UserDto
{
    public string? Username { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public UserRole Role { get; set; }

    public Gender Gender { get; set; }

    public string? ProfilePicUrl { get; set; }

    public string? DisplayName { get; set; }

    public IFormFile? ProfilePic { get; set; }
}