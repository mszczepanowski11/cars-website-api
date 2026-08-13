using AutoMapper;
using CarsWebsite;
using CloudinaryDotNet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsiteTests;

// Every test method gets its own isolated in-memory database (unique name per call), so tests
// never see each other's data and can run in any order/in parallel. Seeds the minimal reference
// taxonomy (VehicleCategory "auta-osobowe") every partner-import test needs, since
// PartnerImportService.ImportAsync hard-requires a matching category slug for every item.
public static class TestDbContextFactory
{
    public static AppDbContext CreateContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            // PaymentService.HandleWebhookAsync explicitly opens a DB transaction (see its own
            // comment on why - Pomelo's retrying execution strategy forbids ambient transactions).
            // The InMemory provider doesn't support real transactions and throws by default when
            // one is requested; tests don't need real transactional isolation, only that the code
            // path under test runs without EF Core treating "no-op transaction" as an error.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    // A handful of code paths (PaymentService.HandleWebhookAsync's row lock via
    // ExecuteSqlRawAsync("... FOR UPDATE") and its explicit BeginTransactionAsync) are genuinely
    // relational and have no InMemory-provider equivalent - InMemory throws
    // "Relational-specific methods can only be used when the context is using a relational
    // database provider" the moment ExecuteSqlRawAsync runs. Those tests need a real MySQL
    // database instead. TEST_MYSQL_CONNECTION_STRING lets CI point this at its own service
    // container; the default matches this repo's own established local-dev credentials
    // (appsettings.Development.json) against a separate `cars_website_test` schema, never the
    // real `cars_website` dev database.
    public static async Task<AppDbContext> CreateMySqlContextAsync(string testName)
    {
        // MySQL database identifiers cap at 64 chars and a raw test method name can easily exceed
        // that - hash down to a short, still-unique-per-test-name name instead of relying on every
        // caller to remember the limit.
        var dbName = "t_" + Math.Abs(testName.GetHashCode());

        var baseConnectionString = Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION_STRING")
            ?? "Server=localhost;Database=cars_website_test;User=root;Password=carswebsite;Port=3306;";
        var connectionString = baseConnectionString.Replace("cars_website_test", dbName);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(9, 4, 0)))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        var context = new AppDbContext(options);
        // EnsureCreated (not Migrate): builds the schema fresh from the current model, which is
        // enough for these tests and avoids replaying the full migration history against a
        // throwaway per-test-run database.
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    public static async Task<VehicleCategory> SeedCategoryAsync(AppDbContext context, string slug = "auta-osobowe", string name = "Auta osobowe")
    {
        var category = new VehicleCategory { Slug = slug, Name = name };
        context.VehicleCategories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    public static async Task<User> SeedBusinessUserAsync(AppDbContext context, string email)
    {
        var user = new User
        {
            Email = email,
            PasswordHash = "test-hash-not-used",
            Name = "Test",
            Surname = "Business",
            PhoneNumber = "+48500000000",
            AccountType = AccountType.Business,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<AdvertMappingProfile>());
        return config.CreateMapper();
    }

    public static IHierarchyValidationService CreateHierarchyValidationService(AppDbContext context)
        => new HierarchyValidationService(context);

    public static Cloudinary CreateDummyCloudinary() => new(new Account("test-cloud", "test-key", "test-secret"));

    public static IAdvertService CreateAdvertService(AppDbContext context)
        => new AdvertService(context, CreateMapper(), NullLogger<AdvertService>.Instance, CreateDummyCloudinary(),
            CreateHierarchyValidationService(context), new NullAdvertSearchIndexService(),
            new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())));
}

// Fail-open by design (see the real interface's comment): tests never touch Meilisearch, so this
// always reports disabled and no-ops every call - mirrors exactly how AdvertService behaves in
// production when Meilisearch isn't configured.
public class NullAdvertSearchIndexService : IAdvertSearchIndexService
{
    public bool IsEnabled => false;
    public Task IndexAsync(CarAdvert advert, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(int advertId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<List<int>?> SearchIdsAsync(string text, int limit, CancellationToken cancellationToken = default) => Task.FromResult<List<int>?>(null);
    public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
