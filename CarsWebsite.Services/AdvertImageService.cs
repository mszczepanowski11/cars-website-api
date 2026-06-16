using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class AdvertImageService : IAdvertImageService
{
    private readonly AppDbContext _context;
    private readonly Cloudinary _cloudinary;

    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public AdvertImageService(AppDbContext context, Cloudinary cloudinary)
    {
        _context = context;
        _cloudinary = cloudinary;
    }

    public async Task<string> UploadAdvertImageAsync(int advertId, IFormFile file, int userId)
    {
        if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            throw new BadHttpRequestException("Invalid file type. Only JPEG, PNG, and WebP images are allowed.");
        if (file.Length > MaxFileSizeBytes)
            throw new BadHttpRequestException("File size exceeds the 10 MB limit.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new BadHttpRequestException("Invalid file extension.");

        var advert = await _context.CarAdverts
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == advertId);
        if (advert == null) throw new KeyNotFoundException("Advert not found");
        if (advert.UserId != userId) throw new UnauthorizedAccessException("You do not own this advert.");
        if (advert.Images.Count >= 20) throw new BadHttpRequestException("Maximum of 20 images per advert.");

        using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            PublicId = $"adverts/{advertId}/{Guid.NewGuid()}",
            Overwrite = false,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto"),
        };

        var result = await _cloudinary.UploadAsync(uploadParams);
        if (result.Error != null)
            throw new InvalidOperationException($"Image upload failed: {result.Error.Message}");

        var url = result.SecureUrl.ToString();
        var isMain = advert.Images.Count == 0;

        _context.AdvertImages.Add(new AdvertImage { AdvertId = advertId, Url = url, IsMain = isMain });
        await _context.SaveChangesAsync();
        return url;
    }

    public async Task SetMainImageAsync(int advertId, int imageId, int userId)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == advertId);
        if (advert == null) throw new KeyNotFoundException("Advert not found");
        var image = advert.Images.FirstOrDefault(i => i.Id == imageId);
        if (image == null) throw new KeyNotFoundException("Image not found");
        if (advert.UserId != userId) throw new UnauthorizedAccessException("You do not own this advert.");

        foreach (var img in advert.Images) img.IsMain = false;
        image.IsMain = true;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteImageAsync(int advertId, int imageId, int userId)
    {
        var image = await _context.AdvertImages
            .Include(i => i.Advert)
            .FirstOrDefaultAsync(i => i.Id == imageId && i.AdvertId == advertId);
        if (image == null) throw new KeyNotFoundException("Image not found");
        if (image.Advert.UserId != userId) throw new UnauthorizedAccessException("You do not own this advert.");

        var publicId = ExtractPublicId(image.Url);
        if (publicId != null)
            await _cloudinary.DestroyAsync(new DeletionParams(publicId));

        _context.AdvertImages.Remove(image);
        await _context.SaveChangesAsync();
    }

    // Extracts the Cloudinary public_id from a secure URL.
    // URL format: https://res.cloudinary.com/{cloud}/image/upload/v{version}/{public_id}.{ext}
    private static string? ExtractPublicId(string url)
    {
        try
        {
            var segments = new Uri(url).AbsolutePath.Split('/');
            var uploadIdx = Array.IndexOf(segments, "upload");
            if (uploadIdx < 0) return null;
            var start = uploadIdx + 1;
            if (start < segments.Length && segments[start].StartsWith('v') && long.TryParse(segments[start][1..], out _))
                start++;
            var idWithExt = string.Join("/", segments[start..]);
            var dot = idWithExt.LastIndexOf('.');
            return dot > 0 ? idWithExt[..dot] : idWithExt;
        }
        catch { return null; }
    }
}
