using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class AdvertImageService : IAdvertImageService
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public AdvertImageService(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public async Task<string> UploadAdvertImageAsync(int advertId, IFormFile file)
    {
        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            throw new BadHttpRequestException("Invalid file type. Only JPEG, PNG, and WebP images are allowed.");

        if (file.Length > MaxFileSizeBytes)
            throw new BadHttpRequestException("File size exceeds the 10 MB limit.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new BadHttpRequestException("Invalid file extension.");

        var advert = await _context.CarAdverts.FindAsync(advertId);
        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        if (string.IsNullOrEmpty(_env.WebRootPath))
            throw new InvalidOperationException("WebRootPath is not configured.");

        var folderPath = Path.Combine(_env.WebRootPath, "uploads", "adverts", advertId.ToString());
        Directory.CreateDirectory(folderPath);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(folderPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var relativeUrl = $"/uploads/adverts/{advertId}/{fileName}";

        var image = new AdvertImage
        {
            AdvertId = advertId,
            Url = relativeUrl,
            IsMain = false
        };

        _context.AdvertImages.Add(image);
        await _context.SaveChangesAsync();

        return relativeUrl;
    }

    public async Task SetMainImageAsync(int advertId, int imageId)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == advertId);

        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        var image = advert.Images.FirstOrDefault(i => i.Id == imageId);
        if (image == null)
            throw new KeyNotFoundException("Image not found");

        foreach (var img in advert.Images)
            img.IsMain = false;

        image.IsMain = true;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteImageAsync(int advertId, int imageId, int userId)
    {
        var image = await _context.AdvertImages
            .Include(i => i.Advert)
            .FirstOrDefaultAsync(i => i.Id == imageId && i.AdvertId == advertId);

        if (image == null)
            throw new KeyNotFoundException("Image not found");

        if (image.Advert.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this advert.");

        var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
        var filePath = Path.GetFullPath(Path.Combine(_env.WebRootPath, image.Url.TrimStart('/', '\\')));

        if (!filePath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Invalid image path.");

        if (File.Exists(filePath))
            File.Delete(filePath);

        _context.AdvertImages.Remove(image);
        await _context.SaveChangesAsync();
    }
}
