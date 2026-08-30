using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Combustible.Api.Security;

public sealed record JwtSettings(string Issuer, string Audience, byte[] Key);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    Guid UserId, string DisplayName, IList<string> Roles);

public sealed class SessionService(AppDbContext db, UserManager<AppUser> users, JwtSettings settings)
{
    public async Task<SessionResponse> IssueAsync(AppUser user, LoginSession? session = null)
    {
        var now = DateTimeOffset.UtcNow;
        session ??= new LoginSession
        {
            UserId = user.Id, ExpiresAt = now.AddHours(8), SecurityStamp = user.SecurityStamp!
        };
        if (db.Entry(session).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            db.Sessions.Add(session);
        var refresh = $"{session.Id:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        session.RefreshHash = Hash(refresh);
        session.Version = Guid.NewGuid();
        var roles = await users.GetRolesAsync(user);
        var expires = now.AddMinutes(15);
        if (expires > session.ExpiresAt) expires = session.ExpiresAt;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("sid", session.Id.ToString()),
            new("stamp", user.SecurityStamp!),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(settings.Key), SecurityAlgorithms.HmacSha256));
        await db.SaveChangesAsync();
        return new SessionResponse(new JwtSecurityTokenHandler().WriteToken(jwt), refresh, expires,
            user.Id, user.DisplayName, roles);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
