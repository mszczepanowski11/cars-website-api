namespace cars_website_api.CarsWebsite.Interfaces;

public interface IReviewService
{
    Task<object> GetSellerReviewsAsync(int sellerId, int page, int pageSize);
}
