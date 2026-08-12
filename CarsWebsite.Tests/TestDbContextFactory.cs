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
            .Options;
        return new AppDbContext(options);
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
            CreateHierarchyValidationService(context), new NullAdvertSearchIndexService());
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
