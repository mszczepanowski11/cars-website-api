namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class BulkAdvertActionDto
{
    public List<int> Ids { get; set; } = new();

    // "activate" | "deactivate" | "delete" | "markSold" | "renew"
    public string Action { get; set; } = string.Empty;
}

public class BulkActionResultDto
{
    public List<int> Succeeded { get; set; } = new();
    public List<BulkActionErrorDto> Failed { get; set; } = new();
}

public class BulkActionErrorDto
{
    public int Id { get; set; }
    public string Error { get; set; } = string.Empty;
}
