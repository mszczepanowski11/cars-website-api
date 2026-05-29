using cars_website_api.CarsWebsite.DTOs.Message;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IMessageService
{
    Task<int> StartOrGetConversationAsync(int buyerId, int advertId, string initialMessage);
    Task<List<ConversationDto>> GetUserConversationsAsync(int userId);
    Task<List<MessageDto>> GetConversationMessagesAsync(int conversationId, int userId);
    Task<MessageDto> SendMessageAsync(int conversationId, int senderId, string content);
    Task<int> GetUnreadCountAsync(int userId);
}