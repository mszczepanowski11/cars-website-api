namespace cars_website_api.CarsWebsite.Interfaces;

public interface IAdvertImageService
{
    Task<string> UploadAdvertImageAsync(int advertId, IFormFile file, int userId);
    Task SetMainImageAsync(int advertId, int imageId, int userId);
    Task DeleteImageAsync(int advertId, int imageId, int userId);
}