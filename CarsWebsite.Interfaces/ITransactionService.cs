using cars_website_api.CarsWebsite.DTOs.Transaction;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ITransactionService
{
    Task<TransactionResponseDto> CreateTransactionAsync(int buyerId, CreateTransactionDto dto);
    Task<PagedResult<TransactionResponseDto>> GetMyTransactionsAsync(int userId, int page, int pageSize);
    Task<TransactionResponseDto> GetTransactionAsync(int id, int userId, bool isAdmin);
    Task<TransactionResponseDto> UpdateStatusAsync(int id, int userId, bool isAdmin, UpdateTransactionStatusDto dto);
    Task CancelTransactionAsync(int id, int userId, bool isAdmin);
}
