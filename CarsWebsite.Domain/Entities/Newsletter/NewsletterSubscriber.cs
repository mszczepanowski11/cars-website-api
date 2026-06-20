namespace CarsWebsite
{
    public class NewsletterSubscriber
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UnsubscribedAt { get; set; }
    }
}
