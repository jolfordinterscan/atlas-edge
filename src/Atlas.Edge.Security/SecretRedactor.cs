namespace Atlas.Edge.Security;

public static class SecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        if (value.Length <= 8)
        {
            return "<redacted>";
        }

        return $"{value[..4]}...{value[^4..]}";
    }
}
