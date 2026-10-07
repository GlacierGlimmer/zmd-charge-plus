namespace EndfieldChargePlus.Customization;

public static class WindowsCpuFrequency
{
    // ProcessorFrequency is the counter's nominal baseline. WMI MaxClockSpeed can
    // describe a different limit and must not be multiplied by this ratio.
    public static double EffectiveGhz(double nominalMhz, double performancePercent)
    {
        if (!double.IsFinite(nominalMhz) || nominalMhz <= 0) return double.NaN;
        double ratio = double.IsFinite(performancePercent) && performancePercent > 0
            ? performancePercent / 100 : 1;
        return nominalMhz * ratio / 1000;
    }
}
