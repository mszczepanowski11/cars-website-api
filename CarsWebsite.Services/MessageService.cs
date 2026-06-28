using cars_website_api.CarsWebsite.DTOs.Message;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class MessageService : IMessageService
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<MessageService> _logger;

    public MessageService(AppDbContext context, INotificationService notifications, ILogger<MessageService> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> StartOrGetConversationAsync(int buyerId, int advertId, string initialMessage)
    {
        var advert = await _context.Adverts.FindAsync(advertId)
            ?? throw new KeyNotFoundException("Advert not found");

        int sellerId = advert.UserId;
        if (buyerId == sellerId)
            throw new InvalidOperationException("Cannot start a conversation with yourself");

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
            .AsNoTracking()
            .Include(c => c.Buyer)
            .Include(c => c.Seller)
            .Include(c => c.Advert)
            .Where(c => c.BuyerId == userId || c.SellerId == userId)
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.LastMessageAt)
            .ToListAsync();

        var convIds = convs.Select(c => c.Id).ToList();
        var advertIds = convs.Select(c => c.AdvertId).Distinct().ToList();

        // Sequential queries — EF Core DbContext does not support concurrent operations.
        var lastMessageIds = await _context.Messages
            .AsNoTracking()
            .Where(m => convIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync();

        var lastMessages = await _context.Messages
            .AsNoTracking()
            .Where(m => lastMessageIds.Contains(m.Id))
            .ToListAsync();

        var unreadCounts = await _context.Messages
            .AsNoTracking()
            .Where(m => convIds.Contains(m.ConversationId) && m.SenderId != userId && !m.IsRead)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConvId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConvId, x => x.Count);

        // Fetch all images for these adverts, then pick the main (or first) per advert client-side.
        var allImages = await _context.AdvertImages
            .AsNoTracking()
            .Where(img => advertIds.Contains(img.AdvertId))
            .ToListAsync();

        var thumbnails = allImages
            .GroupBy(img => img.AdvertId)
            .ToDictionary(
                g => g.Key,
                g => (g.FirstOrDefault(img => img.IsMain) ?? g.First()).Url);

        return convs.Select(c =>
        {
            bool isBuyer = c.BuyerId == userId;
            var other = isBuyer ? c.Seller : c.Buyer;
            var last = lastMessages.FirstOrDefault(m => m.ConversationId == c.Id);

            return new ConversationDto
            {
                Id = c.Id,
                BuyerId = c.BuyerId,
                BuyerName = c.Buyer != null ? $"{c.Buyer.Name} {c.Buyer.Surname}" : "(Użytkownik usunięty)",
                SellerId = c.SellerId,
                SellerName = c.Seller != null ? $"{c.Seller.Name} {c.Seller.Surname}" : "(Użytkownik usunięty)",
                AdvertId = c.AdvertId,
                AdvertTitle = c.Advert?.Title ?? "(Ogłoszenie usunięte)",
                AdvertThumbnail = thumbnails.GetValueOrDefault(c.AdvertId),
                LastMessageAt = c.LastMessageAt,
                LastMessageContent = last?.Content,
                LastMessageIsMine = last?.SenderId == userId,
                UnreadCount = unreadCounts.GetValueOrDefault(c.Id, 0),
                OtherUserId = other?.Id ?? 0,
                OtherUserName = other != null ? $"{other.Name} {other.Surname}" : "(Użytkownik usunięty)",
                OtherUserAvatar = other?.AvatarUrl,
                IsPinned = c.IsPinned,
                IsArchived = c.IsArchived
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
            .ToListAsync(); // tracked so we can mark IsRead below

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
            .AsNoTracking()
            .Where(m =>
                m.SenderId != userId &&
                !m.IsRead &&
                (m.Conversation.BuyerId == userId || m.Conversation.SellerId == userId))
            .CountAsync();

    public async Task<ConversationDto> PinConversationAsync(int conversationId, int userId)
    {
        var conv = await _context.Conversations
            .Include(c => c.Buyer).Include(c => c.Seller).Include(c => c.Advert)
            .FirstOrDefaultAsync(c => c.Id == conversationId &&
                (c.BuyerId == userId || c.SellerId == userId))
            ?? throw new UnauthorizedAccessException();

        conv.IsPinned = !conv.IsPinned;
        await _context.SaveChangesAsync();

        return await BuildConversationDtoAsync(conv, userId);
    }

    public async Task<ConversationDto> ArchiveConversationAsync(int conversationId, int userId)
    {
        var conv = await _context.Conversations
            .Include(c => c.Buyer).Include(c => c.Seller).Include(c => c.Advert)
            .FirstOrDefaultAsync(c => c.Id == conversationId &&
                (c.BuyerId == userId || c.SellerId == userId))
            ?? throw new UnauthorizedAccessException();

        conv.IsArchived = !conv.IsArchived;
        if (conv.IsArchived) conv.IsPinned = false;
        await _context.SaveChangesAsync();

        return await BuildConversationDtoAsync(conv, userId);
    }

    public async Task MarkConversationUnreadAsync(int conversationId, int userId)
    {
        var conv = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId &&
                (c.BuyerId == userId || c.SellerId == userId))
            ?? throw new UnauthorizedAccessException();

        var lastMsg = await _context.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId)
            .OrderByDescending(m => m.SentAt)
            .FirstOrDefaultAsync();

        if (lastMsg != null)
        {
            lastMsg.IsRead = false;
            await _context.SaveChangesAsync();
        }
    }

    private async Task<ConversationDto> BuildConversationDtoAsync(Conversation conv, int userId)
    {
        bool isBuyer = conv.BuyerId == userId;
        var other = isBuyer ? conv.Seller : conv.Buyer;

        var last = await _context.Messages
            .Where(m => m.ConversationId == conv.Id)
            .OrderByDescending(m => m.SentAt)
            .FirstOrDefaultAsync();

        var unreadCount = await _context.Messages
            .CountAsync(m => m.ConversationId == conv.Id && m.SenderId != userId && !m.IsRead);

        var thumb = await _context.AdvertImages
            .Where(img => img.AdvertId == conv.AdvertId)
            .OrderByDescending(img => img.IsMain)
            .Select(img => img.Url)
            .FirstOrDefaultAsync();

        return new ConversationDto
        {
            Id = conv.Id,
            BuyerId = conv.BuyerId,
            BuyerName = $"{conv.Buyer.Name} {conv.Buyer.Surname}",
            SellerId = conv.SellerId,
            SellerName = $"{conv.Seller.Name} {conv.Seller.Surname}",
            AdvertId = conv.AdvertId,
            AdvertTitle = conv.Advert?.Title ?? "(Ogłoszenie usunięte)",
            AdvertThumbnail = thumb,
            LastMessageAt = conv.LastMessageAt,
            LastMessageContent = last?.Content,
            LastMessageIsMine = last?.SenderId == userId,
            UnreadCount = unreadCount,
            OtherUserId = other.Id,
            OtherUserName = $"{other.Name} {other.Surname}",
            OtherUserAvatar = other.AvatarUrl,
            IsPinned = conv.IsPinned,
            IsArchived = conv.IsArchived
        };
    }
}
