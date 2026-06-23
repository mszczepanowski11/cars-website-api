using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/internal")]
[AllowAnonymous]
public class InternalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public InternalController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private bool IsAuthorized()
    {
        var secret = _config["INTERNAL_SERVICE_SECRET"]
                  ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET");
        if (string.IsNullOrEmpty(secret)) return false;
        Request.Headers.TryGetValue("X-Internal-Secret", out var provided);
        return provided == secret;
    }

    /// <summary>
    /// Temporary dev tool — deletes all users and their tokens.
    /// Protected by X-Internal-Secret header.
    /// </summary>
    [HttpDelete("users")]
    public async Task<IActionResult> DeleteAllUsers()
    {
        if (!IsAuthorized())
            return Unauthorized(new { message = "Missing or invalid X-Internal-Secret header." });

        await _db.Database.ExecuteSqlRawAsync("DELETE FROM `refreshtokens`");
        var count = await _db.Database.ExecuteSqlRawAsync("DELETE FROM `users`");

        return Ok(new { message = $"Deleted all users and their refresh tokens.", deletedUsers = count });
    }
}
