using cars_website_api.CarsWebsite.Enums;
using Microsoft.EntityFrameworkCore;
using Mysqlx.Expect;

namespace CarsWebsite;

[Owned]
public class VehicleDetails
{
    public VehicleType? VehicleType { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public int? Mileage { get; set; }
    public FuelType? FuelType { get; set; }
    public Transmission? Transmission { get; set; }
    public Condition? Condition { get; set; }
    public string? Color { get; set; }
    public int? EngineSize { get; set; }
    public int? HorsePower { get; set; }
}