using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;


public class AdvertService: IAdvertService
{
    private readonly AppDbContext _context;
    public AdvertService(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<Advert> AddAdvert(Advert model)
    {
        _context.Adverts.Add(model);
        await _context.SaveChangesAsync();
 
        return model;
    }
    
    public async Task<Advert?> GetById(int id)
    {
        return await _context.Adverts
            .Include(a => a.createdBy)
            .FirstOrDefaultAsync(a => a.Id == id);
    }


    public async Task<List<Advert>> GetAll()
    {
        return await _context.Adverts
            .Include(a => a.createdBy)
            .ToListAsync();
    }


    public async Task<List<Advert>> GetByUserId(int userId)
    {
        return await _context.Adverts
            .Include(a => a.createdBy)
            .Where(a => a.UserId == userId)
            .ToListAsync();
    }
    
    public async Task DeleteAdvert(int id)
    {
        var advert = await _context.Adverts.FindAsync(id);
 
        if (advert != null)
        {
            _context.Adverts.Remove(advert);
            await _context.SaveChangesAsync();
        }
    }
}