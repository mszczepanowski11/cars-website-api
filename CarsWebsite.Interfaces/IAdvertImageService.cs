namespace cars_website_api.CarsWebsite.Interfaces;

public interface IAdvertImageService
{
    Task<string> UploadAdvertImageAsync(int advertId, IFormFile file);
    Task SetMainImageAsync(int advertId, int imageId);
    Task DeleteImageAsync(int advertId, int imageId, int userId);
}