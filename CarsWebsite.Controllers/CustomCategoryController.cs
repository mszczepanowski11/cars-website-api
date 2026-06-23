using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using cars_website_api.CarsWebsite.Domain.Entities;

[ApiController]
[Route("api/custom-categories")]
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
        var request = new CustomCategoryRequest
        {
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

public record CreateCustomCategoryDto(string CategoryName, string? Description, string? ParametersJson);
