namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsBlocked { get; set; }
        public DateTime? BlockedAt { get; set; }
        public string? BlockedReason { get; set; }
        public int AdvertCount { get; set; }
        public string AccountType { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
