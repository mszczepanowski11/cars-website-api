using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class AdvertImageService : IAdvertImageService
{
    private readonly AppDbContext _context;
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<AdvertImageService> _logger;

    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public AdvertImageService(AppDbContext context, Cloudinary cloudinary, ILogger<AdvertImageService> logger)
    {
        _context = context;
        _cloudinary = cloudinary;
        _logger = logger;
    }

    private static void ValidateMagicBytes(IFormFile file, string declaredMime)
    {
        Span<byte> header = stackalloc byte[12];
        using var s = file.OpenReadStream();
        var read = s.Read(header);
        if (read < 3) throw new BadHttpRequestException("Plik jest uszkodzony lub pusty.");
        bool valid = declaredMime switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png"  => read >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            "image/webp" => read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
            _ => false
        };
        if (!valid)
            throw new BadHttpRequestException($"Zawartość pliku nie zgadza się z deklarowanym typem ({declaredMime}).");
    }

    public async Task<string> UploadAdvertImageAsync(int advertId, IFormFile file, int userId)
    {
        _logger.LogInformation("[ImgSvc] Start: advertId={AdvertId} userId={UserId} file={File} size={Size}B mime={Mime}",
            advertId, userId, file.FileName, file.Length, file.ContentType);

        var mime = file.ContentType.ToLowerInvariant();
        if (!AllowedMimeTypes.Contains(mime))
        {
            _logger.LogWarning("[ImgSvc] Rejected mime={Mime}", mime);
            throw new BadHttpRequestException($"Niedozwolony typ pliku: {mime}. Dozwolone: JPEG, PNG, WebP.");
        }
        if (file.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning("[ImgSvc] Rejected size={Size}B", file.Length);
            throw new BadHttpRequestException("Plik przekracza limit 10 MB.");
        }
        ValidateMagicBytes(file, mime);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            // Derive extension from MIME type as fallback (happens when Nuxt sends blob without ext)
            extension = mime switch
            {
                "image/jpeg" => ".jpg",
                "image/png"  => ".png",
                "image/webp" => ".webp",
                _ => extension
            };
            _logger.LogDebug("[ImgSvc] Extension derived from mime: {Ext}", extension);
        }
        if (!AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning("[ImgSvc] Rejected extension={Ext}", extension);
            throw new BadHttpRequestException($"Niedozwolone rozszerzenie pliku: {extension}.");
        }

        var advert = await _context.CarAdverts
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == advertId);
        if (advert == null) throw new KeyNotFoundException($"Ogłoszenie {advertId} nie istnieje.");
        if (advert.UserId != userId) throw new UnauthorizedAccessException("Nie jesteś właścicielem tego ogłoszenia.");
        if (advert.Images.Count >= 20) throw new BadHttpRequestException("Ogłoszenie może mieć maksymalnie 20 zdjęć.");

        var publicId = $"adverts/{advertId}/{Guid.NewGuid()}";
        _logger.LogInformation("[ImgSvc] Uploading to Cloudinary: publicId={PublicId}", publicId);

        ImageUploadResult result;
        using (var stream = file.OpenReadStream())
        {
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                PublicId = publicId,
                Overwrite = false,
                Transformation = new Transformation().Quality("auto"),
            };
            result = await _cloudinary.UploadAsync(uploadParams);
        }

        if (result.Error != null)
        {
            _logger.LogError("[ImgSvc] Cloudinary error: {Error} (httpStatus={Status})",
                result.Error.Message, result.StatusCode);
            throw new InvalidOperationException($"Błąd Cloudinary: {result.Error.Message}");
        }

        var url = result.SecureUrl.ToString();
        var isMain = advert.Images.Count == 0;
        _logger.LogInformation("[ImgSvc] Cloudinary OK: url={Url} isMain={IsMain}", url, isMain);

        var image = new AdvertImage { AdvertId = advertId, Url = url, IsMain = isMain };
        _context.AdvertImages.Add(image);
        await _context.SaveChangesAsync();

        _logger.LogInformation("[ImgSvc] DB saved: imageId={ImageId}", image.Id);
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
        {
            var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
            if (result.Error != null)
                _logger.LogWarning("Cloudinary delete failed for {PublicId}: {Error}", publicId, result.Error.Message);
        }

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
