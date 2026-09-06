using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Combustible.Domain;

namespace Combustible.Infrastructure.Security;

// RS-03 (reposo): AES-256-GCM por campo. La clave maestra se deriva en dos subclaves con HKDF
// para que el cifrado y el índice ciego nunca compartan clave.
public sealed class FieldProtector
{
    private const string Prefix = "v1:";
    private readonly byte[] _encryptionKey;
    private readonly byte[] _indexKey;

    public FieldProtector(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        if (masterKey.Length != 32) throw new InvalidOperationException("DATA_ENCRYPTION_KEY debe tener exactamente 32 bytes (AES-256).");
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "combustible-aes-gcm-v1"u8.ToArray());
        _indexKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "combustible-blind-index-v1"u8.ToArray());
        KeyId = Convert.ToHexString(SHA256.HashData(_indexKey))[..16];
    }

    // Huella de la clave (no la revela): separa modelos de EF construidos con claves distintas.
    public string KeyId { get; }

    public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[12 + data.Length + 16];
        var nonce = output.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, data, output.AsSpan(12, data.Length), output.AsSpan(12 + data.Length), Encoding.UTF8.GetBytes(purpose));
        return Prefix + Convert.ToBase64String(output);
    }

    public string Unprotect(string value, string purpose)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsProtected(value)) throw new CryptographicException($"El valor de {purpose} no está cifrado.");
        var input = Convert.FromBase64String(value[Prefix.Length..]);
        if (input.Length < 28) throw new CryptographicException($"El valor cifrado de {purpose} está truncado.");
        var plain = new byte[input.Length - 28];
        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(input.AsSpan(0, 12), input.AsSpan(12, plain.Length), input.AsSpan(12 + plain.Length), plain, Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    // Índice ciego: permite unicidad y búsqueda exacta sobre un campo cifrado.
    public string BlindIndex(string value, string purpose) =>
        Convert.ToHexString(HMACSHA256.HashData(_indexKey, Encoding.UTF8.GetBytes(purpose + "|" + value)));
}

// RF-07 / RS-04: el QR solo transporta identificador, token y firma. Todo lo demás se lee
// de la base de datos y se comprueba contra la firma ECDSA P-256 sobre SHA-256.
public sealed class TicketSigner : IDisposable
{
    public const string Version = "IC1";
    private readonly ECDsa _key;

    public TicketSigner(string privateKeyPem)
    {
        _key = ECDsa.Create();
        _key.ImportFromPem(privateKeyPem);
        if (_key.KeySize != 256) throw new InvalidOperationException("QR_SIGNING_KEY_B64 debe ser una clave ECDSA P-256.");
        PublicKeyId = Convert.ToHexString(SHA256.HashData(_key.ExportSubjectPublicKeyInfo()))[..16];
    }

    public string PublicKeyId { get; }

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Canonical(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return string.Join('|', Version, ticket.Id.ToString("N"), ticket.Number, ticket.EmployeeId.ToString("N"),
            ticket.VehicleId.ToString("N"), ticket.FuelTypeId.ToString("N"),
            ticket.AuthorizedQuantity.ToString("0.000", CultureInfo.InvariantCulture),
            ticket.IssuedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ticket.ExpiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ticket.TokenHash);
    }

    public void Sign(Ticket ticket)
    {
        var data = Encoding.UTF8.GetBytes(Canonical(ticket));
        ticket.Digest = Convert.ToHexString(SHA256.HashData(data));
        ticket.Signature = Base64Url(_key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    // Verifica que la fila de la base no fue alterada desde la emisión y que el token es el correcto.
    public bool Verify(Ticket ticket, string token, string signature)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashToken(token)), Encoding.ASCII.GetBytes(ticket.TokenHash))) return false;
        if (!string.Equals(signature, ticket.Signature, StringComparison.Ordinal)) return false;
        var data = Encoding.UTF8.GetBytes(Canonical(ticket));
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(data)), ticket.Digest, StringComparison.Ordinal)) return false;
        byte[] raw;
        try { raw = FromBase64Url(signature); }
        catch (FormatException) { return false; }
        return _key.VerifyData(data, raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string Payload(Ticket ticket, string token)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return $"{Version}.{ticket.Id:N}.{token}.{ticket.Signature}";
    }

    public static bool TryParsePayload(string payload, out Guid id, out string token, out string signature)
    {
        id = Guid.Empty; token = string.Empty; signature = string.Empty;
        var parts = (payload ?? string.Empty).Trim().Split('.');
        if (parts.Length != 4 || parts[0] != Version || parts[2].Length != 22 || parts[3].Length is < 80 or > 90) return false;
        if (!Guid.TryParseExact(parts[1], "N", out id)) return false;
        token = parts[2]; signature = parts[3];
        return true;
    }

    // Firma separada por dominio: el prefijo impide confundir una firma de QR con otra de otro uso.
    public string SignDetached(string purpose, string data) =>
        Base64Url(_key.SignData(Encoding.UTF8.GetBytes(purpose + "|" + data), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public bool VerifyDetached(string purpose, string data, string signature)
    {
        byte[] raw;
        try { raw = FromBase64Url(signature ?? string.Empty); }
        catch (FormatException) { return false; }
        return _key.VerifyData(Encoding.UTF8.GetBytes(purpose + "|" + data), raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public string PublicKeyPem() => _key.ExportSubjectPublicKeyInfoPem();

    public void Dispose() => _key.Dispose();

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
