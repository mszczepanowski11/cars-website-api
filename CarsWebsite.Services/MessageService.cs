using cars_website_api.CarsWebsite.DTOs.Message;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class MessageService : IMessageService
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notifications;

    public MessageService(AppDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<int> StartOrGetConversationAsync(int buyerId, int advertId, string initialMessage)
    {
        var advert = await _context.Adverts.FindAsync(advertId)
            ?? throw new Exception("Advert not found");

        int sellerId = advert.UserId;
        if (buyerId == sellerId)
            throw new Exception("Cannot start a conversation with yourself");

        var existing = await _context.Conversations
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.AdvertId == advertId);

        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(initialMessage))
                await AppendMessageAsync(existing, buyerId, initialMessage);
            return existing.Id;
        }

        var conversation = new Conversation
        {
            BuyerId = buyerId,
            SellerId = sellerId,
            AdvertId = advertId,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(initialMessage))
            await AppendMessageAsync(conversation, buyerId, initialMessage);

        return conversation.Id;
    }

    private async Task AppendMessageAsync(Conversation conversation, int senderId, string content)
    {
        var msg = new Message
        {
            ConversationId = conversation.Id,
            SenderId = senderId,
            Content = content,
            SentAt = DateTime.UtcNow,
            IsRead = false
        };
        _context.Messages.Add(msg);
        conversation.LastMessageAt = msg.SentAt;
        await _context.SaveChangesAsync();
    }

    public async Task<List<ConversationDto>> GetUserConversationsAsync(int userId)
    {
        var convs = await _context.Conversations
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Advert)
            .Where(c => c.BuyerId == userId || c.SellerId == userId)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync();

        var convIds = convs.Select(c => c.Id).ToList();

        var lastMessages = await _context.Messages
            .Where(m => convIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.SentAt).First())
            .ToListAsync();

        var unreadCounts = await _context.Messages
            .Where(m => convIds.Contains(m.ConversationId) && m.SenderId != userId && !m.IsRead)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConvId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConvId, x => x.Count);

        return convs.Select(c =>
        {
            bool isBuyer = c.BuyerId == userId;
            var other = isBuyer ? c.Seller : c.Buyer;
            var last = lastMessages.FirstOrDefault(m => m.ConversationId == c.Id);

            return new ConversationDto
            {
                Id = c.Id,
                BuyerId = c.BuyerId,
                BuyerName = $"{c.Buyer.Name} {c.Buyer.Surname}",
                SellerId = c.SellerId,
                SellerName = $"{c.Seller.Name} {c.Seller.Surname}",
                AdvertId = c.AdvertId,
                AdvertTitle = c.Advert?.Title ?? "(Ogłoszenie usunięte)",
                LastMessageAt = c.LastMessageAt,
                LastMessageContent = last?.Content,
                UnreadCount = unreadCounts.GetValueOrDefault(c.Id, 0),
                OtherUserId = other.Id,
                OtherUserName = $"{other.Name} {other.Surname}"
            };
        }).ToList();
    }

    public async Task<List<MessageDto>> GetConversationMessagesAsync(int conversationId, int userId)
    {
        _ = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId &&
                (c.BuyerId == userId || c.SellerId == userId))
            ?? throw new UnauthorizedAccessException();

        var messages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        var unread = messages.Where(m => m.SenderId != userId && !m.IsRead).ToList();
        foreach (var m in unread) m.IsRead = true;
        if (unread.Count > 0) await _context.SaveChangesAsync();

        return messages.Select(m => new MessageDto
        {
            Id = m.Id,
            SenderId = m.SenderId,
            SenderName = $"{m.Sender.Name} {m.Sender.Surname}",
            Content = m.Content,
            SentAt = m.SentAt,
            IsRead = m.IsRead,
            IsMine = m.SenderId == userId
        }).ToList();
    }

    public async Task<MessageDto> SendMessageAsync(int conversationId, int senderId, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 5000)
            throw new ArgumentException("Treść wiadomości jest nieprawidłowa (max 5000 znaków).");

        var conv = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId &&
                (c.BuyerId == senderId || c.SellerId == senderId))
            ?? throw new UnauthorizedAccessException();

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = senderId,
            Content = content,
            SentAt = DateTime.UtcNow,
            IsRead = false
        };
        _context.Messages.Add(msg);
        conv.LastMessageAt = msg.SentAt;
        await _context.SaveChangesAsync();

        var sender = await _context.Users.FindAsync(senderId);

        // Notify the recipient
        var recipientId = conv.BuyerId == senderId ? conv.SellerId : conv.BuyerId;
        _ = _notifications.NotifyAsync(recipientId, EmailNotificationType.NewMessage,
            "Nowa wiadomość",
            $"{sender!.Name} {sender.Surname} wysłał(a) Ci wiadomość: \"{(content.Length > 100 ? content[..100] + "..." : content)}\"",
            advertId: conv.AdvertId);

        return new MessageDto
        {
            Id = msg.Id,
            SenderId = msg.SenderId,
            SenderName = $"{sender!.Name} {sender.Surname}",
            Content = msg.Content,
            SentAt = msg.SentAt,
            IsRead = false,
            IsMine = true
        };
    }

    public async Task<int> GetUnreadCountAsync(int userId) =>
        await _context.Messages
            .Where(m =>
                m.SenderId != userId &&
                !m.IsRead &&
                (m.Conversation.BuyerId == userId || m.Conversation.SellerId == userId))
            .CountAsync();
}