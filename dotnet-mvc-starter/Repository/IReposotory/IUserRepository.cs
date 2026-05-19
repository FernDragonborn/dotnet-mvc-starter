namespace api.Repository.IReposotory;

public interface IUserRepository:IRepository<User>
{
    Task<User> Update(User user);
}