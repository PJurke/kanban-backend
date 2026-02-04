using FluentAssertions;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class AuthServiceTests
{
    private readonly Mock<UserManager<AppUser>> _userManagerMock;
    private readonly Mock<SignInManager<AppUser>> _signInManagerMock;
    private readonly AppDbContext _context;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        // 1. Setup DbContext (InMemory)
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        // 2. Setup UserManager Mock
        var store = new Mock<IUserStore<AppUser>>();
        _userManagerMock = new Mock<UserManager<AppUser>>(store.Object, null, null, null, null, null, null, null, null);

        // 3. Setup SignInManager Mock
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var userPrincipalFactory = new Mock<IUserClaimsPrincipalFactory<AppUser>>();
        _signInManagerMock = new Mock<SignInManager<AppUser>>(_userManagerMock.Object, contextAccessor.Object, userPrincipalFactory.Object, null, null, null, null);

        // 4. Setup Config & Logger
        _configMock = new Mock<IConfiguration>();
        _configMock.Setup(c => c["Auth:JwtSecret"]).Returns("super_secret_key_which_is_long_enough_for_hmacsha256");
        _configMock.Setup(c => c["Auth:Pepper"]).Returns("pepper");

        _loggerMock = new Mock<ILogger<AuthService>>();

        _authService = new AuthService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _context,
            _configMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task RegisterAsync_Should_CallCreateAsync()
    {
        // Arrange
        var email = "test@example.com";
        var password = TestConstants.DefaultPassword;
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), password))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authService.RegisterAsync(email, password);

        // Assert
        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue();
        _userManagerMock.Verify(u => u.CreateAsync(It.Is<AppUser>(u => u.Email == email), password), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnNull_When_UserNotFound()
    {
        // Arrange
        _userManagerMock.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);

        // Act
        var result = await _authService.LoginAsync("unknown@example.com", "pass");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnNull_When_PasswordInvalid()
    {
        // Arrange
        var user = new AppUser { Email = "test@example.com", UserName = "test@example.com" };
        _userManagerMock.Setup(u => u.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(s => s.CheckPasswordSignInAsync(user, "wrong", true))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await _authService.LoginAsync(user.Email, "wrong");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnAuthResult_When_Success()
    {
        // Arrange
        var user = new AppUser { Id = "u1", Email = "test@example.com", UserName = "test@example.com" };
        _userManagerMock.Setup(u => u.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(s => s.CheckPasswordSignInAsync(user, "pass", true))
            .ReturnsAsync(SignInResult.Success);

        // Act
        var result = await _authService.LoginAsync(user.Email, "pass");

        // Assert
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        
        // Verify Refresh Token saved to DB
        var tokenInDb = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
        tokenInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_Should_Succeed_And_RotateToken()
    {
        // Arrange
        var user = new AppUser { Id = "u1", Email = "test@example.com" };
        var oldTokenString = "old_token";
        
        // We need to match hash logic of AuthService (using SHA256 + pepper)
        // Since HashToken is private, we can simulate by inserting a known token hash directly into DB
        // Or we use reflection / InternalsVisibleTo.
        // EASIER: Just use the service to generate a token first (helper method or integration style).
        // OR: Reproduce hash logic here if simple. (It is simple SHA256).
        
        string Hash(string t) 
        {
            var pepper = "pepper";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(t + pepper);
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }

        var oldTokenHash = Hash(oldTokenString);
        var oldTokenEntity = new RefreshToken
        {
            Id = 1,
            TokenHash = oldTokenHash,
            UserId = user.Id,
            Expires = DateTimeOffset.UtcNow.AddDays(1),
            User = user // Navigation prop crucial for rotation
        };
        _context.RefreshTokens.Add(oldTokenEntity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.RefreshAsync(oldTokenString);

        // Assert
        result.Should().NotBeNull();
        result!.RefreshToken.Should().NotBe(oldTokenString); // Rotated

        // DB State Confirmation
        var oldTokenInDb = await _context.RefreshTokens.FindAsync(oldTokenEntity.Id);
        oldTokenInDb!.Revoked.Should().NotBeNull();
        oldTokenInDb.ReplacedByTokenId.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_Should_DetectReuse_And_RevokeAll()
    {
        // Arrange
        var user = new AppUser { Id = "u1" };
        var stolenTokenString = "stolen";
        
        string Hash(string t) 
        {
            var pepper = "pepper";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(t + pepper);
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }

        // Token that was already revoked
        var stolenTokenEntity = new RefreshToken
        {
            Id = 2,
            TokenHash = Hash(stolenTokenString),
            UserId = user.Id,
            Expires = DateTimeOffset.UtcNow.AddDays(1),
            Revoked = DateTimeOffset.UtcNow.AddMinutes(-10), // ALREADY REVOKED
            User = user
        };

        // Another valid token for same user
        var legitimateAndActiveToken = new RefreshToken
        {
            Id = 3,
            TokenHash = Hash("valid"),
            UserId = user.Id,
            Expires = DateTimeOffset.UtcNow.AddDays(1),
            User = user
        };

        _context.RefreshTokens.AddRange(stolenTokenEntity, legitimateAndActiveToken);
        await _context.SaveChangesAsync();

        // Act - Attacker tries to use revoked token
        var result = await _authService.RefreshAsync(stolenTokenString);

        // Assert
        result.Should().BeNull();

        // Verify Reuse Logic: ALL tokens for user should be revoked
        var validTokenInDb = await _context.RefreshTokens.FindAsync(legitimateAndActiveToken.Id);
        validTokenInDb!.Revoked.Should().NotBeNull();
        validTokenInDb.ReasonRevoked.Should().Contain("Reuse detection");
    }

    [Fact]
    public async Task DeleteAccountAsync_Should_ValidatePassword_And_DeleteBoardsAndUser()
    {
        // Arrange
        var userId = "user-to-delete";
        var user = new AppUser { Id = userId, Email = "del@test.com" };
        var password = "password";

        // Setup User
        _userManagerMock.Setup(u => u.FindByIdAsync(userId)).ReturnsAsync(user);
        _userManagerMock.Setup(u => u.CheckPasswordAsync(user, password)).ReturnsAsync(true);
        _userManagerMock.Setup(u => u.DeleteAsync(user)).ReturnsAsync(IdentityResult.Success);

        // Setup Boards
        _context.Boards.Add(new Board { Id = Guid.NewGuid(), OwnerId = userId, Name = "Board 1" });
        _context.Boards.Add(new Board { Id = Guid.NewGuid(), OwnerId = "other", Name = "Board 2" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _authService.DeleteAccountAsync(userId, password);

        // Assert
        result.Succeeded.Should().BeTrue();
        
        // Verify User Deletion called
        _userManagerMock.Verify(u => u.DeleteAsync(user), Times.Once);

        // Verify Boards Deleted
        var remainingBoards = await _context.Boards.Where(b => b.OwnerId == userId).ToListAsync();
        remainingBoards.Should().BeEmpty();

        var otherBoards = await _context.Boards.Where(b => b.OwnerId == "other").ToListAsync();
        otherBoards.Should().HaveCount(1);
    }
}
