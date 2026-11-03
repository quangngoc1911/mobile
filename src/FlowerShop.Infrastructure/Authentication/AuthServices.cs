using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlowerShop.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "FlowerShop.Api";
    public string Audience { get; set; } = "FlowerShop.Mobile";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, DateTimeOffset RefreshExpiresAt);

public interface ITokenService
{
    TokenPair Create(User user, Guid sessionId, string activeRole, string refreshToken);
    string HashRefreshToken(string token);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenPair Create(User user, Guid sessionId, string activeRole, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey phải có ít nhất 32 byte.");
        var now = DateTimeOffset.UtcNow;
        var accessExpiry = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new("full_name", user.FullName),
            new("active_role", activeRole),
            new("sid", sessionId.ToString()),
        };
        // Token chỉ mang quyền đang sử dụng; server vẫn đối chiếu lại quyền và phiên.
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, now.UtcDateTime, accessExpiry.UtcDateTime, credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), refreshToken, accessExpiry, refreshExpiry);
    }

    public string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string hash, string password);
}

public sealed class PasswordService(IPasswordHasher<User> hasher) : IPasswordService
{
    public string Hash(User user, string password) => hasher.HashPassword(user, password);
    public bool Verify(User user, string hash, string password) => hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
}

public static class TokenRandomizer
{
    public static string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}

public sealed class AccountStore(FlowerShopDbContext db, IPasswordService passwords)
{
    public async Task<User?> FindAsync(string username, CancellationToken ct) => await db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.NormalizedUsername == Normalize(username), ct);
    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => await db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == id, ct);
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
    public string HashPassword(User user, string password) => passwords.Hash(user, password);
}
