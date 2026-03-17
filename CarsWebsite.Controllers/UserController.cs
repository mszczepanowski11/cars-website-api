using System.Threading.Tasks;
using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Services;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;


namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
   private readonly UserService _userService;

   public UserController(UserService userService)
   {
      _userService = userService;
   }
   
   [Authorize]
   [HttpGet("me")]
   public async Task<IActionResult> GetUser()
   {
      var token = Request.Headers["Authorization"]
         .ToString()
         .Replace("Bearer ", "");;
      Console.WriteLine(token);
      
      if (string.IsNullOrEmpty(token))
         return Unauthorized();

      var user = await _userService.GetByToken(token);

      if (user == null)
         return NotFound("User not found or token is invalid.");

      return Ok(user);
   }
}