namespace api.Repository.IReposotory;

public interface IUnitOfWork
{
    IUserRepository UserRepository { get; }
    IUserImageRepository UserImageRepository { get; }
}