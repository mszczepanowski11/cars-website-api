using cars_website_api.CarsWebsite.DTOs.Car;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CarAdvertResponseDto
{
    
        public int Id { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }

        public decimal Price { get; set; }
        public int Year { get; set; }
        public int Mileage { get; set; }

        public BrandDto Brand { get; set; }
        public ModelDto Model { get; set; }
        public GenerationDto Generation { get; set; }
        public EngineVersionDto EngineVersion { get; set; }

        public FuelTypeDto FuelType { get; set; }
        public GearboxDto Gearbox { get; set; }
        public BodyTypeDto BodyType { get; set; }

        public List<FeatureDto> Features { get; set; }
        public List<AdvertImageDto> Images { get; set; }

        public DateTime CreatedAt { get; set; }
}