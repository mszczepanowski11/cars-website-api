using System.ComponentModel.DataAnnotations;
using cars_website_api.CarsWebsite.DTOs.Advert;

namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminCreateClientAdvertDto
    {
        [Required] [MaxLength(200)] public string FullName { get; set; } = string.Empty;
        [Required] [EmailAddress] [MaxLength(256)] public string Email { get; set; } = string.Empty;
        [Required] [MinLength(9)] [MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
        [Required] public CreateCarAdvertDto Advert { get; set; } = null!;
    }

    public class AdminCreateClientAdvertResultDto
    {
        public int UserId { get; set; }
        public int AdvertId { get; set; }
        public bool WasNewAccount { get; set; }
    }
}
