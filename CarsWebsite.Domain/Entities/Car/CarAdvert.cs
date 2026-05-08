using CarsWebsite;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class CarAdvert : Advert
{
    public int? VehicleCategoryId { get; set; }
    public VehicleCategory? VehicleCategory { get; set; }

    public int BrandId { get; set; }
    public Brand Brand { get; set; }
    public int ModelId { get; set; }
    public Model Model { get; set; }
    public int? GenerationId { get; set; }
    public Generation Generation { get; set; }
    public int? EngineVersionId { get; set; }
    public EngineVersion EngineVersion { get; set; }
    public int FuelTypeId { get; set; }
    public FuelType FuelType { get; set; }
    public int GearboxId { get; set; }
    public Gearbox Gearbox { get; set; }
    public int BodyTypeId { get; set; }
    public BodyType BodyType { get; set; }

    public int Year { get; set; }
    public int Mileage { get; set; }
    public int PowerHP { get; set; }
    public int PowerKW { get; set; }
    public int EngineSize { get; set; }

    public ICollection<AdvertFeature> AdvertFeatures { get; set; }
}