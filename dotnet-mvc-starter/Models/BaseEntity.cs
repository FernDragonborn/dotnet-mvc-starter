namespace api.Models;

/// <summary>
///     Base class for all main entities.
///     EF will automatically add these fields to every table that inherits it.
/// </summary>
public abstract class BaseEntity
{
	public Guid Id { get; set; }

	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }

	// Soft delete. null = active record. Date = deleted at this timestamp.
	public DateTime? DeletedAt { get; set; }
}