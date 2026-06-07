using System;

namespace CarsWebsite
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string PhoneNumber { get; set; }
        public string PasswordHash { get; set; }
        public AccountType AccountType { get; set; } = AccountType.Personal;
        public string? CompanyName { get; set; }
        public string? Nip { get; set; }
        public bool IsAdmin { get; set; } = false;
        public bool IsBlocked { get; set; } = false;
        public DateTime? BlockedAt { get; set; }
        public string? BlockedReason { get; set; }
        public List<Advert> Adverts { get; set; } = new();
    }
}