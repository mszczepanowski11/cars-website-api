using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AdvertController : Controller
{
    private readonly IAdvertService _advertService;
    private readonly UserService _userService;
    
    public AdvertController(IAdvertService advertService, UserService userService)
    {
        _advertService = advertService;
        _userService = userService;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var adverts = await _advertService.GetAll();
        return Ok(adverts);
    }
    
    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> GetByUser()
    {
        var user = await GetCurrentUser();
        if (user == null) return Unauthorized();
 
        var adverts = await _advertService.GetByUserId(user.Id);
        return Ok(adverts);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var advert = await _advertService.GetById(id);
 
        if (advert == null)
            return NotFound("Advert not found.");
 
        return Ok(advert);
    }

    
    [HttpPost]
    public async Task<IActionResult> AddAdvert([FromBody] CreateAdvertDto dto)
    {
        
        var token = Request.Headers["Authorization"]
            .ToString()
            .Replace("Bearer ", "");
 
        var user = await _userService.GetByToken(token);
 
        if (user == null)
            return Unauthorized();
        
        var advert = new Advert
        {
            AdvertType = dto.AdvertType,
            Title = dto.Title,
            Price = dto.Price,
            Description = dto.Description,
            Location = dto.Location,
            Images = dto.Images ?? new List<string>(),
            UserId = user.Id,
            VehicleDetails = dto.VehicleDetails,
            PartDetails = dto.PartDetails
        };
        
        var created = await _advertService.AddAdvert(advert);
        
        return Ok(created);
    }
    
    private async Task<User?> GetCurrentUser()
    {
        var token = Request.Headers["Authorization"]
            .ToString()
            .Replace("Bearer ", "");
 
        return await _userService.GetByToken(token);
    }
    
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAdvert(int id)
    {
        var user = await GetCurrentUser();
        if (user == null) return Unauthorized();
 
        var advert = await _advertService.GetById(id);
 
        if (advert == null)
            return NotFound("Advert not found.");
 
        if (advert.UserId != user.Id)
            return Forbid();
 
        await _advertService.DeleteAdvert(id);
        return NoContent();
    }
}