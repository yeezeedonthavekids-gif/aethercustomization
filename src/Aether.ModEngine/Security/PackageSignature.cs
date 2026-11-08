using System.Security.Cryptography;
using System.Text;
using Aether.Storage;

namespace Aether.ModEngine.Security;

public enum VerificationLevel
{
    /// <summary>Ships inside AETHER itself.</summary>
    BuiltIn,
    /// <summary>Valid signature from a publisher the user trusts.</summary>
    Trusted,
    /// <summary>Valid signature, but the publisher is not in the trusted list.</summary>
    SignedUntrusted,
    /// <summary>No signature present.</summary>
    Unsigned,
    /// <summary>Signature present but does not match the content — the package has been tampered with.</summary>
    Invalid,
}

public sealed class SignatureDocument
{
    public const string FileName = "signature.json";
    public string Algorithm { get; init; } = "ECDSA-P256-SHA256";
    public required string PublisherId { get; init; }
    public required string PublisherName { get; init; }
    /// <summary>SubjectPublicKeyInfo, base64.</summary>
    public required string PublicKey { get; init; }
    /// <summary>Hex SHA-256 package digest that was signed.</summary>
    public required string Digest { get; init; }
    /// <summary>Base64 IEEE P1363 signature over the digest bytes.</summary>
    public required string Signature { get; init; }
}

public sealed class TrustedPublisher
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string PublicKey { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record VerificationResult(VerificationLevel Level, string Digest, string? PublisherId, string? PublisherName, string Message);

public static class PackageIntegrity
{
    /// <summary>
    /// The package digest is SHA-256 over "path\nsha256(file)\n" for every file except the signature,
    /// in ordinal path order. Any added, removed, renamed or modified file changes it.
    /// </summary>
    public static string ComputeDigest(IReadOnlyDictionary<string, byte[]> files)
    {
        var sb = new StringBuilder();
        foreach (var path in files.Keys.Where(p => !p.Equals(SignatureDocument.FileName, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            sb.Append(path).Append('\n').Append(Convert.ToHexStringLower(SHA256.HashData(files[path]))).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static SignatureDocument Sign(IReadOnlyDictionary<string, byte[]> files, ECDsa key, string publisherId, string publisherName)
    {
        var digest = ComputeDigest(files);
        var signature = key.SignData(Convert.FromHexString(digest), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new SignatureDocument
        {
            PublisherId = publisherId,
            PublisherName = publisherName,
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Digest = digest,
            Signature = Convert.ToBase64String(signature),
        };
    }

    public static VerificationResult Verify(IReadOnlyDictionary<string, byte[]> files, IEnumerable<TrustedPublisher> trusted)
    {
        var digest = ComputeDigest(files);
        if (!files.TryGetValue(SignatureDocument.FileName, out var sigBytes))
            return new VerificationResult(VerificationLevel.Unsigned, digest, null, null, "The package is not signed.");

        SignatureDocument? sig;
        try { sig = AetherJson.Deserialize<SignatureDocument>(Encoding.UTF8.GetString(sigBytes)); }
        catch (Exception) { sig = null; }
        if (sig is null || sig.Algorithm != "ECDSA-P256-SHA256")
            return new VerificationResult(VerificationLevel.Invalid, digest, null, null, "The signature file is malformed or uses an unsupported algorithm.");

        if (!string.Equals(sig.Digest, digest, StringComparison.OrdinalIgnoreCase))
            return new VerificationResult(VerificationLevel.Invalid, digest, sig.PublisherId, sig.PublisherName, "The package contents do not match its signature (files were modified).");

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(sig.PublicKey), out _);
            var ok = ecdsa.VerifyData(Convert.FromHexString(digest), Convert.FromBase64String(sig.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            if (!ok) return new VerificationResult(VerificationLevel.Invalid, digest, sig.PublisherId, sig.PublisherName, "The signature is not valid.");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return new VerificationResult(VerificationLevel.Invalid, digest, sig.PublisherId, sig.PublisherName, $"The signature could not be verified: {ex.Message}");
        }

        var publisher = trusted.FirstOrDefault(t => t.Id == sig.PublisherId && t.PublicKey == sig.PublicKey);
        return publisher is not null
            ? new VerificationResult(VerificationLevel.Trusted, digest, sig.PublisherId, publisher.Name, $"Signed by trusted publisher {publisher.Name}.")
            : new VerificationResult(VerificationLevel.SignedUntrusted, digest, sig.PublisherId, sig.PublisherName, $"Signed by '{sig.PublisherName}', who is not in your trusted publishers list.");
    }
}

public interface ITrustedPublisherStore
{
    IReadOnlyList<TrustedPublisher> GetAll();
    void Add(TrustedPublisher publisher);
    bool Remove(string id);
}

public sealed class TrustedPublisherStore(IDocumentStore store) : ITrustedPublisherStore
{
    private const string Collection = "trusted-publishers";
    public IReadOnlyList<TrustedPublisher> GetAll() => store.GetAll<TrustedPublisher>(Collection);
    public void Add(TrustedPublisher publisher) => store.Put(Collection, publisher.Id, publisher);
    public bool Remove(string id) => store.Delete(Collection, id);
}
