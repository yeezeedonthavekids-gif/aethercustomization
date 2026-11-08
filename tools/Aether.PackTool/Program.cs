using System.Security.Cryptography;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Packaging;
using Aether.ModEngine.Security;

// aether-pack: create signing keys, validate, pack, sign and verify AETHER mod packages.
static int Usage()
{
    Console.WriteLine("""
        aether-pack keygen <publisher-id> <out.key>        Create an ECDSA P-256 signing key (keep it secret)
        aether-pack validate <mod-folder>                  Validate aether-mod.json
        aether-pack pack <mod-folder> <out.aethermod> [--key <file> --publisher <id> --name <display name>]
        aether-pack verify <package.aethermod>             Show digest and signature status
        aether-pack pubkey <file.key>                      Print the public key to share with users
        """);
    return 2;
}

if (args.Length == 0) return Usage();
try
{
    switch (args[0])
    {
        case "keygen" when args.Length == 3:
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(args[2], key.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Key written to {args[2]}. Public key:\n{Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
            return 0;
        }
        case "pubkey" when args.Length == 2:
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(args[1]));
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
            return 0;
        }
        case "validate" when args.Length == 2:
        {
            var package = ModPackage.ReadDirectory(args[1]);
            var errors = ManifestValidator.Validate(package.Manifest);
            foreach (var e in errors) Console.WriteLine($"✗ {e}");
            Console.WriteLine(errors.Count == 0 ? $"✓ {package.Manifest.Id} {package.Manifest.Version} is valid." : $"{errors.Count} problem(s).");
            return errors.Count == 0 ? 0 : 1;
        }
        case "pack" when args.Length >= 3:
        {
            var source = ModPackage.ReadDirectory(args[1]);
            var errors = ManifestValidator.Validate(source.Manifest);
            if (errors.Count > 0) { foreach (var e in errors) Console.WriteLine($"✗ {e}"); return 1; }
            var builder = new ModPackageBuilder();
            foreach (var (path, bytes) in source.Files.Where(f => f.Key != SignatureDocument.FileName)) builder.WithFile(path, bytes);
            string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            if (Opt("--key") is { } keyFile)
            {
                using var key = ECDsa.Create();
                key.ImportFromPem(File.ReadAllText(keyFile));
                builder.SignWith(key, Opt("--publisher") ?? throw new ArgumentException("--publisher is required when signing"), Opt("--name") ?? Opt("--publisher")!);
            }
            File.WriteAllBytes(args[2], builder.BuildZip());
            Console.WriteLine($"✓ Wrote {args[2]}");
            return 0;
        }
        case "verify" when args.Length == 2:
        {
            var package = ModPackage.ReadZip(args[1]);
            var result = PackageIntegrity.Verify(package.Files, []);
            Console.WriteLine($"{package.Manifest.Id} {package.Manifest.Version}\nDigest: {result.Digest}\nSignature: {result.Level} — {result.Message}");
            return result.Level == VerificationLevel.Invalid ? 1 : 0;
        }
        default:
            return Usage();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
