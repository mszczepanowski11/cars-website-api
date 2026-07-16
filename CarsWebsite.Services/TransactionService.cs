using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Transaction;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notifications;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(AppDbContext context, INotificationService notifications, ILogger<TransactionService> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<TransactionResponseDto> CreateTransactionAsync(int buyerId, CreateTransactionDto dto)
    {
        var advert = await _context.CarAdverts
            .FirstOrDefaultAsync(a => a.Id == dto.AdvertId)
            ?? throw new KeyNotFoundException("Ogłoszenie nie istnieje.");

        if (!advert.IsActive || advert.IsHidden)
            throw new InvalidOperationException("To ogłoszenie nie jest już aktywne.");

        if (advert.UserId == buyerId)
            throw new InvalidOperationException("Nie możesz zarezerwować własnego ogłoszenia.");

        var transaction = new Transaction
        {
            Type = dto.Type,
            Status = TransactionStatus.Pending,
            AdvertId = advert.Id,
            BuyerId = buyerId,
            SellerId = advert.UserId,
            ScheduledAt = dto.ScheduledAt,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        var typeLabel = TypeLabel(dto.Type);
        _ = _notifications.NotifyAsync(advert.UserId, EmailNotificationType.PromotionActivated,
            $"Nowa prośba: {typeLabel}",
            $"Otrzymałeś nową prośbę ({typeLabel}) dotyczącą ogłoszenia \"{System.Net.WebUtility.HtmlEncode(advert.Title)}\". Sprawdź szczegóły i potwierdź lub odrzuć.",
            advertId: advert.Id);

        return await MapToDtoAsync(transaction);
    }

    public async Task<PagedResult<TransactionResponseDto>> GetMyTransactionsAsync(int userId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Transactions
            .Where(t => t.BuyerId == userId || t.SellerId == userId)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = new List<TransactionResponseDto>(items.Count);
        foreach (var t in items) dtos.Add(await MapToDtoAsync(t));

        return new PagedResult<TransactionResponseDto> { Items = dtos, TotalCount = totalCount };
    }

    public async Task<TransactionResponseDto> GetTransactionAsync(int id, int userId, bool isAdmin)
    {
        var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Transakcja nie istnieje.");

        if (!isAdmin && transaction.BuyerId != userId && transaction.SellerId != userId)
            throw new UnauthorizedAccessException("Nie masz dostępu do tej transakcji.");

        return await MapToDtoAsync(transaction);
    }

    public async Task<TransactionResponseDto> UpdateStatusAsync(int id, int userId, bool isAdmin, UpdateTransactionStatusDto dto)
    {
        var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Transakcja nie istnieje.");

        if (!isAdmin && transaction.BuyerId != userId && transaction.SellerId != userId)
            throw new UnauthorizedAccessException("Nie masz dostępu do tej transakcji.");

        if (transaction.Status is TransactionStatus.Cancelled or TransactionStatus.Completed)
            throw new InvalidOperationException("Ta transakcja jest już zakończona i nie można jej zmienić.");

        // Only the seller (or an admin) can confirm/complete a request - the buyer's only lever
        // over a transaction they don't own the vehicle for is cancelling it (see CancelTransactionAsync).
        if (!isAdmin && transaction.SellerId != userId)
            throw new UnauthorizedAccessException("Tylko sprzedawca może zmienić status tej transakcji.");

        if (dto.Status == TransactionStatus.Completed && transaction.Status != TransactionStatus.Confirmed)
            throw new InvalidOperationException("Transakcja musi być najpierw potwierdzona.");

        transaction.Status = dto.Status;
        if (dto.Status == TransactionStatus.Completed) transaction.CompletedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Note))
            transaction.Notes = string.IsNullOrWhiteSpace(transaction.Notes) ? dto.Note : $"{transaction.Notes}\n{dto.Note}";

        await _context.SaveChangesAsync();

        var typeLabel = TypeLabel(transaction.Type);
        if (dto.Status == TransactionStatus.Confirmed)
        {
            _ = _notifications.NotifyAsync(transaction.BuyerId, EmailNotificationType.PromotionActivated,
                $"Potwierdzono: {typeLabel}",
                $"Sprzedawca potwierdził Twoją prośbę ({typeLabel}).", advertId: transaction.AdvertId);
        }
        else if (dto.Status == TransactionStatus.Completed)
        {
            _ = _notifications.NotifyAsync(transaction.BuyerId, EmailNotificationType.PromotionActivated,
                $"Zakończono: {typeLabel}",
                $"Transakcja ({typeLabel}) została oznaczona jako zakończona.", advertId: transaction.AdvertId);
        }

        return await MapToDtoAsync(transaction);
    }

    public async Task CancelTransactionAsync(int id, int userId, bool isAdmin)
    {
        var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Transakcja nie istnieje.");

        if (!isAdmin && transaction.BuyerId != userId && transaction.SellerId != userId)
            throw new UnauthorizedAccessException("Nie masz dostępu do tej transakcji.");

        if (transaction.Status is TransactionStatus.Cancelled or TransactionStatus.Completed)
            throw new InvalidOperationException("Ta transakcja jest już zakończona.");

        transaction.Status = TransactionStatus.Cancelled;
        await _context.SaveChangesAsync();

        var typeLabel = TypeLabel(transaction.Type);
        var notifyUserId = userId == transaction.BuyerId ? transaction.SellerId : transaction.BuyerId;
        _ = _notifications.NotifyAsync(notifyUserId, EmailNotificationType.PromotionExpired,
            $"Anulowano: {typeLabel}",
            $"Druga strona anulowała transakcję ({typeLabel}).", advertId: transaction.AdvertId);
    }

    private async Task<TransactionResponseDto> MapToDtoAsync(Transaction t)
    {
        var advert = await _context.CarAdverts.AsNoTracking()
            .Where(a => a.Id == t.AdvertId)
            .Select(a => new { a.Title, a.Price })
            .FirstOrDefaultAsync();

        var buyer = await _context.Users.AsNoTracking()
            .Where(u => u.Id == t.BuyerId)
            .Select(u => new { u.Name, u.Surname })
            .FirstOrDefaultAsync();

        var seller = await _context.Users.AsNoTracking()
            .Where(u => u.Id == t.SellerId)
            .Select(u => new { u.Name, u.Surname, u.PhoneNumber })
            .FirstOrDefaultAsync();

        return new TransactionResponseDto
        {
            Id = t.Id,
            Type = t.Type,
            Status = t.Status,
            AdvertId = t.AdvertId,
            AdvertTitle = advert?.Title ?? string.Empty,
            AdvertPrice = advert?.Price ?? 0,
            BuyerId = t.BuyerId,
            BuyerName = buyer != null ? $"{buyer.Name} {buyer.Surname}".Trim() : string.Empty,
            SellerId = t.SellerId,
            SellerName = seller != null ? $"{seller.Name} {seller.Surname}".Trim() : string.Empty,
            CreatedAt = t.CreatedAt,
            ScheduledAt = t.ScheduledAt,
            CompletedAt = t.CompletedAt,
            Notes = t.Notes,
            SellerPhone = t.Status == TransactionStatus.Confirmed || t.Status == TransactionStatus.Completed ? seller?.PhoneNumber : null,
        };
    }

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Reservation => "Rezerwacja",
        TransactionType.Viewing => "Oględziny",
        TransactionType.Purchase => "Zakup",
        _ => type.ToString(),
    };
}
