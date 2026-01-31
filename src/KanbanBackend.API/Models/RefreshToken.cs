using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KanbanBackend.API.Models;

public class RefreshToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)] 
    // We store the hash of the token, not the plaintext token
    public string TokenHash { get; set; } = string.Empty;

    public string? TokenSalt { get; set; } // Optional: specific salt if not using global pepper

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Expires { get; set; }
    
    public string? CreatedByIp { get; set; }
    
    public DateTimeOffset? Revoked { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReasonRevoked { get; set; }

    // Session Tracking
    public string? SessionId { get; set; }

    // Replaced by new token (Rotation chain)
    public int? ReplacedByTokenId { get; set; }
    
    [ForeignKey(nameof(ReplacedByTokenId))]
    public RefreshToken? ReplacedByToken { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= Expires;
    public bool IsRevoked => Revoked != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Foreign Key to User
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }
}
