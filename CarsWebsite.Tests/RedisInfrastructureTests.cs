using System.Reflection;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Services;
using CarsWebsite;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace CarsWebsiteTests;

// CTO audit Etap 4 - "Prowizjonować Redis; przenieść cache taksonomii, wyniki wyszukiwania, stan
// rate limitera". These tests run against a real local Redis (see TEST_REDIS_CONNECTION_STRING),
// not a mock, because the entire point of this work is cross-instance behavior that an in-process
// fake can't exercise honestly: two independent IDistributedCache/RateLimiter instances pointed at
// the same Redis simulate two app replicas behind a load balancer.
public class RedisInfrastructureTests
{
    private static string RedisConnectionString =>
        Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION_STRING") ?? "localhost:6379";

    private static IDistributedCache CreateRedisDistributedCache() =>
        new RedisCache(Options.Create(new RedisCacheOptions { Configuration = RedisConnectionString }));

    [Fact]
    public async Task TaxonomyCacheVersion_BumpOnOneInstance_IsVisibleFromAnotherInstance()
    {
        // Two separate TaxonomyCacheVersion instances, each with its own IDistributedCache client,
        // both pointed at the same Redis - simulating replica A (which handles an admin's taxonomy
        // edit) and replica B (which must invalidate its own cached results as a result).
        var versionOnReplicaA = new TaxonomyCacheVersion(CreateRedisDistributedCache());
        var versionOnReplicaB = new TaxonomyCacheVersion(CreateRedisDistributedCache());

        var initial = await versionOnReplicaB.GetCurrentAsync();
        await versionOnReplicaA.BumpAsync();
        var afterBump = await versionOnReplicaB.GetCurrentAsync();

        Assert.NotEqual(initial, afterBump);
    }

    [Fact]
    public async Task TaxonomyService_CachesBrands_AndInvalidatesOnlyAfterVersionBump()
    {
        var context = TestDbContextFactory.CreateContext(Guid.NewGuid().ToString());
        context.Brands.Add(new Brand { Name = "TestBrand1", Slug = "testbrand1-" + Guid.NewGuid() });
        await context.SaveChangesAsync();

        var cache = CreateRedisDistributedCache();
        var version = new TaxonomyCacheVersion(cache);
        var service = new TaxonomyService(context, cache, version);

        var first = await service.GetBrandsAsync();
        Assert.Single(first);

        // A second brand added directly (simulating an admin edit through a different code path)
        // must NOT show up until the cache version is bumped.
        context.Brands.Add(new Brand { Name = "TestBrand2", Slug = "testbrand2-" + Guid.NewGuid() });
        await context.SaveChangesAsync();
        var stillCached = await service.GetBrandsAsync();
        Assert.Single(stillCached);

        await version.BumpAsync();
        var afterBump = await service.GetBrandsAsync();
        Assert.Equal(2, afterBump.Count());
    }

    [Fact]
    public async Task SearchCarAdvertsAsync_PublicSearch_CachesResultAcrossIdenticalQueries()
    {
        var context = TestDbContextFactory.CreateContext(Guid.NewGuid().ToString());
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, "search-cache-" + Guid.NewGuid() + "@bulk.test");
        var advertService = new AdvertService(
            context, TestDbContextFactory.CreateMapper(), NullLogger<AdvertService>.Instance,
            TestDbContextFactory.CreateDummyCloudinary(), TestDbContextFactory.CreateHierarchyValidationService(context),
            new NullAdvertSearchIndexService(), CreateRedisDistributedCache());

        await advertService.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = "Cache Test Advert", Description = "desc", Price = 20000, Condition = "used", SellerType = "dealer",
        }, user.Id);

        var searchDto = new SearchCarAdvertDto { PageSize = 50 };
        var first = await advertService.SearchCarAdvertsAsync(searchDto);
        Assert.Equal(1, first.TotalCount);

        // A second advert created directly after the first search - a fresh (non-cached) search
        // would now see 2 results.
        await advertService.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = "Second Advert", Description = "desc", Price = 5000, Condition = "used", SellerType = "dealer",
        }, user.Id);

        // Same DTO -> same cache key -> still the stale-but-cached count of 1, proving the second
        // call actually hit Redis instead of re-querying the database.
        var second = await advertService.SearchCarAdvertsAsync(searchDto);
        Assert.Equal(1, second.TotalCount);

        // A user-scoped search (UserId set) must never be cached - it bypasses the cache
        // entirely and sees the true, current count.
        var userScoped = await advertService.SearchCarAdvertsAsync(new SearchCarAdvertDto { PageSize = 50, UserId = user.Id });
        Assert.Equal(2, userScoped.TotalCount);
    }

    [Fact]
    public void RedisFixedWindowRateLimiter_RejectsOnceLimitExceeded()
    {
        var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
        var key = $"test:ratelimit:{Guid.NewGuid()}";
        var limiter = new RedisFixedWindowRateLimiter(redis, key, permitLimit: 2, window: TimeSpan.FromSeconds(30));

        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.False(limiter.AttemptAcquire(1).IsAcquired);
    }

    [Fact]
    public void RedisFixedWindowRateLimiter_SharesQuotaAcrossTwoInstances_SimulatingTwoReplicas()
    {
        var redis = ConnectionMultiplexer.Connect(RedisConnectionString);
        var key = $"test:ratelimit:{Guid.NewGuid()}";
        // Same key, two independent limiter instances - exactly what happens when the same client
        // hits two different replicas within one rate-limit window.
        var limiterOnReplicaA = new RedisFixedWindowRateLimiter(redis, key, permitLimit: 3, window: TimeSpan.FromSeconds(30));
        var limiterOnReplicaB = new RedisFixedWindowRateLimiter(redis, key, permitLimit: 3, window: TimeSpan.FromSeconds(30));

        Assert.True(limiterOnReplicaA.AttemptAcquire(1).IsAcquired);
        Assert.True(limiterOnReplicaA.AttemptAcquire(1).IsAcquired);
        Assert.True(limiterOnReplicaB.AttemptAcquire(1).IsAcquired);
        // 4th request across the two replicas combined - must be rejected, proving the quota is
        // shared rather than each replica getting its own independent limit of 3.
        Assert.False(limiterOnReplicaB.AttemptAcquire(1).IsAcquired);
    }

    [Fact]
    public void ParseRedisConnectionString_ConvertsRailwayStyleUrlToStackExchangeFormat()
    {
        var programType = Assembly.Load("cars-website-api").GetType("Program")
            ?? throw new InvalidOperationException("Program type not found via reflection.");
        var method = programType.GetMethod("ParseRedisConnectionString", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ParseRedisConnectionString method not found via reflection.");

        var result = (string?)method.Invoke(null, new object?[] { "redis://default:s3cr3t@redis.railway.internal:6379" });

        Assert.NotNull(result);
        Assert.Contains("redis.railway.internal:6379", result);
        Assert.Contains("password=s3cr3t", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRedisConnectionString_PassesThroughNonUrlConnectionStringUnchanged()
    {
        var method = Assembly.Load("cars-website-api").GetType("Program")!.GetMethod("ParseRedisConnectionString", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string?)method.Invoke(null, new object?[] { "localhost:6379" });

        Assert.Equal("localhost:6379", result);
    }

    [Fact]
    public void ParseRedisConnectionString_EmptyInput_ReturnsNull()
    {
        var method = Assembly.Load("cars-website-api").GetType("Program")!.GetMethod("ParseRedisConnectionString", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string?)method.Invoke(null, new object?[] { "" });

        Assert.Null(result);
    }
}
