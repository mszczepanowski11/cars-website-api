
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CreateAdvertDto
{
  
    public string Title { get; set; }
    public decimal Price { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public List<string>? Images { get; set; }
    
}