using cars_website_api.CarsWebsite.DTOs.Partner;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IPartnerImportService
{
    // Parses the raw XML or CSV feed body, upserts CarAdverts (matched by ExternalId within this
    // partner) via IAdvertService, and writes a PartnerImportLog row summarizing the run.
    Task<PartnerImportLogResponseDto> ImportAsync(CarsWebsite.Partner partner, string content, CarsWebsite.PartnerFeedFormat format);
}
