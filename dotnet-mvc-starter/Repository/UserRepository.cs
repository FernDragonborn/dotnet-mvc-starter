using api.DbContext;

namespace api.Repository;

public class UserRepository : Repository<User>, IUserRepository
{
	private readonly MyDbContext _context;
	public UserRepository(MyDbContext context) : base(context)
	{
		_context = context;
	}
	public async Task<User> Update(User user)
	{
		user.UpdatedAt = DateTime.UtcNow;
		var updatedUser = _context.Users.Update(user);
		await _context.SaveChangesAsync();
		return updatedUser.Entity;
	} 
}