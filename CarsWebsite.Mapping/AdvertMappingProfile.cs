using AutoMapper;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Car;
using CarsWebsite;

public class AdvertMappingProfile : Profile
{
    public AdvertMappingProfile()
    {
        
        CreateMap<Brand, BrandDto>();
        CreateMap<Model, ModelDto>();
        CreateMap<Generation, GenerationDto>();
        CreateMap<EngineVersion, EngineVersionDto>();
        CreateMap<FuelType, FuelTypeDto>();
        CreateMap<Gearbox, GearboxDto>();
        CreateMap<BodyType, BodyTypeDto>();

     
        CreateMap<FeatureCategory, FeatureCategoryDto>();
        CreateMap<Feature, FeatureDto>();
        
        CreateMap<AdvertImage, AdvertImageDto>();
        
        CreateMap<CarAdvert, CarAdvertResponseDto>()
            .ForMember(dest => dest.Features, opt => opt.MapFrom(src =>
                src.AdvertFeatures.Select(af => af.Feature)))
            .ForMember(dest => dest.Images, opt => opt.MapFrom(src => src.Images));

       
        CreateMap<CreateCarAdvertDto, CarAdvert>()
            .ForMember(dest => dest.AdvertFeatures, opt => opt.Ignore())
            .ForMember(dest => dest.Images, opt => opt.Ignore());

        CreateMap<UpdateCarAdvertDto, CarAdvert>()
            .ForMember(dest => dest.AdvertFeatures, opt => opt.Ignore())
            .ForMember(dest => dest.Images, opt => opt.Ignore());
    }
}