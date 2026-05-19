using System.Linq.Expressions;

namespace api.Repository.IReposotory;

public interface IRepository<T> where T : class
{
	Task<Result<List<T>>> GetAsync(Expression<Func<T, bool>>? filter = null, string? includeProperties = null);

	Task<Result<T>> GetOneAsync(Expression<Func<T, bool>> filter, string? includeProperties = null);

	Task<Result<(List<T> Items, int TotalCount)>> GetPagedAsync(
		Expression<Func<T, bool>>? filter = null,
		Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
		int pageNumber = 1,
		int pageSize = 10);

	Task<Result<T>> AddAsync(T entity);

	Task<Result> Update(T entity);

	Task<Result> Remove(T entity);
}