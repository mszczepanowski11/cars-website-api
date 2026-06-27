using cars_website_api.CarsWebsite.DTOs.Message;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("global")]
public class MessageController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessageController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartConversation([FromBody] StartConversationDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var conversationId = await _messageService.StartOrGetConversationAsync(
                userId, dto.AdvertId, dto.InitialMessage ?? string.Empty);
            return Ok(new { conversationId });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<MessageController>>();
            logger.LogError(ex, "[Message] StartConversation failed userId={UserId} advertId={AdvertId}", userId, dto.AdvertId);
            return StatusCode(500, new { message = "Wystąpił błąd serwera." });
        }
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        var conversations = await _messageService.GetUserConversationsAsync(userId);
        return Ok(conversations);
    }

    [HttpGet("conversation/{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(int conversationId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var messages = await _messageService.GetConversationMessagesAsync(conversationId, userId);
            return Ok(messages);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("conversation/{conversationId}")]
    public async Task<IActionResult> SendMessage(int conversationId, [FromBody] SendMessageDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var message = await _messageService.SendMessageAsync(conversationId, userId, dto.Content);
            return Ok(message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        var count = await _messageService.GetUnreadCountAsync(userId);
        return Ok(count);
    }

    [HttpPut("conversation/{conversationId}/pin")]
    public async Task<IActionResult> TogglePin(int conversationId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _messageService.PinConversationAsync(conversationId, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("conversation/{conversationId}/archive")]
    public async Task<IActionResult> ToggleArchive(int conversationId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _messageService.ArchiveConversationAsync(conversationId, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("conversation/{conversationId}/mark-unread")]
    public async Task<IActionResult> MarkUnread(int conversationId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            await _messageService.MarkConversationUnreadAsync(conversationId, userId);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
