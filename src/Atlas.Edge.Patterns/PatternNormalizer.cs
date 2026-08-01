using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Patterns;

internal static class PatternNormalizer
{
    public static ImmutableSortedDictionary<string, string> Normalize(ScannerEvidenceSnapshot snapshot)
    {
        var fields = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        AddNested(fields, snapshot.Identity, "identity.manufacturer", identity => identity.Manufacturer);
        AddNested(fields, snapshot.Identity, "identity.model", identity => identity.Model);

        AddNested(fields, snapshot.Driver, "driver.package", driver => driver.PackageName);
        AddNested(fields, snapshot.Driver, "driver.version", driver => driver.Version);
        AddNested(fields, snapshot.Driver, "driver.provider", driver => driver.Provider);

        AddNested(fields, snapshot.Connection, "usb.present", connection => connection.Present, FormatBoolean);

        if (snapshot.Services.State == EvidenceValueState.Known)
        {
            foreach (var service in snapshot.Services.Value)
            {
                var prefix = DynamicKey("service", service.ServiceName);
                Add(fields, service.State, prefix + ".state", value => NormalizeText(value.ToString()));
                Add(fields, service.Version, prefix + ".version");
            }
        }

        if (snapshot.Events.State == EvidenceValueState.Known)
        {
            foreach (var eventEvidence in snapshot.Events.Value)
            {
                var code = NormalizeText(eventEvidence.StableEventCode);
                if (code.Length > 0)
                {
                    fields[DynamicKey("event", $"{eventEvidence.Kind}:{code}")] = "present";
                }
            }
        }

        AddCounters(fields, snapshot.Counters, "counter");
        AddNested(fields, snapshot.Firmware, "firmware.version", firmware => firmware.Version);

        if (snapshot.Maintenance.State == EvidenceValueState.Known)
        {
            foreach (var item in snapshot.Maintenance.Value.Values)
            {
                Add(fields, item.Value, DynamicKey("maintenance", item.Key));
            }
        }

        if (snapshot.LogReferences.State == EvidenceValueState.Known)
        {
            foreach (var code in snapshot.LogReferences.Value
                         .SelectMany(reference => reference.StableErrorCodes)
                         .Select(NormalizeText)
                         .Where(code => code.Length > 0)
                         .Distinct(StringComparer.Ordinal))
            {
                fields[DynamicKey("log.error", code)] = "present";
            }
        }

        if (snapshot.Network.State == EvidenceValueState.Known)
        {
            var network = snapshot.Network.Value;
            Add(fields, network.Present, "network.present", FormatBoolean);
            Add(fields, network.Firmware, "network.firmware");
            Add(fields, network.ErrorState, "network.error_state");
            AddCounters(fields, network.Counters, "network.counter");
        }

        return fields.ToImmutable();
    }

    private static void AddCounters(
        ImmutableSortedDictionary<string, string>.Builder fields,
        EvidenceValue<CounterEvidence> counters,
        string prefix)
    {
        if (counters.State != EvidenceValueState.Known)
        {
            return;
        }

        foreach (var counter in counters.Value.Counters)
        {
            Add(
                fields,
                counter.Value,
                DynamicKey(prefix, counter.Key),
                value => value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddNested<TOuter, TValue>(
        ImmutableSortedDictionary<string, string>.Builder fields,
        EvidenceValue<TOuter> outer,
        string key,
        Func<TOuter, EvidenceValue<TValue>> selector,
        Func<TValue, string>? formatter = null)
    {
        if (outer.State == EvidenceValueState.Known)
        {
            Add(fields, selector(outer.Value), key, formatter);
        }
    }

    private static void Add<T>(
        ImmutableSortedDictionary<string, string>.Builder fields,
        EvidenceValue<T> value,
        string key,
        Func<T, string>? formatter = null)
    {
        if (value.State != EvidenceValueState.Known)
        {
            return;
        }

        var normalized = formatter is null
            ? NormalizeText(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty)
            : formatter(value.Value);
        if (normalized.Length > 0)
        {
            fields[key] = normalized;
        }
    }

    private static string DynamicKey(string prefix, string value)
    {
        var normalized = NormalizeText(value);
        return $"{prefix}[{Encoding.UTF8.GetByteCount(normalized).ToString(CultureInfo.InvariantCulture)}]{normalized}";
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string NormalizeText(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
}
