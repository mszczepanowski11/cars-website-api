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
        public string? AvatarUrl { get; set; }
        public bool EmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationTokenExpires { get; set; }
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpires { get; set; }
        public string? GoogleId { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Profile
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Street { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? About { get; set; }

        // Notification settings
        public bool EmailNotifications { get; set; } = true;
        public bool PriceChangeAlerts { get; set; } = true;
        public bool NewMessageAlerts { get; set; } = true;
        public bool NewsletterSubscribed { get; set; } = false;

        public List<Advert> Adverts { get; set; } = new();
    }
}
