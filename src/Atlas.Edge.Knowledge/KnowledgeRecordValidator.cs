namespace Atlas.Edge.Knowledge;

internal static class KnowledgeRecordValidator
{
    public static void Validate(KnowledgeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.RecordId == Guid.Empty)
        {
            throw new ArgumentException("RecordId must not be empty.", nameof(record));
        }

        if (record.Issue is null)
        {
            throw new ArgumentException("Issue is required.", nameof(record));
        }

        Required(record.Issue.Name, "Issue.Name", record);
        Required(record.Issue.Description, "Issue.Description", record);
        if (record.Scanner is null)
        {
            throw new ArgumentException("Scanner is required.", nameof(record));
        }

        Required(record.Scanner.Manufacturer.Name, "Scanner.Manufacturer", record);
        Required(record.Scanner.Model.Name, "Scanner.Model", record);
        if (record.Customer is null)
        {
            throw new ArgumentException("Customer is required.", nameof(record));
        }

        Required(record.Customer.CustomerId, "Customer.CustomerId", record);
        if (record.Site is null)
        {
            throw new ArgumentException("Site is required.", nameof(record));
        }

        Required(record.Site.SiteId, "Site.SiteId", record);

        if (record.Timestamp.Value == default)
        {
            throw new ArgumentException("Timestamp must be provided.", nameof(record));
        }

        if (record.Observations.IsDefault || record.Evidence.IsDefault || record.PartsUsed.IsDefault)
        {
            throw new ArgumentException("Knowledge record collections must be initialized.", nameof(record));
        }

        foreach (var observation in record.Observations)
        {
            if (observation is null)
            {
                throw new ArgumentException("Observation is required.", nameof(record));
            }

            Required(observation.Description, "Observation.Description", record);
            if (observation.ObservedAt.Value == default)
            {
                throw new ArgumentException("Observation timestamp must be provided.", nameof(record));
            }
        }

        foreach (var evidence in record.Evidence)
        {
            if (evidence is null)
            {
                throw new ArgumentException("Evidence is required.", nameof(record));
            }

            Required(evidence.Kind, "Evidence.Kind", record);
            Required(evidence.Description, "Evidence.Description", record);
        }

        if (record.Resolution is not null)
        {
            Required(record.Resolution.Description, "Resolution.Description", record);
        }

        if (record.Confidence is { } confidence && confidence.Percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(record), "Recorded confidence must be between 0 and 100.");
        }

        if (record.RepairTime is { } repairTime && repairTime.Duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(record), "Repair time must not be negative.");
        }

        foreach (var part in record.PartsUsed)
        {
            if (part is null)
            {
                throw new ArgumentException("Part is required.", nameof(record));
            }

            Required(part.PartNumber, "PartUsed.PartNumber", record);
            Required(part.Name, "PartUsed.Name", record);
            if (part.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(record), "Part quantity must be greater than zero.");
            }
        }
    }

    private static void Required(string? value, string field, KnowledgeRecord record)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field} is required.", nameof(record));
        }
    }
}
