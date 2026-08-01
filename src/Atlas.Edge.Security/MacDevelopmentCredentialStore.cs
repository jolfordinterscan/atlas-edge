using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Edge.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Security;

public sealed class MacDevelopmentCredentialStore : ICredentialStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly string _storeFilePath;

    public MacDevelopmentCredentialStore(IOptions<AtlasEdgeOptions> options)
    {
        var configuredPath = options.Value.CredentialStorePath;
        var storeDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".atlas-edge",
                "dev-credential-store")
            : configuredPath;

        Directory.CreateDirectory(storeDirectory);

        var keyDirectory = Path.Combine(storeDirectory, "keys");
        Directory.CreateDirectory(keyDirectory);

        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory));
        _protector = provider.CreateProtector("Atlas.Edge.Security.DevCredentialStore.v1");

        _storeFilePath = Path.Combine(storeDirectory, "credentials.protected.json");
    }

    public async Task<StoredEdgeCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_storeFilePath))
        {
            return null;
        }

        var protectedPayload = await File.ReadAllTextAsync(_storeFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return null;
        }

        var unprotected = _protector.Unprotect(protectedPayload);
        var credentials = JsonSerializer.Deserialize<StoredEdgeCredentials>(unprotected, SerializerOptions);

        return credentials;
    }

    public async Task SaveAsync(StoredEdgeCredentials credentials, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.Serialize(credentials, SerializerOptions);
        var protectedPayload = _protector.Protect(payload);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(_storeFilePath)!,
            $"credentials.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, protectedPayload, Encoding.UTF8, cancellationToken);
            TryRestrictFileAccess(temporaryPath);
            File.Move(temporaryPath, _storeFilePath, overwrite: true);
            TryRestrictFileAccess(_storeFilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_storeFilePath))
        {
            File.Delete(_storeFilePath);
        }

        return Task.CompletedTask;
    }

    private static void TryRestrictFileAccess(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite);
            }
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (CryptographicException)
        {
        }
    }
}
