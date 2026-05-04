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

    public async Task<string> UploadAdvertImageAsync(int advertId, IFormFile file)
    {
        var advert = await _context.CarAdverts.FindAsync(advertId);
        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        var folderPath = Path.Combine(_env.WebRootPath, "uploads", "adverts", advertId.ToString());
        Directory.CreateDirectory(folderPath);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
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

    public async Task DeleteImageAsync(int advertId, int imageId)
    {
        var image = await _context.AdvertImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.AdvertId == advertId);

        if (image == null)
            throw new KeyNotFoundException("Image not found");

        var filePath = Path.Combine(_env.WebRootPath, image.Url.TrimStart('/'));

        if (File.Exists(filePath))
            File.Delete(filePath);

        _context.AdvertImages.Remove(image);
        await _context.SaveChangesAsync();
    }
}
