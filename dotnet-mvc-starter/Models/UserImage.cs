using System.ComponentModel.DataAnnotations;

namespace api.Models;

public class UserImage(Guid userId, string fileName, DateTime uploadedAt) : BaseEntity
{

	public Guid UserId { get; set; } = userId;

	[MaxLength(300)]
	public string FileName { get; set; } = fileName;

	public DateTime UploadedAt { get; set; } = uploadedAt;

	public virtual User User { get; set; } = null!;
}