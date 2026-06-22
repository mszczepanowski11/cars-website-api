using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Review;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;

    public ReviewService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ReviewsResultDto> GetSellerReviewsAsync(int sellerId, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Reviews.Where(r => r.SellerId == sellerId);
        var total = await query.CountAsync();
        var avg = total > 0 ? await query.AverageAsync(r => (double)r.Rating) : 0.0;
        var reviews = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = await EnrichReviewsAsync(reviews);
        return new ReviewsResultDto { Items = items, TotalCount = total, AverageRating = Math.Round(avg, 2) };
    }

    public async Task<ReviewsResultDto> GetMyReceivedReviewsAsync(int userId, int page, int pageSize)
        => await GetSellerReviewsAsync(userId, page, pageSize);

    public async Task<PagedReviewResultDto> GetMyGivenReviewsAsync(int userId, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Reviews.Where(r => r.BuyerId == userId);
        var total = await query.CountAsync();
        var reviews = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = await EnrichReviewsAsync(reviews);
        return new PagedReviewResultDto { Items = items, TotalCount = total };
    }

    public async Task<bool> CanReviewAsync(int buyerId, int sellerId)
    {
        if (buyerId == sellerId) return false;
        return !await _context.Reviews.AnyAsync(r => r.BuyerId == buyerId && r.SellerId == sellerId);
    }

    public async Task<ReviewDto> CreateReviewAsync(int buyerId, CreateReviewDto dto)
    {
        if (buyerId == dto.SellerId)
            throw new InvalidOperationException("Cannot review yourself.");

        if (!await CanReviewAsync(buyerId, dto.SellerId))
            throw new InvalidOperationException("Already reviewed this seller.");

        var review = new Review
        {
            SellerId = dto.SellerId,
            BuyerId = buyerId,
            AdvertId = dto.AdvertId ?? 0,
            Rating = Math.Clamp(dto.Rating, 1, 5),
            Comment = dto.Content,
            CreatedAt = DateTime.UtcNow
        };
        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        var items = await EnrichReviewsAsync(new List<Review> { review });
        return items.First();
    }

    public async Task DeleteReviewAsync(int reviewId, int userId, bool isAdmin)
    {
        var review = await _context.Reviews.FindAsync(reviewId)
            ?? throw new KeyNotFoundException("Review not found.");
        if (!isAdmin && review.BuyerId != userId)
            throw new UnauthorizedAccessException("Cannot delete this review.");
        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();
    }

    private async Task<List<ReviewDto>> EnrichReviewsAsync(List<Review> reviews)
    {
        if (!reviews.Any()) return new List<ReviewDto>();

        var userIds = reviews.Select(r => r.SellerId).Concat(reviews.Select(r => r.BuyerId)).Distinct().ToList();
        var advertIds = reviews.Select(r => r.AdvertId).Where(id => id != 0).Distinct().ToList();

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Surname })
            .ToListAsync();
        var userMap = users.ToDictionary(u => u.Id);

        var advertMap = new Dictionary<int, string>();
        if (advertIds.Any())
        {
            var adverts = await _context.Adverts
                .Where(a => advertIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Title })
                .ToListAsync();
            advertMap = adverts.ToDictionary(a => a.Id, a => a.Title);
        }

        return reviews.Select(r =>
        {
            userMap.TryGetValue(r.SellerId, out var seller);
            userMap.TryGetValue(r.BuyerId, out var buyer);
            advertMap.TryGetValue(r.AdvertId, out var advertTitle);
            return new ReviewDto
            {
                Id = r.Id,
                SellerId = r.SellerId,
                SellerName = seller != null ? $"{seller.Name} {seller.Surname}".Trim() : "",
                BuyerId = r.BuyerId,
                BuyerName = buyer != null ? $"{buyer.Name} {buyer.Surname}".Trim() : "",
                AdvertId = r.AdvertId,
                AdvertTitle = advertTitle,
                Rating = r.Rating,
                Content = r.Comment,
                CreatedAt = r.CreatedAt,
                IsVerifiedPurchase = false
            };
        }).ToList();
    }
}
