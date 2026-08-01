using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Security;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task CredentialPersistence_StoresAndReloadsProtectedPayload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "atlas-edge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var options = Options.Create(new AtlasEdgeOptions
            {
                CredentialStorePath = tempDir
            });

            var store = new MacDevelopmentCredentialStore(options);
            var credentials = new StoredEdgeCredentials(
                new AgentIdentity("agent-1", "device-1", "tenant-a", "Test", false, DateTimeOffset.UtcNow),
                "device-1",
                "https://localhost:7143/",
                "UTC",
                "super-secret-access-token",
                "refresh-token-placeholder",
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow);

            await store.SaveAsync(credentials, CancellationToken.None);
            var reloaded = await store.LoadAsync(CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(credentials.Identity.AgentId, reloaded!.Identity.AgentId);
            Assert.Equal(credentials.AccessToken, reloaded.AccessToken);

            var persisted = await File.ReadAllTextAsync(Path.Combine(tempDir, "credentials.protected.json"));
            Assert.DoesNotContain("super-secret-access-token", persisted, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
