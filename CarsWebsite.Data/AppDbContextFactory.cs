using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace cars_website_api.CarsWebsite.Data;

// Design-time factory for `dotnet ef` tooling (migrations add/list/database update/...).
// Without this, EF Core falls back to reflecting over Program.Main, which for this app means
// running the ENTIRE startup pipeline - advisory-locked schema guards, ModelSeeder,
// ComprehensiveSeeder, ExternalTaxonomySeeder, AttributeDefinitionMigrationSeeder, etc. - just to
// get a DbContext instance to inspect. That's minutes of unrelated work (and live writes against
// whatever database Program.cs resolves) for what should be an instant, read-only operation.
// This factory builds the same connection string Program.cs would (Railway env vars first, falling
// back to appsettings' DefaultConnection) but returns bare `new AppDbContext(options)` - no seeding,
// no migration-apply, no advisory lock.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var mysqlHost = Environment.GetEnvironmentVariable("MYSQLHOST");
        var mysqlPass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
        string? connectionString;
        if (!string.IsNullOrEmpty(mysqlHost) && !string.IsNullOrEmpty(mysqlPass))
        {
            var port = Environment.GetEnvironmentVariable("MYSQLPORT") ?? "3306";
            var db = Environment.GetEnvironmentVariable("MYSQLDATABASE") ?? Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "railway";
            var user = Environment.GetEnvironmentVariable("MYSQLUSER") ?? "root";
            connectionString = $"Server={mysqlHost};Port={port};Database={db};User={user};Password={mysqlPass};";
        }
        else
        {
            connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(9, 4, 0)));
        return new AppDbContext(optionsBuilder.Options);
    }
}
