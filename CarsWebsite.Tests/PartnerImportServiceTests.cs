using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarsWebsiteTests;

// Integration tests for the partner XML/CSV import pipeline (CTO audit Etap 2 - "najbardziej
// ryzykowna nieotestowana ścieżka, dokładnie ta, którą AKOL/44FOX uderzą"). Each test builds a
// real AppDbContext (EF Core InMemory), a real PartnerImportService, and feeds it real XML/CSV
// text exactly as it would arrive over POST /api/partner/adverts/import - no mocking of the
// service under test itself, only its Cloudinary/Meilisearch side dependencies (see
// TestDbContextFactory), which the import pipeline doesn't meaningfully exercise anyway.
public class PartnerImportServiceTests
{
    private static async Task<(AppDbContext Context, PartnerImportService Service, Partner Partner)> SetupAsync(string testName)
    {
        var context = TestDbContextFactory.CreateContext(testName);
        await TestDbContextFactory.SeedCategoryAsync(context);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@partner.test");
        var partner = new Partner
        {
            CompanyName = "TestPartner",
            ContactEmail = "partner@test.com",
            ApiKeyHash = "unused",
            LinkedUserId = user.Id,
            IsActive = true,
        };
        context.Partners.Add(partner);
        await context.SaveChangesAsync();

        var advertService = TestDbContextFactory.CreateAdvertService(context);
        var importService = new PartnerImportService(context, advertService, TestDbContextFactory.CreateDummyCloudinary());
        return (context, importService, partner);
    }

    [Fact]
    public async Task ImportAsync_CreatesNewAdvert_FromDefaultXmlSchema()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_CreatesNewAdvert_FromDefaultXmlSchema));
        var xml = """
            <Adverts>
              <Advert>
                <ExternalId>EXT-1</ExternalId>
                <Title>Audi A4 2020</Title>
                <Description>Testowy opis</Description>
                <Price>80000</Price>
                <Category>auta-osobowe</Category>
                <Brand>Audi</Brand>
                <Model>A4</Model>
                <Year>2020</Year>
                <Mileage>50000</Mileage>
              </Advert>
            </Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(1, log.ItemsTotal);
        Assert.Equal(1, log.ItemsCreated);
        Assert.Equal(0, log.ItemsFailed);
        var advert = await context.CarAdverts.Include(a => a.Brand).FirstAsync(a => a.ExternalId == "EXT-1");
        Assert.Equal("Audi", advert.Brand!.Name);
        Assert.Equal(partner.Id, advert.PartnerId);
    }

    [Fact]
    public async Task ImportAsync_SecondImportWithSameExternalId_UpdatesInPlaceAndRenewsExpiresAt()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_SecondImportWithSameExternalId_UpdatesInPlaceAndRenewsExpiresAt));
        var xml1 = """
            <Adverts><Advert>
                <ExternalId>EXT-2</ExternalId><Title>BMW 320d</Title><Price>90000</Price>
                <Category>auta-osobowe</Category><Brand>BMW</Brand><Model>3 Series</Model>
                <Year>2019</Year><Mileage>60000</Mileage>
            </Advert></Adverts>
            """;
        await service.ImportAsync(partner, xml1, PartnerFeedFormat.Xml);
        var firstAdvert = await context.CarAdverts.FirstAsync(a => a.ExternalId == "EXT-2");
        var advertId = firstAdvert.Id;
        // Simulate the bug this exact test guards against: an advert whose ExpiresAt has already
        // drifted close to expiry, exactly like one sitting untouched between two 6h sync cycles.
        firstAdvert.ExpiresAt = DateTime.UtcNow.AddDays(1);
        await context.SaveChangesAsync();

        var xml2 = """
            <Adverts><Advert>
                <ExternalId>EXT-2</ExternalId><Title>BMW 320d (zaktualizowany)</Title><Price>88000</Price>
                <Category>auta-osobowe</Category><Brand>BMW</Brand><Model>3 Series</Model>
                <Year>2019</Year><Mileage>61000</Mileage>
            </Advert></Adverts>
            """;
        var log = await service.ImportAsync(partner, xml2, PartnerFeedFormat.Xml);

        Assert.Equal(0, log.ItemsCreated);
        Assert.Equal(1, log.ItemsUpdated);
        var totalAdverts = await context.CarAdverts.CountAsync(a => a.ExternalId == "EXT-2");
        Assert.Equal(1, totalAdverts); // same row updated, not a second one created
        var updated = await context.CarAdverts.FirstAsync(a => a.Id == advertId);
        Assert.Equal("BMW 320d (zaktualizowany)", updated.Title);
        Assert.True(updated.ExpiresAt > DateTime.UtcNow.AddDays(80), "ExpiresAt must be renewed on every successful sync, not just on first import");
    }

    [Fact]
    public async Task ImportAsync_FieldMapping_ReadsPartnersOwnElementNames()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_FieldMapping_ReadsPartnersOwnElementNames));
        context.PartnerFieldMappings.AddRange(
            new PartnerFieldMapping { PartnerId = partner.Id, OurField = "ExternalId", SourcePath = "OfertaId" },
            new PartnerFieldMapping { PartnerId = partner.Id, OurField = "Title", SourcePath = "Naglowek" },
            new PartnerFieldMapping { PartnerId = partner.Id, OurField = "Price", SourcePath = "CenaNetto" },
            new PartnerFieldMapping { PartnerId = partner.Id, OurField = "Category", SourcePath = "Kategoria" },
            new PartnerFieldMapping { PartnerId = partner.Id, OurField = "Brand", SourcePath = "Marka" }
        );
        await context.SaveChangesAsync();

        var xml = """
            <Adverts><Advert>
                <OfertaId>AKOL-1</OfertaId><Naglowek>Skoda Octavia</Naglowek><CenaNetto>55000</CenaNetto>
                <Kategoria>auta-osobowe</Kategoria><Marka>Skoda</Marka><Model>Octavia</Model>
                <Year>2018</Year><Mileage>100000</Mileage>
            </Advert></Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(1, log.ItemsCreated);
        var advert = await context.CarAdverts.Include(a => a.Brand)
            .FirstAsync(a => a.ExternalId == "AKOL-1");
        Assert.Equal("Skoda Octavia", advert.Title);
        Assert.Equal("Skoda", advert.Brand!.Name);
        Assert.Equal(55000, advert.Price);
    }

    [Fact]
    public async Task ImportAsync_ValueMapping_TranslatesPartnersCategoryString()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_ValueMapping_TranslatesPartnersCategoryString));
        context.PartnerValueMappings.Add(new PartnerValueMapping
        {
            PartnerId = partner.Id, Field = "Category", ExternalValue = "Osobowe", InternalValue = "auta-osobowe",
        });
        await context.SaveChangesAsync();

        var xml = """
            <Adverts><Advert>
                <ExternalId>VAL-1</ExternalId><Title>Ford Focus</Title><Price>40000</Price>
                <Category>Osobowe</Category><Brand>Ford</Brand><Model>Focus</Model>
                <Year>2017</Year><Mileage>90000</Mileage>
            </Advert></Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(1, log.ItemsCreated);
        Assert.Equal(0, log.ItemsFailed);
        var advert = await context.CarAdverts.FirstAsync(a => a.ExternalId == "VAL-1");
        Assert.NotNull(advert.VehicleCategoryId);
    }

    [Fact]
    public async Task ImportAsync_UnmappedUnknownCategory_FailsOnlyThatItem()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_UnmappedUnknownCategory_FailsOnlyThatItem));
        var xml = """
            <Adverts>
              <Advert>
                <ExternalId>BAD-CAT</ExternalId><Title>Nieznana kategoria</Title><Price>10000</Price>
                <Category>nie-istnieje</Category>
              </Advert>
              <Advert>
                <ExternalId>GOOD-1</ExternalId><Title>Dobra oferta</Title><Price>20000</Price>
                <Category>auta-osobowe</Category>
              </Advert>
            </Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(2, log.ItemsTotal);
        Assert.Equal(1, log.ItemsCreated);
        Assert.Equal(1, log.ItemsFailed);
        Assert.Contains("nieznana kategoria", log.ErrorSummary);
        Assert.True(await context.CarAdverts.AnyAsync(a => a.ExternalId == "GOOD-1"));
        Assert.False(await context.CarAdverts.AnyAsync(a => a.ExternalId == "BAD-CAT"));
    }

    [Fact]
    public async Task ImportAsync_MissingDescription_DoesNotCrashTheWholeImport()
    {
        // Regression test for a real bug found while building the dedup feature: a null
        // Description used to throw a DbUpdateException at the FINAL SaveChangesAsync (after the
        // per-item loop), outside the per-item try/catch - one item without a description used to
        // 500 the entire request instead of just failing that one row.
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_MissingDescription_DoesNotCrashTheWholeImport));
        var xml = """
            <Adverts><Advert>
                <ExternalId>NO-DESC</ExternalId><Title>Brak opisu</Title><Price>15000</Price>
                <Category>auta-osobowe</Category>
            </Advert></Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(1, log.ItemsCreated);
        Assert.Equal(0, log.ItemsFailed);
    }

    [Fact]
    public async Task ImportAsync_VinMatch_FlagsSecondPartnersAdvertAsDuplicateOfFirst()
    {
        var context = TestDbContextFactory.CreateContext(nameof(ImportAsync_VinMatch_FlagsSecondPartnersAdvertAsDuplicateOfFirst));
        await TestDbContextFactory.SeedCategoryAsync(context);
        var userA = await TestDbContextFactory.SeedBusinessUserAsync(context, "akol@partner.test");
        var userB = await TestDbContextFactory.SeedBusinessUserAsync(context, "fox@partner.test");
        var partnerA = new Partner { CompanyName = "Akol", ContactEmail = "a@t.com", ApiKeyHash = "x", LinkedUserId = userA.Id, IsActive = true };
        var partnerB = new Partner { CompanyName = "Fox", ContactEmail = "b@t.com", ApiKeyHash = "x", LinkedUserId = userB.Id, IsActive = true };
        context.Partners.AddRange(partnerA, partnerB);
        await context.SaveChangesAsync();
        var advertService = TestDbContextFactory.CreateAdvertService(context);
        var service = new PartnerImportService(context, advertService, TestDbContextFactory.CreateDummyCloudinary());

        const string vin = "WAUZZZ8K9BA123456";
        var xmlA = $"""
            <Adverts><Advert>
                <ExternalId>AKOL-VIN</ExternalId><Title>Audi A4 od Akol</Title><Price>90000</Price>
                <Category>auta-osobowe</Category><Brand>Audi</Brand><Model>A4</Model>
                <Year>2021</Year><Mileage>30000</Mileage><Vin>{vin}</Vin>
            </Advert></Adverts>
            """;
        var xmlB = $"""
            <Adverts><Advert>
                <ExternalId>FOX-VIN</ExternalId><Title>Audi A4 od Fox</Title><Price>91000</Price>
                <Category>auta-osobowe</Category><Brand>Audi</Brand><Model>A4</Model>
                <Year>2021</Year><Mileage>30500</Mileage><Vin>{vin}</Vin>
            </Advert></Adverts>
            """;

        await service.ImportAsync(partnerA, xmlA, PartnerFeedFormat.Xml);
        var logB = await service.ImportAsync(partnerB, xmlB, PartnerFeedFormat.Xml);

        var canonical = await context.CarAdverts.FirstAsync(a => a.ExternalId == "AKOL-VIN");
        var duplicate = await context.CarAdverts.FirstAsync(a => a.ExternalId == "FOX-VIN");
        Assert.Null(canonical.DuplicateOfId);
        Assert.Equal(canonical.Id, duplicate.DuplicateOfId);
        Assert.Equal("VIN", duplicate.DuplicateMatchReason);
        Assert.Contains("duplikat", logB.ErrorSummary);
    }

    [Fact]
    public async Task ImportAsync_CsvFormat_ParsesCorrectly()
    {
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_CsvFormat_ParsesCorrectly));
        var csv = "externalid,title,price,category,brand,model,year,mileage\n" +
                   "CSV-1,Opel Astra,35000,auta-osobowe,Opel,Astra,2016,120000\n";

        var log = await service.ImportAsync(partner, csv, PartnerFeedFormat.Csv);

        Assert.Equal(1, log.ItemsCreated);
        var advert = await context.CarAdverts.Include(a => a.Brand).FirstAsync(a => a.ExternalId == "CSV-1");
        Assert.Equal("Opel", advert.Brand!.Name);
    }

    [Fact]
    public async Task ImportAsync_ImageRehostFails_StillCreatesTheAdvert()
    {
        // TestDbContextFactory's Cloudinary client uses fake credentials, so every re-host upload
        // genuinely fails here - exactly the case this test guards: a partner's image (dead link,
        // unreachable host, Cloudinary outage) must not take the whole item down with it. CTO audit
        // Etap 2 "re-hosting zdjęć partnera na Cloudinary zamiast linkowania".
        var (context, service, partner) = await SetupAsync(nameof(ImportAsync_ImageRehostFails_StillCreatesTheAdvert));
        var xml = """
            <Adverts><Advert>
                <ExternalId>IMG-1</ExternalId><Title>Renault Clio</Title><Price>25000</Price>
                <Category>auta-osobowe</Category><Brand>Renault</Brand><Model>Clio</Model>
                <Year>2015</Year><Mileage>140000</Mileage>
                <Images><Image>https://example.com/photo1.jpg</Image></Images>
            </Advert></Adverts>
            """;

        var log = await service.ImportAsync(partner, xml, PartnerFeedFormat.Xml);

        Assert.Equal(1, log.ItemsCreated);
        Assert.Equal(0, log.ItemsFailed);
        var advert = await context.CarAdverts.FirstAsync(a => a.ExternalId == "IMG-1");
        Assert.False(await context.AdvertImages.AnyAsync(i => i.AdvertId == advert.Id));
    }
}
