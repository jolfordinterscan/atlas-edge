using System.Globalization;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Microsoft.Win32;

namespace Atlas.Edge.RicohProbe;

public sealed class WindowsRicohRuntimeAvailability(bool sdkBuildEnabled) : IRicohRuntimeAvailability
{
    private const string ActiveXClsid = "{383DF553-B568-4E66-99C6-8ABBEE951537}";

    public RicohRuntimeAvailability Inspect()
    {
        var isWindows = OperatingSystem.IsWindows();
        var isX64 = RuntimeInformation.OSArchitecture == Architecture.X64;
        if (!isWindows)
        {
            return new(false, isX64, false, sdkBuildEnabled);
        }

        try
        {
            using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
            using var clsid = classes.OpenSubKey($"CLSID\\{ActiveXClsid}", writable: false);
            var version = clsid?.GetValue("Version") as string;
            return new(true, isX64, clsid is not null, sdkBuildEnabled, NormalizeVersion(version));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new(true, isX64, false, sdkBuildEnabled);
        }
    }

    private static string NormalizeVersion(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 64 ? "Unknown" : value.Trim();
}

public sealed class MachineWideRicohSessionGate(string? gateName = null) : IRicohSessionGate
{
    private const string WindowsSemaphoreName = @"Global\InterScan.AtlasEdge.RicohSdk";
    private const string PortableSemaphoreName = "InterScan.AtlasEdge.RicohSdk";
    private static readonly ConcurrentDictionary<string, Semaphore> PortableSemaphores = new(StringComparer.Ordinal);

    private readonly string name = gateName ??
        (OperatingSystem.IsWindows() ? WindowsSemaphoreName : PortableSemaphoreName);

    public IDisposable? TryAcquire()
    {
        var ownsHandle = OperatingSystem.IsWindows();
        var semaphore = ownsHandle
            ? new Semaphore(1, 1, name)
            : PortableSemaphores.GetOrAdd(name, _ => new Semaphore(1, 1));
        try
        {
            if (!semaphore.WaitOne(TimeSpan.Zero))
            {
                if (ownsHandle)
                {
                    semaphore.Dispose();
                }

                return null;
            }

            return new SemaphoreLease(semaphore, ownsHandle);
        }
        catch
        {
            if (ownsHandle)
            {
                semaphore.Dispose();
            }

            throw;
        }
    }

    private sealed class SemaphoreLease(Semaphore semaphore, bool ownsHandle) : IDisposable
    {
        private Semaphore? current = semaphore;

        public void Dispose()
        {
            var acquired = Interlocked.Exchange(ref current, null);
            if (acquired is null)
            {
                return;
            }

            try
            {
                acquired.Release();
            }
            finally
            {
                if (ownsHandle)
                {
                    acquired.Dispose();
                }
            }
        }
    }
}

public sealed class RicohSerialValidator : IRicohSerialValidator
{
    public bool TryValidate(string? value, RicohProbeRequest request, out string? serial)
    {
        serial = value?.Trim();
        if (string.IsNullOrEmpty(serial) || serial.Length > 128)
        {
            serial = null;
            return false;
        }

        if (serial.Any(character => char.IsControl(character) || char.IsSurrogate(character)) ||
            serial.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            serial.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
            serial.All(character => character == '0') ||
            LooksLikeTopologyIdentifier(serial) ||
            EqualsNormalized(serial, request.Model) ||
            EqualsVidPid(serial, request.UsbVendorId, request.UsbProductId))
        {
            serial = null;
            return false;
        }

        return true;
    }

    public string Mask(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var visible = serial.Length <= 4 ? serial : serial[^4..];
        return $"******{visible}";
    }

    private static bool LooksLikeTopologyIdentifier(string value) =>
        value.Contains('&', StringComparison.Ordinal) ||
        value.Contains('\\', StringComparison.Ordinal) ||
        value.Contains("VID_", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("PID_", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("USB", StringComparison.OrdinalIgnoreCase);

    private static bool EqualsNormalized(string value, string? comparison) =>
        !string.IsNullOrWhiteSpace(comparison) &&
        Normalize(value).Equals(Normalize(comparison), StringComparison.Ordinal);

    private static bool EqualsVidPid(string value, string? vendorId, string? productId)
    {
        var normalized = Normalize(value);
        var vid = NormalizeHex(vendorId);
        var pid = NormalizeHex(productId);
        return (!string.IsNullOrEmpty(vid) && normalized == vid) ||
            (!string.IsNullOrEmpty(pid) && normalized == pid) ||
            (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(pid) && normalized == vid + pid);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpper(CultureInfo.InvariantCulture);

    private static string NormalizeHex(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Normalize(value);
}
