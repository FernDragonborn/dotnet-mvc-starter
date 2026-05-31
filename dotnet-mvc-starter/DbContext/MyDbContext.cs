using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace api.DbContext;

public class MyDbContext(DbContextOptions options) : Microsoft.EntityFrameworkCore.DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<UserImage> UserImages { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.LogTo(Console.WriteLine, LogLevel.Warning);
        }

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var prop = Expression.Property(parameter, nameof(BaseEntity.DeletedAt));
            var nullConst = Expression.Constant(null, typeof(DateTime?));
            var body = Expression.Equal(prop, nullConst);
            var lambda = Expression.Lambda(body, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}