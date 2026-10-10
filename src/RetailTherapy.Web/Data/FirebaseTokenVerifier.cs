using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>
/// Checks a Firebase "ID token" (the proof of sign-in the browser sends) without any extra package:
/// the token is a signed JWT, and Google publishes the public keys that sign it. We check the signature,
/// who it was issued for (our project), who issued it, and that it has not expired. Nothing is called per
/// request except a cached download of Google's public keys.
/// </summary>
public sealed class FirebaseTokenVerifier
{
    private readonly IHttpClientFactory _http;
    private readonly IOptions<AuthOptions> _opt;
    private readonly ILogger<FirebaseTokenVerifier> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, X509Certificate2> _keys = new();
    private DateTime _expires = DateTime.MinValue;
    private DateTime _lastFetch = DateTime.MinValue;

    public FirebaseTokenVerifier(IHttpClientFactory http, IOptions<AuthOptions> opt, ILogger<FirebaseTokenVerifier> log)
    {
        _http = http;
        _opt = opt;
        _log = log;
    }

    public bool Enabled => _opt.Value.Enabled;

    /// <summary>The signed-in person, or null if the token is missing, forged, expired or for another project.</summary>
    public async Task<SessionUser?> VerifyAsync(string? bearer, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(bearer) || bearer.Length > 4096) return null;
        try
        {
            var parts = bearer.Split('.');
            if (parts.Length != 3) return null;

            using var header = JsonDocument.Parse(B64(parts[0]));
            var h = header.RootElement;
            if (h.GetProperty("alg").GetString() != "RS256") return null;
            var kid = h.GetProperty("kid").GetString();
            if (string.IsNullOrEmpty(kid)) return null;

            var cert = await FindKeyAsync(kid, ct);
            if (cert is null) return null;
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is null) return null;
            var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!rsa.VerifyData(signed, B64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

            using var payload = JsonDocument.Parse(B64(parts[1]));
            var p = payload.RootElement;
            var project = _opt.Value.ProjectId;
            if (p.GetProperty("aud").GetString() != project) return null;
            if (p.GetProperty("iss").GetString() != "https://securetoken.google.com/" + project) return null;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (p.GetProperty("exp").GetInt64() <= now) return null;
            if (p.GetProperty("iat").GetInt64() > now + 300) return null;       // 5 minutes of clock slack
            var uid = p.GetProperty("sub").GetString();
            if (string.IsNullOrEmpty(uid) || uid.Length > 128) return null;

            var provider = "";
            if (p.TryGetProperty("firebase", out var fb) && fb.TryGetProperty("sign_in_provider", out var sp))
                provider = sp.GetString() ?? "";
            var email = p.TryGetProperty("email", out var e) ? e.GetString() : null;
            var verified = p.TryGetProperty("email_verified", out var ev) && ev.ValueKind == JsonValueKind.True;
            return new SessionUser(uid, provider, email, verified);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or CryptographicException)
        {
            return null;   // anything malformed is simply "not signed in"
        }
    }

    private async Task<X509Certificate2?> FindKeyAsync(string kid, CancellationToken ct)
    {
        if (DateTime.UtcNow < _expires && _keys.TryGetValue(kid, out var hit)) return hit;

        await _gate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow < _expires && _keys.TryGetValue(kid, out hit)) return hit;
            // Google rotates keys now and then. Re-download when the cache expired or a new key id shows up,
            // but never more than once a minute (so a flood of bad tokens cannot make us hammer Google).
            if (DateTime.UtcNow - _lastFetch < TimeSpan.FromMinutes(1)) return _keys.GetValueOrDefault(kid);
            _lastFetch = DateTime.UtcNow;

            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var res = await client.GetAsync(_opt.Value.CertsUrl, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var fresh = new Dictionary<string, X509Certificate2>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                fresh[prop.Name] = X509Certificate2.CreateFromPem(prop.Value.GetString()!);
            _keys = fresh;
            var maxAge = res.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1);
            _expires = DateTime.UtcNow + (maxAge < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : maxAge);
            return _keys.GetValueOrDefault(kid);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load Firebase signing keys");
            return _keys.GetValueOrDefault(kid);   // keep using the old keys if Google cannot be reached
        }
        finally { _gate.Release(); }
    }

    private static byte[] B64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
