using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Model
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    [MaxLength(100)] public string Name { get; set; }
    [MaxLength(100)] public string Slug { get; set; }

    public Brand Brand { get; set; }
    public ICollection<Generation> Generations { get; set; }
}