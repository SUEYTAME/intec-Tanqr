using System.Text;
using Combustible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace Combustible.Api.Security;

public sealed class PassphraseValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        var length = password?.EnumerateRunes().Count() ?? 0;
        var normalized = password?.Normalize(NormalizationForm.FormKC).ToUpperInvariant() ?? string.Empty;
        var common = new[] { "PASSWORD", "CONTRASEÑA", "123456", "QWERTY", "COMBUSTIBLE", "ADMINISTRADOR" };
        var invalid = length is < 15 or > 128 || string.IsNullOrWhiteSpace(password)
            || normalized.Distinct().Count() < 5
            || common.Any(normalized.Contains)
            || (user.Email is { Length: > 0 } && normalized.Contains(user.Email.ToUpperInvariant(), StringComparison.Ordinal));
        return Task.FromResult(invalid
            ? IdentityResult.Failed(new IdentityError { Code = "Passphrase", Description = "Usa una frase de 15 a 128 caracteres que no sea común ni incluya tu correo." })
            : IdentityResult.Success);
    }
}
