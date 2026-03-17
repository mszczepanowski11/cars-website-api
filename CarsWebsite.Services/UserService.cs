using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class UserService
{
   private readonly AppDbContext _context;

   public UserService(AppDbContext context)
   {
      _context = context;
   }

   public async Task<User> AddUser(User model)
   {
      _context.Users.Add(model);
      await _context.SaveChangesAsync();
      
      return model;
   }
   
   public async Task<User?> GetById(int id)
   {
      return await _context.Users.FindAsync(id);
   }

   public async Task<User?> GetByToken(string token)
   {
      var handler = new JwtSecurityTokenHandler();

      if (!handler.CanReadToken(token))
         return null;

      var jwtToken = handler.ReadJwtToken(token);

      var userIdClaim = jwtToken.Claims
         .FirstOrDefault(c => c.Type == "nameid"
                              || c.Type == ClaimTypes.NameIdentifier)?.Value;

      if (userIdClaim == null)
         return null;

      return await _context.Users.FindAsync(int.Parse(userIdClaim));
   }
  
}