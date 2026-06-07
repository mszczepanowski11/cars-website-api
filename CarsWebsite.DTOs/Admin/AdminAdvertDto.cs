namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminAdvertDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "PLN";
        public bool IsHidden { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UserId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Region { get; set; }
    }
}