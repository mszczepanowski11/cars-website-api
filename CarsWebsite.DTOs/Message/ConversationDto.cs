namespace cars_website_api.CarsWebsite.DTOs.Message;

public class ConversationDto
{
    public int Id { get; set; }
    public int BuyerId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public int SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public int AdvertId { get; set; }
    public string AdvertTitle { get; set; } = string.Empty;
    public string? AdvertThumbnail { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? LastMessageContent { get; set; }
    public bool LastMessageIsMine { get; set; }
    public int UnreadCount { get; set; }
    public int OtherUserId { get; set; }
    public string OtherUserName { get; set; } = string.Empty;
    public string? OtherUserAvatar { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
}
