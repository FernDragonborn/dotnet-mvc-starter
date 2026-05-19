using System.Diagnostics;
using System.Linq.Expressions;
using api.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace api.Repository;

public class Repository<T> : IRepository<T> where T : class
{
	private readonly MyDbContext _context;
	private readonly DbSet<T> _dbSet;

	protected Repository(MyDbContext context)
	{
		_context = context;
		_dbSet = _context.Set<T>();
	}

	public async Task<Result<T>> AddAsync(T entity)
	{
		if (entity is BaseEntity baseModel) baseModel.CreatedAt = DateTime.UtcNow;
		EntityEntry<T> entry;
		try
		{
			entry = await _dbSet.AddAsync(entity);
		}
		catch (Exception ex)
		{
			return Result.Exception<T>(ex);
		}
		await _context.SaveChangesAsync();
		return Result.Ok(entry.Entity);
	}

	public async Task<Result<List<T>>> GetAsync(Expression<Func<T, bool>>? filter = null,
		string? includeProperties = null)
	{
		var query = _dbSet.AsQueryable();
		if (filter is not null) query = query.Where(filter);

		var propsAreIncluded = !string.IsNullOrWhiteSpace(includeProperties);
		if (propsAreIncluded)
		{
			Debug.Assert(includeProperties != null, nameof(includeProperties) + " != null");
			query = includeProperties
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Aggregate(query,
					(current, includeProperty) => current.Include(includeProperty)
				);
		}

		List<T> fetched;
		try
		{
			fetched = await query.ToListAsync();
		}	
		catch (Exception ex)
		{
			return Result.Exception<List<T>>(ex);
		}
		return Result.Ok(fetched);
	}

	public async Task<Result<(List<T> Items, int TotalCount)>> GetPagedAsync(
		Expression<Func<T, bool>>? filter = null,
		Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
		int pageNumber = 1,
		int pageSize = 10)
	{
		try
		{
			IQueryable<T> query = _dbSet;

			if (filter is not null)
				query = query.Where(filter);

			// 1. Рахуємо загальну кількість ДО пагінації
			var totalCount = await query.CountAsync();

			// 2. Сортування (обов'язкове для коректної пагінації)
			if (orderBy != null)
				query = orderBy(query);
			else if (typeof(BaseEntity).IsAssignableFrom(typeof(T)))
				query = query.OrderByDescending(x => ((BaseEntity)(object)x).CreatedAt);

			// 3. Пагінація
			var items = await query
				.Skip((pageNumber - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			return Result.Ok((items, totalCount));
		}
		catch (Exception ex)
		{
			return Result.Exception<(List<T>, int)>(ex);
		}
	}


	public async Task<Result<T>> GetOneAsync(Expression<Func<T, bool>> filter, string? includeProperties = null)
	{
		var query = _dbSet.AsQueryable();
		query = query.Where(filter);

		if (!string.IsNullOrWhiteSpace(includeProperties))
		{
			query = includeProperties
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Aggregate(query,
					(current, includeProperty) => current.Include(includeProperty)
				);
		}

		var fetched = await query.FirstOrDefaultAsync();
		return fetched is null
			? Result.Fail<T>($"No such entry for filter: \'{filter.Body}\' in table {typeof(T).Name}")
			: Result.Ok(fetched);
	}

	public async Task<Result> Update(T entity)
	{
		try
		{
			_dbSet.Update(entity);
			await _context.SaveChangesAsync();
		}
		catch (Exception ex)
		{
			return Result.Exception<Result>(ex);
		}
		return Result.Ok();
	}

	public async Task<Result> Remove(T entity)
	{
		try
		{
			_dbSet.Remove(entity);
			await _context.SaveChangesAsync();
		}
		catch (Exception ex)
		{
			return Result.Exception<Result>(ex);
		}
		return Result.Ok();
	}
}