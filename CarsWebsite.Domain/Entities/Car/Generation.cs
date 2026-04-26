namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Generation
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    public string Name { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public string Slug { get; set; }

    public Model Model { get; set; }
    public ICollection<EngineVersion> EngineVersions { get; set; }
}