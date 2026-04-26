namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Model
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }

    public Brand Brand { get; set; }
    public ICollection<Generation> Generations { get; set; }
}