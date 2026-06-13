using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.DTOs.User;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IUserService
{
    Task<User?> GetById(int id);
    Task<UserStatsDto> GetUserStatsAsync(int userId);
    Task<UserStatsDto> GetPublicStatsAsync(int userId);
    Task<User> UpdateProfileAsync(int userId, UpdateProfileDto dto);
    Task UpdatePasswordAsync(int userId, string currentPassword, string newPassword);
    Task<UserSettingsDto> GetSettingsAsync(int userId);
    Task<UserSettingsDto> UpdateSettingsAsync(int userId, UserSettingsDto dto);
    Task DeleteAccountAsync(int userId);
    Task<PublicUserProfileDto?> GetPublicProfileAsync(int userId);
}
