using cars_website_api.CarsWebsite.DTOs.Review;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IReviewService
{
    Task<ReviewsResultDto> GetSellerReviewsAsync(int sellerId, int page, int pageSize);
    Task<ReviewsResultDto> GetMyReceivedReviewsAsync(int userId, int page, int pageSize);
    Task<PagedReviewResultDto> GetMyGivenReviewsAsync(int userId, int page, int pageSize);
    Task<bool> CanReviewAsync(int buyerId, int sellerId);
    Task<ReviewDto> CreateReviewAsync(int buyerId, CreateReviewDto dto);
    Task DeleteReviewAsync(int reviewId, int userId, bool isAdmin);
}
