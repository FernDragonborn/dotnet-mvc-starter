using dotenv.net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace api.DbContext;

public static class ContextFactory
{
    private static DbContextOptions? _options;

    public static MyDbContext CreateNew()
    {
        var envOptions = new DotEnvOptions(ignoreExceptions: true);
        var env = DotEnv.Read(envOptions);

        if (!env.TryGetValue("CONNECTION_STRING", out var connectionString))
        {
            // This connection string used for making migrations in docker
            connectionString = "Data Source=dummy.db";
            Console.WriteLine("⚠WARNING⚠: CONNECTION_STRING not found. Using dummy connecting string.");
        }

        _options ??= new DbContextOptionsBuilder<MyDbContext>()
            .UseLazyLoadingProxies()
            .EnableSensitiveDataLogging()
            .LogTo(Console.WriteLine)
            .UseSqlite(connectionString)
            .Options;

        return new MyDbContext(_options);
    }
}

public class DbContextDesignTimeFactory : IDesignTimeDbContextFactory<MyDbContext>
{
    public MyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MyDbContext>();
        
        // This connection string used for making migrations in docker
        var connectionString = "Data Source=dummy.db";

        try
        {
            // Ignoring exceptions for proper use of dummy connection string
            var envOptions = new DotEnvOptions(ignoreExceptions: true);
            var env = DotEnv.Read(envOptions);
            
            // Якщо файл прочитано і змінна існує (наприклад, локально), використовуємо її
            if (env.TryGetValue("CONNECTION_STRING", out var realConnectionString))
            {
                connectionString = realConnectionString;
            }
        }
        catch
        {
            //Just ignore exceptions on docker migrations.
        }

        builder.UseSqlite(connectionString);

        return new MyDbContext(builder.Options);
    }
}