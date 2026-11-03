using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

internal enum UsbDeviceCategory
{
    Storage,
    Peripheral,
}

internal enum UsbDeviceChangeKind
{
    Arrival,
    Removal,
}

internal sealed record UsbDeviceChange(
    UsbDeviceChangeKind Kind,
    UsbDeviceCategory Category,
    string Name,
    double? UsedPercent = null,
    string? DriveLetter = null);

internal sealed record UsbPnpInfo(string Name, string PnpDeviceId, UsbDeviceCategory Category);

internal sealed record UsbStorageVolume(string Id, string DisplayName, double UsedPercent);

// Native WM_DEVICECHANGE gives an immediate trigger. The actual change is derived
// by diffing a small USB PnP/storage snapshot, which also supplies friendly names,
// drive letters and free-space percentages.
internal sealed class WindowsUsbDeviceWatcher : IDisposable
{
    private const int DebounceMilliseconds = 150;
    private const int RetryMilliseconds = 250;
    private const int NoChangeRetries = 6;
    private const int StorageRetries = 10;

    private readonly Action<UsbDeviceChange> _changed;
    private readonly object _gate = new();
    private readonly object _volumeCacheGate = new();
    private readonly Timer _debounceTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, (bool IsUsb, long CheckedAt)> _usbVolumeCache = new(StringComparer.OrdinalIgnoreCase);
    private WindowsDeviceChangeMonitor? _monitor;
    private Timer? _fallbackTimer;
    private Dictionary<string, UsbPnpInfo> _lastPnp = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, UsbStorageVolume> _lastStorage = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _refreshAgain;
    private bool _baselineReady;
    private bool _storagePending;
    private int _noChangeRetries;
    private int _started;
    private int _disposed;

    internal WindowsUsbDeviceWatcher(Action<UsbDeviceChange> changed)
    {
        _changed = changed;
        _debounceTimer = new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal bool IsRunning => Volatile.Read(ref _started) == 1;

    internal void Start()
    {
        if (_disposed != 0 || Interlocked.Exchange(ref _started, 1) == 1) return;
        try
        {
            _monitor = new WindowsDeviceChangeMonitor(OnNativeDeviceChange);
            AppLog.Info("USB device watcher started with native device-change notifications.");
        }
        catch (Exception ex)
        {
            _monitor = null;
            AppLog.Error("Native device-change notifications are unavailable; falling back to USB polling.", ex);
            _fallbackTimer = new Timer(_ => OnNativeDeviceChange(), null, 0, 1000);
        }

        _ = Task.Run(InitializeBaselineAsync);
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0 && _monitor is null && _fallbackTimer is null) return;
        _monitor?.Dispose();
        _monitor = null;
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
        try { _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        lock (_gate)
        {
            _baselineReady = false;
            _lastPnp = new(StringComparer.OrdinalIgnoreCase);
            _lastStorage = new(StringComparer.OrdinalIgnoreCase);
            _storagePending = false;
            _noChangeRetries = 0;
            _refreshAgain = false;
        }
        lock (_volumeCacheGate) _usbVolumeCache.Clear();
    }

    private async Task InitializeBaselineAsync()
    {
        try
        {
            var pnp = await Task.Run(TakePnpSnapshot).ConfigureAwait(false);
            var storage = await Task.Run(() => TakeStorageSnapshot()).ConfigureAwait(false);
            if (!IsRunning) return;
            lock (_gate)
            {
                if (_baselineReady) return;
                _lastPnp = pnp;
                _lastStorage = storage;
                _baselineReady = true;
            }
            AppLog.Info($"USB device baseline: {pnp.Count} PnP nodes, {storage.Count} USB storage volumes.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to establish the USB device baseline.", ex);
        }
    }

    private void OnNativeDeviceChange()
    {
        if (!IsRunning) return;
        try { _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private async Task RefreshAsync()
    {
        if (!IsRunning) return;
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            _refreshAgain = true;
            return;
        }

        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshOnceAsync().ConfigureAwait(false);
            }
            while (_refreshAgain && IsRunning);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to process a USB device change.", ex);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshOnceAsync()
    {
        Dictionary<string, UsbPnpInfo> pnp;
        Dictionary<string, UsbStorageVolume> storage;
        try
        {
            pnp = await Task.Run(TakePnpSnapshot).ConfigureAwait(false);
            storage = await Task.Run(() => TakeStorageSnapshot(recheckUnknown: true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to read USB device state.", ex);
            return;
        }

        Dictionary<string, UsbPnpInfo> oldPnp;
        Dictionary<string, UsbStorageVolume> oldStorage;
        lock (_gate)
        {
            if (!IsRunning) return;
            if (!_baselineReady)
            {
                _lastPnp = pnp;
                _lastStorage = storage;
                _baselineReady = true;
                return;
            }
            oldPnp = _lastPnp;
            oldStorage = _lastStorage;
            _lastPnp = pnp;
            _lastStorage = storage;
        }

        var change = SelectChange(oldPnp, oldStorage, pnp, storage, out bool storagePnpPending);
        if (change is not null)
        {
            ResetPending();
            _changed(change);
            return;
        }

        if (storagePnpPending)
        {
            _storagePending = true;
            _noChangeRetries = 0;
            await RetryStorageAsync().ConfigureAwait(false);
            return;
        }

        // The broadcast can arrive before the device tree settles. Try a few short refreshes.
        if (_noChangeRetries++ < NoChangeRetries && IsRunning)
        {
            await Task.Delay(RetryMilliseconds).ConfigureAwait(false);
            _refreshAgain = true;
        }
        else
        {
            _noChangeRetries = 0;
        }
    }

    internal static UsbDeviceChange? SelectChange(
        IReadOnlyDictionary<string, UsbPnpInfo> oldPnp,
        IReadOnlyDictionary<string, UsbStorageVolume> oldStorage,
        IReadOnlyDictionary<string, UsbPnpInfo> newPnp,
        IReadOnlyDictionary<string, UsbStorageVolume> newStorage,
        out bool storagePnpPending)
    {
        storagePnpPending = false;

        UsbStorageVolume? addedStorage = newStorage.Values
            .Where(v => !oldStorage.ContainsKey(v.Id))
            .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (addedStorage is not null)
            return new UsbDeviceChange(UsbDeviceChangeKind.Arrival, UsbDeviceCategory.Storage,
                addedStorage.DisplayName, addedStorage.UsedPercent, addedStorage.Id);

        UsbStorageVolume? removedStorage = oldStorage.Values
            .Where(v => !newStorage.ContainsKey(v.Id))
            .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (removedStorage is not null)
            return new UsbDeviceChange(UsbDeviceChangeKind.Removal, UsbDeviceCategory.Storage,
                removedStorage.DisplayName, null, removedStorage.Id);

        var addedPnp = newPnp.Values.Where(v => !oldPnp.ContainsKey(v.PnpDeviceId)).ToList();
        if (addedPnp.Any(p => p.Category == UsbDeviceCategory.Storage))
        {
            storagePnpPending = true;
            return null;
        }

        var removedPnp = oldPnp.Values.Where(v => !newPnp.ContainsKey(v.PnpDeviceId)).ToList();
        if (removedPnp.Any(p => p.Category == UsbDeviceCategory.Storage))
            return new UsbDeviceChange(UsbDeviceChangeKind.Removal, UsbDeviceCategory.Storage,
                LocalizationManager.Text("USB 存储设备", "USB storage device"));

        var addedPeripheral = SelectBest(addedPnp.Where(p => p.Category == UsbDeviceCategory.Peripheral));
        if (addedPeripheral is not null)
            return new UsbDeviceChange(UsbDeviceChangeKind.Arrival, UsbDeviceCategory.Peripheral, addedPeripheral.Name);

        var removedPeripheral = SelectBest(removedPnp.Where(p => p.Category == UsbDeviceCategory.Peripheral));
        if (removedPeripheral is not null)
            return new UsbDeviceChange(UsbDeviceChangeKind.Removal, UsbDeviceCategory.Peripheral, removedPeripheral.Name);

        return null;
    }

    private async Task RetryStorageAsync()
    {
        for (int attempt = 0; attempt < StorageRetries && IsRunning && _storagePending; attempt++)
        {
            await Task.Delay(RetryMilliseconds).ConfigureAwait(false);
            if (!IsRunning) return;

            Dictionary<string, UsbStorageVolume> storage;
            try { storage = await Task.Run(() => TakeStorageSnapshot(recheckUnknown: true)).ConfigureAwait(false); }
            catch (Exception ex)
            {
                AppLog.Error("Unable to refresh USB storage volumes.", ex);
                continue;
            }

            Dictionary<string, UsbStorageVolume> old;
            lock (_gate)
            {
                old = _lastStorage;
                _lastStorage = storage;
            }
            var added = storage.Values.Where(v => !old.ContainsKey(v.Id))
                .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (added is not null)
            {
                ResetPending();
                _changed(new UsbDeviceChange(UsbDeviceChangeKind.Arrival, UsbDeviceCategory.Storage,
                    added.DisplayName, added.UsedPercent, added.Id));
                return;
            }
        }

        if (_storagePending)
        {
            ResetPending();
            _changed(new UsbDeviceChange(UsbDeviceChangeKind.Arrival, UsbDeviceCategory.Storage,
                LocalizationManager.Text("USB 存储设备", "USB storage device")));
        }
    }

    private void ResetPending()
    {
        _storagePending = false;
        _noChangeRetries = 0;
    }

    private Dictionary<string, UsbPnpInfo> TakePnpSnapshot()
    {
        var result = new Dictionary<string, UsbPnpInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, Service, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'");
            foreach (ManagementObject instance in searcher.Get())
            {
                string pnpDeviceId = ReadString(instance, "PNPDeviceID");
                if (string.IsNullOrWhiteSpace(pnpDeviceId)
                    || pnpDeviceId.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ReadInt(instance, "ConfigManagerErrorCode") > 0) continue;

                string name = ReadString(instance, "Name");
                string service = ReadString(instance, "Service");
                string pnpClass = ReadString(instance, "PNPClass");
                result[pnpDeviceId] = new UsbPnpInfo(name, pnpDeviceId, Classify(name, service, pnpClass));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to enumerate USB PnP devices.", ex);
        }
        return result;
    }

    private Dictionary<string, UsbStorageVolume> TakeStorageSnapshot(bool recheckUnknown = false)
    {
        var result = new Dictionary<string, UsbStorageVolume>(StringComparer.OrdinalIgnoreCase);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex)
        {
            AppLog.Error("Unable to enumerate storage volumes.", ex);
            return result;
        }

        foreach (var drive in drives)
        {
            string letter;
            try { letter = drive.Name.TrimEnd('\\', '/'); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(letter)) continue;
            present.Add(letter);

            bool isUsb;
            lock (_volumeCacheGate)
            {
                long now = Environment.TickCount64;
                if (_usbVolumeCache.TryGetValue(letter, out var cached))
                {
                    isUsb = cached.IsUsb;
                    // A new fixed USB disk can be queried before its PnP/disk nodes settle.
                    // Recheck negative answers at most once per second during the retry window.
                    if (!isUsb && recheckUnknown && now - cached.CheckedAt >= 1000)
                    {
                        isUsb = IsUsbVolume(drive);
                        _usbVolumeCache[letter] = (isUsb, now);
                    }
                }
                else
                {
                    isUsb = IsUsbVolume(drive);
                    _usbVolumeCache[letter] = (isUsb, now);
                }
            }
            if (!isUsb) continue;

            try
            {
                if (!drive.IsReady) continue;
                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                if (total <= 0) continue;
                string label = "";
                try { label = drive.VolumeLabel.Trim(); } catch { }
                string display = FormatStorageName(label, letter, drive.DriveType == DriveType.Removable);
                double used = Math.Clamp((total - free) * 100d / total, 0d, 100d);
                result[letter] = new UsbStorageVolume(letter, display, used);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Unable to read USB storage volume {letter}.", ex);
            }
        }

        lock (_volumeCacheGate)
        {
            foreach (string letter in _usbVolumeCache.Keys.Where(k => !present.Contains(k)).ToArray())
                _usbVolumeCache.Remove(letter);
        }
        return result;
    }

    private static bool IsUsbVolume(DriveInfo drive)
    {
        try
        {
            if (drive.DriveType == DriveType.Removable) return true;
            if (drive.DriveType != DriveType.Fixed) return false;

            string letter = drive.Name.TrimEnd('\\', '/');
            if (letter.Length < 2) return false;

            using var partitions = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{EscapeWql(letter)}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject partition in partitions.Get())
            {
                string partitionId = ReadString(partition, "DeviceID");
                if (string.IsNullOrWhiteSpace(partitionId)) continue;

                using var disks = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{EscapeWql(partitionId)}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject disk in disks.Get())
                {
                    string interfaceType = ReadString(disk, "InterfaceType");
                    string pnpDeviceId = ReadString(disk, "PNPDeviceID");
                    if (string.Equals(interfaceType, "USB", StringComparison.OrdinalIgnoreCase)
                        || pnpDeviceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
                        || HasUsbAncestor(pnpDeviceId))
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to inspect the storage bus type.", ex);
        }
        return false;
    }

    // UASP/SCSI disks do not always report InterfaceType=USB; walk the PnP parent chain.
    private static bool HasUsbAncestor(string deviceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId)) return false;
        try
        {
            if (CM_Locate_DevNodeW(out uint device, deviceInstanceId, 0) != 0) return false;
            for (int depth = 0; depth < 8; depth++)
            {
                if (CM_Get_Parent(out uint parent, device, 0) != 0) return false;
                var buffer = new StringBuilder(1024);
                if (CM_Get_Device_IDW(parent, buffer, (uint)buffer.Capacity, 0) != 0) return false;
                string parentId = buffer.ToString();
                if (parentId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return true;
                device = parent;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to walk the storage device parent chain.", ex);
        }
        return false;
    }

    internal static string FormatStorageName(string? label, string letter, bool removable)
    {
        string text = label?.Trim() ?? "";
        if (text.Equals("新加卷", StringComparison.Ordinal)
            || text.Equals("New Volume", StringComparison.OrdinalIgnoreCase))
            text = "";
        if (string.IsNullOrWhiteSpace(text))
            text = removable
                ? LocalizationManager.Text("U盘", "USB drive")
                : LocalizationManager.Text("移动硬盘", "USB storage");
        return $"{text} ({letter})";
    }

    internal static UsbDeviceCategory Classify(string name, string service, string pnpClass)
    {
        string n = name ?? "", s = service ?? "", c = pnpClass ?? "";
        if (s.Contains("stor", StringComparison.OrdinalIgnoreCase)
            || c.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)
            || c.Equals("Volume", StringComparison.OrdinalIgnoreCase)
            || n.Contains("mass storage", StringComparison.OrdinalIgnoreCase)
            || n.Contains("大容量存储", StringComparison.Ordinal)
            || n.Contains("移动硬盘", StringComparison.Ordinal)
            || n.Contains("flash disk", StringComparison.OrdinalIgnoreCase))
            return UsbDeviceCategory.Storage;
        return UsbDeviceCategory.Peripheral;
    }

    internal static UsbPnpInfo? SelectBest(IEnumerable<UsbPnpInfo> items)
        => items.OrderByDescending(Score).FirstOrDefault();

    private static int Score(UsbPnpInfo device)
    {
        string name = device.Name ?? "";
        int score = Math.Min(name.Length, 40);
        if (name.Length == 0) score -= 100;
        if (name.Contains("composite", StringComparison.OrdinalIgnoreCase)
            || name.Contains("复合", StringComparison.Ordinal))
            score -= 30;
        if (name.Contains("mass storage", StringComparison.OrdinalIgnoreCase)
            || name.Contains("大容量存储", StringComparison.Ordinal))
            score -= 20;
        if (name.Contains("hub", StringComparison.OrdinalIgnoreCase)
            || name.Contains("集线器", StringComparison.Ordinal))
            score -= 25;
        return score;
    }

    private static string EscapeWql(string value) => value.Replace("'", "''");

    private static string ReadString(ManagementBaseObject instance, string property)
        => Convert.ToString(instance[property], CultureInfo.InvariantCulture)?.Trim() ?? "";

    private static int ReadInt(ManagementBaseObject instance, string property)
    {
        object? value = instance[property];
        if (value is null) return -1;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return -1; }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint deviceInstance, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint deviceInstance, StringBuilder buffer, uint bufferLength, uint flags);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _debounceTimer.Dispose();
    }
}
