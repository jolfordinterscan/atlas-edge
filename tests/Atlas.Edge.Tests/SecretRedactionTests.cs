using Atlas.Edge.Security;

namespace Atlas.Edge.Tests;

public sealed class SecretRedactionTests
{
    [Fact]
    public void Redact_MasksSecrets()
    {
        var redacted = SecretRedactor.Redact("abcdefghijklmnopqrstuvwxyz");

        Assert.StartsWith("abcd", redacted, StringComparison.Ordinal);
        Assert.EndsWith("wxyz", redacted, StringComparison.Ordinal);
        Assert.Contains("...", redacted, StringComparison.Ordinal);
    }
}
