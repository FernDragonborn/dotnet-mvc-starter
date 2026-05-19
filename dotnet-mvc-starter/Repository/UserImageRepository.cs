using api.DbContext;

namespace api.Repository;

public class UserImageRepository : Repository<UserImage>, IUserImageRepository
{
    private readonly MyDbContext _db;

    public UserImageRepository(MyDbContext db) : base(db)
    {
        _db = db;
    }

    public async Task<UserImage> Update(UserImage userImage)
    {
        userImage.UpdatedAt = DateTime.UtcNow;
        _db.UserImages.Update(userImage);
        await _db.SaveChangesAsync();
        return userImage;
    }
}