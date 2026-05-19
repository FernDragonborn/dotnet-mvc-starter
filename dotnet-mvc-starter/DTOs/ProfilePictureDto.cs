namespace api.DTOs;

public class ProfilePictureDto
{
    public string? Email { get; set; }
    public IFormFile? ProfilePic { get; set; }
}