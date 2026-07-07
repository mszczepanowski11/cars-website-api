namespace cars_website_api.CarsWebsite.Interfaces;

public interface IAdvertImageService
{
    Task<string> UploadAdvertImageAsync(int advertId, IFormFile file, int userId, bool isAdmin = false);
    Task SetMainImageAsync(int advertId, int imageId, int userId, bool isAdmin = false);
    Task DeleteImageAsync(int advertId, int imageId, int userId, bool isAdmin = false);
}