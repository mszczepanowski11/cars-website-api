namespace CarsWebsite
{
    public class NewsletterSubscriber
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; } = false;
        public bool IsConfirmed { get; set; } = false;
        public string? ConfirmationToken { get; set; }
        public DateTime? ConfirmationTokenExpires { get; set; }
        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? UnsubscribedAt { get; set; }
    }
}
