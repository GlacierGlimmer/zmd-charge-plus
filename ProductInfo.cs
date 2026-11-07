namespace EndfieldChargePlus;

internal static class ProductInfo
{
    internal const string BuildDate = "2026.11.07";
    internal const string LinuxName = "Endfield Charge Plus For Linux";
    internal const string WindowsName = "Endfield Charge Plus";
    // Product name comes from the shared language resource (windows.title / linux.title).
    internal static string Name
    {
        get
        {
            try
            {
                var localized=LocalizationManager.PlatformTitle;
                return string.IsNullOrWhiteSpace(localized) ? (OperatingSystem.IsLinux() ? LinuxName : WindowsName) : localized;
            }
            catch
            {
                return OperatingSystem.IsLinux() ? LinuxName : WindowsName;
            }
        }
    }
    internal static string Brand(string value)
    {
        if (!OperatingSystem.IsLinux()) return value;
        return value.Replace(LinuxName, "Endfield Charge Plus", StringComparison.Ordinal)
            .Replace("Endfield Charge Plus", LinuxName, StringComparison.Ordinal)
            .Replace("ENDFIELD CHARGE PLUS FOR LINUX", "ENDFIELD CHARGE PLUS", StringComparison.Ordinal)
            .Replace("ENDFIELD CHARGE PLUS", "ENDFIELD CHARGE PLUS FOR LINUX", StringComparison.Ordinal);
    }
}
