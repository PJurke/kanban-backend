using KanbanBackend.API.Models;
using Microsoft.AspNetCore.Identity;

namespace KanbanBackend.API.Services;

public interface IAuthService
{
    Task<IdentityResult> RegisterAsync(string email, string password);
    Task<AuthService.AuthResult?> LoginAsync(string email, string password);
    Task<AuthService.AuthResult?> RefreshAsync(string refreshToken);
    Task RevokeAsync(string refreshToken, string? ipAddress = null);
    Task<IdentityResult> DeleteAccountAsync(string userId, string password);
}
