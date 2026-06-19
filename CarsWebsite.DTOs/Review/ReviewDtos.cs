using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Review;

public class ReviewDto
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public string SellerName { get; set; } = "";
    public int BuyerId { get; set; }
    public string BuyerName { get; set; } = "";
    public int AdvertId { get; set; }
    public string? AdvertTitle { get; set; }
    public int Rating { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsVerifiedPurchase { get; set; }
}

public class CreateReviewDto
{
    [Required] public int SellerId { get; set; }
    public int? AdvertId { get; set; }
    [Required] [Range(1, 5)] public int Rating { get; set; }
    [Required] [MaxLength(2000)] public string Content { get; set; } = "";
}

public class ReviewsResultDto
{
    public List<ReviewDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }
}

public class PagedReviewResultDto
{
    public List<ReviewDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
}
