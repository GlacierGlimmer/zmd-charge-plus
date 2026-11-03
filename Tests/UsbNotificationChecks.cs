using System.Runtime.InteropServices;
using System.Text.Json;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

internal static class UsbNotificationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var defaults = new AppSettings();
        var configured = new AppSettings
        {
            UsbInsertNotificationEnabled = true,
            UsbInsertNotificationScope = "Storage",
            UsbInsertNotificationTagline = "/// CUSTOM",
            UsbInsertNotificationTitle = "{device} {action} {used}%",
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(configured))!;
        check(!defaults.UsbInsertNotificationEnabled
              && defaults.UsbInsertNotificationScope == "All"
              && restored.UsbInsertNotificationEnabled
              && restored.UsbInsertNotificationScope == "Storage"
              && restored.UsbInsertNotificationTagline == "/// CUSTOM"
              && restored.UsbInsertNotificationTitle == "{device} {action} {used}%",
            "USB notification preferences default off and round-trip.");

        check(WindowsUsbDeviceWatcher.Classify("USB 大容量存储设备", "USBSTOR", "USB") == UsbDeviceCategory.Storage
              && WindowsUsbDeviceWatcher.Classify("SanDisk Cruzer", "UASPStor", "DiskDrive") == UsbDeviceCategory.Storage
              && WindowsUsbDeviceWatcher.Classify("HID Keyboard Device", "HidUsb", "Keyboard") == UsbDeviceCategory.Peripheral
              && WindowsUsbDeviceWatcher.Classify("USB Composite Device", "usbccgp", "USB") == UsbDeviceCategory.Peripheral,
            "USB notifications distinguish storage devices from peripherals.");
        check(WindowsUsbDeviceWatcher.FormatStorageName("U盘", "F:", true) == "U盘 (F:)"
              && WindowsUsbDeviceWatcher.FormatStorageName("New Volume", "G:", false).EndsWith("(G:)"),
            "USB storage uses the volume label and drive letter, with a generic fallback.");

        var arrivalChange = new UsbDeviceChange(UsbDeviceChangeKind.Arrival, UsbDeviceCategory.Storage, "U盘 (F:)", 68, "F:");
        var arrival = CustomHudRuntime.BuildUsbNotificationData(arrivalChange, "/// CUSTOM", "{device} {used}%");
        check(arrival.Tagline == "/// CUSTOM"
              && arrival.Title.Contains("U盘 (F:)")
              && arrival.Title.Contains("68")
              && arrival.RightText == "68"
              && arrival.RightSuffix == "%"
              && Math.Abs(arrival.Progress - 0.68) < 0.001,
            "USB storage notifications show custom text, drive name and used-space percentage.");
        var removal = CustomHudRuntime.BuildUsbNotificationData(
            new UsbDeviceChange(UsbDeviceChangeKind.Removal, UsbDeviceCategory.Storage, "U盘 (F:)"));
        check(removal.Progress == 0 && removal.AccentColor == "#FFB84D" && !string.IsNullOrWhiteSpace(removal.RightText),
            "USB removal notifications use the removal presentation.");

        var emptyPnp = new Dictionary<string, UsbPnpInfo>();
        var emptyStorage = new Dictionary<string, UsbStorageVolume>();
        var insertedStorage = new Dictionary<string, UsbStorageVolume> { ["F:"] = new("F:", "U盘 (F:)", 68) };
        var storageChange = WindowsUsbDeviceWatcher.SelectChange(emptyPnp, emptyStorage, emptyPnp, insertedStorage, out bool storagePending);
        check(storageChange is { Kind: UsbDeviceChangeKind.Arrival, Category: UsbDeviceCategory.Storage, UsedPercent: 68 }
              && !storagePending,
            "Storage snapshots produce an arrival with the used-space percentage.");
        var storageGone = WindowsUsbDeviceWatcher.SelectChange(emptyPnp, insertedStorage, emptyPnp, emptyStorage, out storagePending);
        check(storageGone is { Kind: UsbDeviceChangeKind.Removal, Category: UsbDeviceCategory.Storage, Name: "U盘 (F:)" }
              && !storagePending,
            "Storage snapshots produce a removal with the cached drive name.");
        var keyboard = new UsbPnpInfo("USB Keyboard", @"USB\VID_TEST&PID_KEY", UsbDeviceCategory.Peripheral);
        var keyboardArrival = WindowsUsbDeviceWatcher.SelectChange(emptyPnp, emptyStorage,
            new Dictionary<string, UsbPnpInfo> { [keyboard.PnpDeviceId] = keyboard }, emptyStorage, out storagePending);
        check(keyboardArrival is { Kind: UsbDeviceChangeKind.Arrival, Category: UsbDeviceCategory.Peripheral, Name: "USB Keyboard" },
            "Peripheral snapshots produce a device arrival.");
        var pendingStoragePnp = new UsbPnpInfo("USB Attached SCSI (UAS) Mass Storage Device", @"USB\VID_TEST&PID_UAS", UsbDeviceCategory.Storage);
        var pendingChange = WindowsUsbDeviceWatcher.SelectChange(emptyPnp, emptyStorage,
            new Dictionary<string, UsbPnpInfo> { [pendingStoragePnp.PnpDeviceId] = pendingStoragePnp },
            emptyStorage, out storagePending);
        check(pendingChange is null && storagePending,
            "A storage PnP node without a mounted volume waits for its drive letter.");

        using (var watcher = new WindowsUsbDeviceWatcher(_ => { }))
        {
            watcher.Start();
            check(watcher.IsRunning, "USB device notifications subscribe to Windows PnP device changes.");
            watcher.Stop();
            check(!watcher.IsRunning, "USB device notifications release the PnP subscription.");
        }

        int deviceBroadcasts = 0;
        using (var monitor = new WindowsDeviceChangeMonitor(() => deviceBroadcasts++))
        {
            SendMessage(monitor.WindowHandle, 0x0219, new IntPtr(0x0007), IntPtr.Zero);
            check(deviceBroadcasts > 0, "The hidden device-change window handles WM_DEVICECHANGE.");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
