using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace api.Models;

[Index(nameof(Email), IsUnique = true)]
public class User : BaseEntity
{
    public User()
    {
    }

    public User(string passwordSalt)
    {
        Id = Guid.NewGuid();
        PasswordSalt = passwordSalt;
    }

    [Required] [MaxLength(25)] public string Username { get; set; } = string.Empty;

    [Required] [MaxLength(50)] public string Email { get; set; } = string.Empty;

    [Required] [MaxLength(60)] public string PasswordHash { get; set; } = string.Empty;

    [Required] [MaxLength(60)] public string PasswordSalt { get; set; } = string.Empty;

    [Column(TypeName = "varchar(20)")]
    [Required]
    public UserRole Role { get; set; }

    [Column(TypeName = "varchar(20)")] public Gender Gender { get; set; }

    [MaxLength(120)] public string? ProfilePicUrl { get; set; } = string.Empty;

    [MaxLength(50)] public string? DisplayName { get; set; }
}