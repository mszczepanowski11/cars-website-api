using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

// Faza 8 of the category/attribute restructure: read/write shapes for AdvertDocument, the
// multi-document/video replacement for the single YoutubeUrl/PdfBrochureUrl columns.
public class AdvertDocumentDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public AdvertDocumentType Type { get; set; }
    public string? Label { get; set; }
    public int SortOrder { get; set; }
}

public class CreateAdvertDocumentDto
{
    [Required, MaxLength(1000)]
    public string Url { get; set; } = string.Empty;

    public AdvertDocumentType Type { get; set; }

    [MaxLength(200)]
    public string? Label { get; set; }

    public int SortOrder { get; set; }
}
