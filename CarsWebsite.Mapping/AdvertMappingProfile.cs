using AutoMapper;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Car;
using CarsWebsite;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;

public class AdvertMappingProfile : Profile
{
    public AdvertMappingProfile()
    {
        CreateMap<Brand, BrandDto>();
        CreateMap<Model, ModelDto>();
        CreateMap<Generation, GenerationDto>();
        CreateMap<EngineVersion, EngineVersionDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.EngineName))
            .ForMember(dest => dest.Horsepower, opt => opt.MapFrom(src => src.PowerHP))
            .ForMember(dest => dest.FuelTypeName, opt => opt.MapFrom(src => src.FuelType != null ? src.FuelType.Name : null));
        CreateMap<FuelType, FuelTypeDto>();
        CreateMap<Gearbox, GearboxDto>();
        CreateMap<BodyType, BodyTypeDto>();
        CreateMap<DriveType, DriveTypeDto>();
        CreateMap<CarColor, CarColorDto>();

        CreateMap<FeatureCategory, FeatureCategoryDto>()
            .ForMember(dest => dest.Features, opt => opt.MapFrom(src => src.Features));
        CreateMap<Feature, FeatureDto>();

        CreateMap<AdvertImage, AdvertImageDto>();

        CreateMap<CarAdvert, CarAdvertResponseDto>()
            .ForMember(dest => dest.Features, opt => opt.MapFrom(src =>
                src.AdvertFeatures.Select(af => af.Feature)))
            .ForMember(dest => dest.Images, opt => opt.MapFrom(src => src.Images))
            .ForMember(dest => dest.Color, opt => opt.MapFrom(src => src.CarColor))
            .ForMember(dest => dest.ViewCount, opt => opt.Ignore())
            .ForMember(dest => dest.SoldAt, opt => opt.MapFrom(src => src.SoldAt));

        CreateMap<CreateCarAdvertDto, CarAdvert>()
            .ForMember(dest => dest.AdvertFeatures, opt => opt.Ignore())
            .ForMember(dest => dest.Images, opt => opt.Ignore())
            .ForMember(dest => dest.Slug, opt => opt.Ignore())
            .ForMember(dest => dest.Badge, opt => opt.Ignore())
            .ForMember(dest => dest.BadgeExpiresAt, opt => opt.Ignore())
            .ForMember(dest => dest.CarColor, opt => opt.Ignore())
            .ForMember(dest => dest.DriveType, opt => opt.Ignore())
            .ForMember(dest => dest.VehicleCategory, opt => opt.Ignore())
            .ForMember(dest => dest.Brand, opt => opt.Ignore())
            .ForMember(dest => dest.Model, opt => opt.Ignore())
            .ForMember(dest => dest.Generation, opt => opt.Ignore())
            .ForMember(dest => dest.EngineVersion, opt => opt.Ignore())
            .ForMember(dest => dest.FuelType, opt => opt.Ignore())
            .ForMember(dest => dest.Gearbox, opt => opt.Ignore())
            .ForMember(dest => dest.BodyType, opt => opt.Ignore())
            .ForMember(dest => dest.createdBy, opt => opt.Ignore());

        CreateMap<UpdateCarAdvertDto, CarAdvert>()
            .ForMember(dest => dest.AdvertFeatures, opt => opt.Ignore())
            .ForMember(dest => dest.Images, opt => opt.Ignore())
            .ForMember(dest => dest.Slug, opt => opt.Ignore())
            .ForMember(dest => dest.Badge, opt => opt.Ignore())
            .ForMember(dest => dest.BadgeExpiresAt, opt => opt.Ignore())
            .ForMember(dest => dest.CarColor, opt => opt.Ignore())
            .ForMember(dest => dest.DriveType, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow));
    }
}
