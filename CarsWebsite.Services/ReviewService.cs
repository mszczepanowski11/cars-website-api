using cars_website_api.CarsWebsite.Domain.Entities;
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

    public async Task<object> GetSellerReviewsAsync(int sellerId, int page, int pageSize)
    {
        var query = _context.Reviews.Where(r => r.SellerId == sellerId).OrderByDescending(r => r.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.BuyerId, r.Rating, r.Comment, r.CreatedAt })
            .ToListAsync();

        return new { Items = items, TotalCount = total };
    }
}
