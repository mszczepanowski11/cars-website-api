namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }

    public ICollection<Model> Models { get; set; }
}