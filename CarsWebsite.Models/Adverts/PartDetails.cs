using Microsoft.EntityFrameworkCore;

namespace CarsWebsite;

[Owned]
public class PartDetails
{
    public string? Category { get; set; }
    public string? PartNumber { get; set; }
    public string? Condition { get; set; }
}