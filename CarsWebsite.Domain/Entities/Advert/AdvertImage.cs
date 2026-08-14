using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsite;

public class AdvertImage
{
    public int Id { get; set; }
    public int AdvertId { get; set; }
    public CarAdvert Advert { get; set; }

    public string Url { get; set; }
    public int Order { get; set; } 
    public bool IsMain { get; set; }

}