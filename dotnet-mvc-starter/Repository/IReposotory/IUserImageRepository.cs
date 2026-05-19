namespace api.Repository.IReposotory;

public interface IUserImageRepository : IRepository<UserImage>
{
    Task<UserImage> Update(UserImage userImage);
}