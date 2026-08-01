using Atlas.Edge.Security;

namespace Atlas.Edge.Tests;

public sealed class SecretRedactionTests
{
    [Fact]
    public void Redact_MasksSecrets()
    {
        var redacted = SecretRedactor.Redact("abcdefghijklmnopqrstuvwxyz");

        Assert.StartsWith("sha256:", redacted, StringComparison.Ordinal);
        Assert.Equal(19, redacted.Length);
        Assert.DoesNotContain("abcd", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("wxyz", redacted, StringComparison.Ordinal);
    }
}
