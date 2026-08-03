using System.Security.Cryptography;
using System.Text.Json;
using Atlas.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Security;

public sealed class WindowsProtectedCredentialStore : ICredentialStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _storeFilePath;

    public WindowsProtectedCredentialStore(IOptions<AtlasEdgeOptions> options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows protected credential storage requires Windows.");
        }

        var configuredPath = options.Value.CredentialStorePath;

        var storeDirectory = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "InterScan",
                "Atlas Edge",
                "identity")
            : configuredPath;

        Directory.CreateDirectory(storeDirectory);

        _storeFilePath = Path.Combine(
            storeDirectory,
            "credentials.protected.bin");
    }

    public async Task<StoredEdgeCredentials?> LoadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_storeFilePath))
        {
            return null;
        }

        var protectedPayload = await File.ReadAllBytesAsync(
            _storeFilePath,
            cancellationToken);

        if (protectedPayload.Length == 0)
        {
            return null;
        }

        var payload = ProtectedData.Unprotect(
            protectedPayload,
            optionalEntropy: null,
            scope: DataProtectionScope.LocalMachine);

        return JsonSerializer.Deserialize<StoredEdgeCredentials>(
            payload,
            SerializerOptions);
    }

    public async Task SaveAsync(
        StoredEdgeCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            credentials,
            SerializerOptions);

        var protectedPayload = ProtectedData.Protect(
            payload,
            optionalEntropy: null,
            scope: DataProtectionScope.LocalMachine);

        var directory = Path.GetDirectoryName(_storeFilePath)!;
        var temporaryPath = Path.Combine(
            directory,
            $"credentials.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                protectedPayload,
                cancellationToken);

            File.Move(
                temporaryPath,
                _storeFilePath,
                overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);

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
}
