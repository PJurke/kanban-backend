using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
// Add other usings as needed

namespace KanbanBackend.API.Services;

public class AuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthService(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        AppDbContext context,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _configuration = configuration;
    }

    public async Task<IdentityResult> RegisterAsync(string email, string password)
    {
        var user = new AppUser { UserName = email, Email = email };
        return await _userManager.CreateAsync(user, password);
    }

    public async Task<AuthResult?> LoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return null;

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, true); // lockoutOnFailure: true
        if (!result.Succeeded) return null;

        return await GenerateAuthResultAsync(user);
    }

    public async Task<AuthResult?> RefreshAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);
        
        var existingToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .Include(rt => rt.ReplacedByToken)
            .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existingToken == null)
        {
            // Token likely forged or already cleaned up
            return null; 
        }

        // Reuse Detection: If token is revoked but used again -> Security Alert!
        if (existingToken.IsRevoked)
        {
            // REVOKE ALL TOKENS FOR THIS USER (Family revoke)
            await RevokeAllUserTokensAsync(existingToken.UserId);
            return null; // or throw SecurityException
        }

        if (existingToken.IsExpired)
        {
            return null; // Just expired, clean login needed
        }

        // Rotation: Revoke used token, create new one
        return await RotateRefreshTokenAsync(existingToken);
    }

    public async Task RevokeAsync(string refreshToken, string? ipAddress = null)
    {
        var tokenHash = HashToken(refreshToken);
        var existingToken = await _context.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (existingToken != null && existingToken.IsActive)
        {
            existingToken.Revoked = DateTimeOffset.UtcNow;
            existingToken.RevokedByIp = ipAddress;
            existingToken.ReasonRevoked = "Logout";
            await _context.SaveChangesAsync();
        }
    }

    private async Task<AuthResult> GenerateAuthResultAsync(AppUser user)
    {
        var accessToken = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            TokenHash = HashToken(refreshToken),
            UserId = user.Id,
            Created = DateTimeOffset.UtcNow,
            Expires = DateTimeOffset.UtcNow.AddDays(7) // 7 days refresh
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        return new AuthResult(accessToken, refreshToken, refreshTokenEntity.Expires);
    }

    private async Task<AuthResult> RotateRefreshTokenAsync(RefreshToken oldToken)
    {
        var newRefreshToken = GenerateRefreshToken();
        var newRefreshTokenEntity = new RefreshToken
        {
            TokenHash = HashToken(newRefreshToken),
            UserId = oldToken.UserId,
            Created = DateTimeOffset.UtcNow,
            Expires = DateTimeOffset.UtcNow.AddDays(7) 
        };

        // Link old token to new one
        oldToken.Revoked = DateTimeOffset.UtcNow;
        oldToken.ReasonRevoked = "Replaced by new token";
        oldToken.ReplacedByToken = newRefreshTokenEntity;

        _context.RefreshTokens.Add(newRefreshTokenEntity);
        _context.RefreshTokens.Update(oldToken);
        await _context.SaveChangesAsync();

        var accessToken = GenerateJwtToken(oldToken.User!);
        return new AuthResult(accessToken, newRefreshToken, newRefreshTokenEntity.Expires);
    }

    private async Task RevokeAllUserTokensAsync(string userId)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.Revoked == null)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoked = DateTimeOffset.UtcNow;
            token.ReasonRevoked = "Security: Reuse detection triggered";
        }
        await _context.SaveChangesAsync();
    }

    private string GenerateJwtToken(AppUser user)
    {
        var secret = _configuration["Auth:JwtSecret"];
        if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("JWT Secret not configured");

        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "kanban-backend", 
            audience: "kanban-client",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private string HashToken(string token)
    {
        var pepper = _configuration["Auth:Pepper"] ?? "";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token + pepper);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public record AuthResult(string AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpires);
}
