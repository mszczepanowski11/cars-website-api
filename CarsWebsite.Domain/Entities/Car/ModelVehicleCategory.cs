namespace cars_website_api.CarsWebsite.Domain.Entities;

// Taksonomia Etap 3: scopes a Model to one or more VehicleCategories, mirroring the long-standing
// brand-level mapping (`brandvehiclecategories`). Without it, a brand that spans categories - BMW
// sells auta osobowe, dostawcze AND motocykle - listed every one of its models under every one of
// its categories, so picking "Motocykle" then BMW offered Seria 3 and X5 next to R 1250 GS.
//
// Modelled as an explicit join entity rather than a skip-navigation so it can be queried directly
// for the "unmapped means any category" wildcard in TaxonomyService.GetModelsByBrandAsync.
// Column names deliberately match EF's join-table convention already used by brandvehiclecategories.
public class ModelVehicleCategory
{
    public int ModelsId { get; set; }
    public int CategoriesId { get; set; }
}
