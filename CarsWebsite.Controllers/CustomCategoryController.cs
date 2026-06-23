using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using cars_website_api.CarsWebsite.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

[ApiController]
[Route("api/custom-categories")]
[Authorize]
[EnableRateLimiting("global")]
public class CustomCategoryController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomCategoryController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CategoryName))
            return BadRequest("categoryName is required");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var request = new CustomCategoryRequest
        {
            UserId = userId,
            CategoryName = dto.CategoryName,
            Description = dto.Description,
            ParametersJson = dto.ParametersJson,
            CreatedAt = DateTime.UtcNow
        };
        _db.CustomCategoryRequests.Add(request);
        await _db.SaveChangesAsync();
        return Ok(new { request.Id, request.Status, message = "Wniosek wysłany do weryfikacji" });
    }
}

public record CreateCustomCategoryDto(
    [MaxLength(100)] string CategoryName,
    [MaxLength(1000)] string? Description,
    [MaxLength(5000)] string? ParametersJson);
