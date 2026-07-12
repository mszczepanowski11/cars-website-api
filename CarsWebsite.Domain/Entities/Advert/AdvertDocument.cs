namespace CarsWebsite;

// Faza 8 of the category/attribute restructure: replaces the single YoutubeUrl/PdfBrochureUrl
// columns on CarAdvert with proper multiple-documents-per-advert support. AdvertId FKs to the
// base Advert (not CarAdvert specifically) - same TPT-shared-Id relationship as AdvertAttributeValue.
public enum AdvertDocumentType { Video, Pdf, Image, Other }

public class AdvertDocument
{
    public int Id { get; set; }
    public int AdvertId { get; set; }
    public Advert Advert { get; set; }

    public string Url { get; set; } = string.Empty;
    public AdvertDocumentType Type { get; set; }
    public string? Label { get; set; }
    public int SortOrder { get; set; }
}
