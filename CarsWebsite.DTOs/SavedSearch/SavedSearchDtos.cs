using System.ComponentModel.DataAnnotations;
using cars_website_api.CarsWebsite.DTOs.Advert;

namespace cars_website_api.CarsWebsite.DTOs.SavedSearch;

public class SavedSearchResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SearchCarAdvertDto Criteria { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public bool NotifyOnNew { get; set; }
    public int NewResultsCount { get; set; }
}

public class CreateSavedSearchDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public SearchCarAdvertDto Criteria { get; set; } = new();

    public bool NotifyOnNew { get; set; } = true;
}

public class UpdateSavedSearchDto
{
    [MaxLength(200)]
    public string? Name { get; set; }

    public SearchCarAdvertDto? Criteria { get; set; }

    public bool? NotifyOnNew { get; set; }
}

public class SetSavedSearchNotifyDto
{
    [Required]
    public bool NotifyOnNew { get; set; }
}
