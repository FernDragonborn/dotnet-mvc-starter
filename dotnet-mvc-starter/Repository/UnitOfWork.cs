using api.DbContext;

namespace api.Repository;

public class UnitOfWork : IUnitOfWork
{
    public IUserRepository UserRepository { get; private set; }
    public IUserImageRepository UserImageRepository { get; private set; }

    public UnitOfWork(MyDbContext db)
    {
        UserRepository = new UserRepository(db);
        UserImageRepository = new UserImageRepository(db);
    }
}