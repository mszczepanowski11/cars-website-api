using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Generation
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    [MaxLength(100)] public string Name { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    [MaxLength(100)] public string Slug { get; set; }

    // Physical dimensions (audit §5): identical for every advert of this generation, so they
    // belong here rather than on CarAdvert where every listing would duplicate the same values.
    // All nullable/default-per-generation - an individual advert can still override on the rare
    // case a facelift within the same generation actually differs (extra trunk liner, etc.).
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public int? WheelbaseMm { get; set; }
    public int? TrunkCapacityL { get; set; }
    public int? DefaultSeatsCount { get; set; }
    public int? DefaultDoorsCount { get; set; }

    public Model Model { get; set; }
    public ICollection<EngineVersion> EngineVersions { get; set; }
    public ICollection<Trim> Trims { get; set; } = new List<Trim>();
}