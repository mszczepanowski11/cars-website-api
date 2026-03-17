using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IAdvertService
{
    Task<Advert> AddAdvert(Advert model);
    Task<Advert?> GetById(int id);
    Task<List<Advert>> GetAll();
    Task<List<Advert>> GetByUserId(int userId);
    Task DeleteAdvert(int id);  
}