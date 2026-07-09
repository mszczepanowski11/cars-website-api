using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Controllers;

// Meta Platform Terms 3(d)(i) requires every app using Facebook Login to expose a Data Deletion
// Callback (or Data Deletion Instructions URL) and to honor Deauthorize callbacks - these are
// called directly by Meta, not by end users, so no [Authorize] and no login-style rate limiting.
[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting("global")]
public class MetaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaController> _logger;

    public MetaController(AppDbContext db, IConfiguration configuration, ILogger<MetaController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    private string? GetAppSecret() =>
        _configuration["Facebook:AppSecret"] ?? Environment.GetEnvironmentVariable("FACEBOOK_APP_SECRET");

    // Decodes and verifies Meta's signed_request format: "{base64url signature}.{base64url json
    // payload}", HMAC-SHA256 of the payload segment keyed by the App Secret. Returns the parsed
    // payload only if the signature actually matches - never trust user_id from an unverified one.
    private JsonElement? ParseSignedRequest(string signedRequest, string appSecret)
    {
        var parts = signedRequest.Split('.', 2);
        if (parts.Length != 2) return null;

        var expectedSig = Base64UrlDecode(parts[0]);
        var payloadBytes = Base64UrlDecode(parts[1]);
        if (expectedSig == null || payloadBytes == null) return null;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var actualSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[1]));
        if (!CryptographicOperations.FixedTimeEquals(actualSig, expectedSig)) return null;

        try { return JsonSerializer.Deserialize<JsonElement>(payloadBytes); }
        catch { return null; }
    }

    private static byte[]? Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try { return Convert.FromBase64String(s); }
        catch { return null; }
    }

    [HttpPost("data-deletion")]
    public async Task<IActionResult> DataDeletion([FromForm] string signed_request)
    {
        var appSecret = GetAppSecret();
        if (string.IsNullOrEmpty(appSecret))
        {
            _logger.LogError("[Meta] DataDeletion: Facebook:AppSecret nie skonfigurowany.");
            return StatusCode(500);
        }

        var payload = ParseSignedRequest(signed_request, appSecret);
        if (payload == null || !payload.Value.TryGetProperty("user_id", out var uidProp))
        {
            _logger.LogWarning("[Meta] DataDeletion: nieprawidłowy lub niezweryfikowany signed_request.");
            return BadRequest();
        }

        var facebookUserId = uidProp.GetString() ?? "";
        var confirmationCode = Guid.NewGuid().ToString("N");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.FacebookId == facebookUserId);
        if (user != null)
        {
            // Only the Facebook-sourced identifier is removed - the CARIZO account itself (and
            // any data the user added independently, e.g. a password or adverts) is not deleted,
            // since Platform Data deletion and account deletion are two different user requests.
            user.FacebookId = null;
        }

        _db.DataDeletionRequests.Add(new DataDeletionRequest
        {
            FacebookUserId = facebookUserId,
            UserId = user?.Id,
            ConfirmationCode = confirmationCode,
            RequestedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("[Meta] DataDeletion completed facebookUserId={FbId} confirmationCode={Code}", facebookUserId, confirmationCode);

        var siteUrl = (_configuration["SiteUrl"] ?? "https://carizo.eu").TrimEnd('/');
        return Ok(new
        {
            url = $"{siteUrl}/data-deletion-status?id={confirmationCode}",
            confirmation_code = confirmationCode,
        });
    }

    [HttpPost("deauthorize")]
    public async Task<IActionResult> Deauthorize([FromForm] string signed_request)
    {
        var appSecret = GetAppSecret();
        if (string.IsNullOrEmpty(appSecret)) return StatusCode(500);

        var payload = ParseSignedRequest(signed_request, appSecret);
        if (payload == null || !payload.Value.TryGetProperty("user_id", out var uidProp))
            return BadRequest();

        var facebookUserId = uidProp.GetString() ?? "";
        var user = await _db.Users.FirstOrDefaultAsync(u => u.FacebookId == facebookUserId);
        if (user != null)
        {
            user.FacebookId = null;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("[Meta] Deauthorize facebookUserId={FbId}", facebookUserId);
        return Ok();
    }

    [HttpGet("data-deletion-status/{code}")]
    public async Task<IActionResult> DataDeletionStatus(string code)
    {
        var request = await _db.DataDeletionRequests.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ConfirmationCode == code);
        if (request == null) return NotFound();

        return Ok(new
        {
            confirmationCode = request.ConfirmationCode,
            requestedAt = request.RequestedAt,
            completed = request.CompletedAt != null,
        });
    }
}
