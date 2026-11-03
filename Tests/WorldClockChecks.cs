using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;

internal static class WorldClockChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var settings=CustomHudSettings.CreateDefault();
        var world=settings.Profiles.Single(p=>p.BuiltInKey=="time.world-clock");
        var day=settings.Profiles.Single(p=>p.BuiltInKey=="time.day-progress");
        var instant=DateTimeOffset.Parse("2026-01-01T02:30:00Z");
        var empty=new Dictionary<string,object?>();
        var previous=LocalizationManager.Current;
        LocalizationManager.SetLanguage(AppLanguage.English);
        try
        {
            check(settings.Profiles.Select(p=>p.BuiltInKey).SequenceEqual(BuiltInProfileOrder.Keys.Where(key=>settings.Profiles.Any(p=>p.BuiltInKey==key))),"Built-in schemes have one consistent category order.");
            check(settings.Profiles.Single(p=>p.Id==settings.ActiveProfileId).BuiltInKey=="system.memory" && !settings.CycleProfileIds!.Contains(world.Id),"Adding World Clock keeps the selected default and existing carousel intact.");
            check(WorldClockTimeZones.All.Count==39 && WorldClockTimeZones.All.Select(z=>z.Minutes).Distinct().Count()==39,"Fixed UTC zone options cover half- and quarter-hour offsets without duplicates.");
            var mountain=WorldClockTimeZones.All.Single(z=>z.Minutes==-420);
            check(mountain.Label(false).Contains("山地时间") && mountain.Label(true)=="UTC-07:00 Mountain Time (US & Canada)","UTC region labels are bilingual.");
            var west=world with { TimeUtcOffsetMinutes=-420 };
            var east=world with { TimeUtcOffsetMinutes=345 };
            var a=HudProfileRenderer.RenderAt(west,empty,instant);
            var b=HudProfileRenderer.RenderAt(east,empty,instant);
            check(a.PrimaryText=="19:30:00" && a.SecondaryText==" UTC-07:00" && a.Title=="World Clock" && Math.Abs(a.Progress-19.5/24)<1e-10,"Western World Clock text and ring use the same selected time.");
            check(b.PrimaryText=="08:15:00" && b.SecondaryText==" UTC+05:45" && Math.Abs(b.Progress-8.25/24)<1e-10,"Independent HUDs use independent UTC offsets from one shared instant.");
            var calendar=world with { TimeUtcOffsetMinutes=-720,PrimaryTemplate="{time.date} {time.year} {time.hour}" };
            // Custom templates on a built-in are canonicalized by localization. Test a genuine copy.
            calendar=calendar with { IsBuiltIn=false,BuiltInKey="" };
            check(HudProfileRenderer.RenderAt(calendar,empty,instant).PrimaryText=="2025-12-31 2025 14","UTC conversion changes date, year and calendar variables together.");
            var target=west with { TimeTargetEnabled=true,TimeTarget="20:30:00" };
            var countdown=HudProfileRenderer.RenderAt(target,empty,instant);
            check(countdown.RightText=="Left 4%" && Math.Abs(countdown.Progress-1.0/24)<1e-10,"Daily target time is interpreted in the selected UTC zone.");
            check(HudProfileRenderer.RenderAt(target,empty,instant.AddHours(1)).RightText=="Left 100%","An exact daily target rolls over to the next day.");
            check(HudProfileRenderer.RenderAt(target,empty,instant.AddHours(2)).RightText=="Left 96%","An expired daily target rolls over without a negative ring.");
            check(WorldClockTimeZones.ReadClock(west,DateTimeOffset.Parse("2026-07-01T12:00:00Z")).Offset==TimeSpan.FromHours(-7),"Fixed offsets do not silently change in summer.");
            check(WorldClockTimeZones.ReadClock(day,instant)==instant.ToLocalTime() && day.TimeUtcOffsetMinutes is null,"Day Progress retains the device's local clock.");
            check(HudProfileRenderer.GetUnavailableVariables(world,empty).Count==0 && HudProfileRenderer.GetUnavailableVariables(world with { IsBuiltIn=false,BuiltInKey="",PrimaryTemplate="{unreadable.metric}" },empty).Contains("unreadable.metric"),"Real local clock computations are valid without masking unrelated missing data.");
            var configured=settings with { ActiveProfileId=world.Id,CycleProfileIds=new() { day.Id,world.Id,settings.ActiveProfileId },Profiles=settings.Profiles.Select(p=>p.Id==world.Id?target:p).ToList() };
            var imported=HudSettingsNormalizer.Normalize(JsonSerializer.Deserialize<CustomHudSettings>(JsonSerializer.Serialize(configured))!);
            var kept=imported.Profiles.Single(p=>p.Id==world.Id);
            check(kept.TimeUtcOffsetMinutes==-420 && kept.TimeTargetEnabled && kept.TimeTarget=="20:30:00" && imported.ActiveProfileId==world.Id && imported.CycleProfileIds!.SequenceEqual(configured.CycleProfileIds!),"Time-zone, target, selection and explicit queue survive serialization and reordering.");
            check(HudSettingsNormalizer.NormalizeProfile(target with { TimeUtcOffsetMinutes=999 }).TimeUtcOffsetMinutes==0,"Malformed imported UTC offsets normalize safely.");
            check(HudSettingsNormalizer.NormalizeProfile(target with { IsBuiltIn=false,BuiltInKey="",Category="自定义" }).TimeUtcOffsetMinutes==-420,"A user copy retains its world clock configuration.");
            check(HudSettingsNormalizer.Normalize(settings with { CycleProfileIds=null }).CycleProfileIds!.All(id=>id!=world.Id),"Legacy carousel migration does not add the new world clock automatically.");
            check(HudProfileRenderer.NeedsSecondAccurateClock(world),"World Clock participates in the second-accurate scheduler.");
        }
        finally { LocalizationManager.SetLanguage(previous); }
    }

    internal static void Editor(Action<bool,string> check)
    {
        var previous=LocalizationManager.Current;
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
        var settings=CustomHudSettings.CreateDefault();
        var view=new HudCustomizerView();
        view.Load(settings,null);
        var profiles=view.FindControl<ComboBox>("ProfileCombo")!;
        var zones=view.FindControl<ComboBox>("WorldClockZoneCombo")!;
        profiles.SelectedIndex=settings.Profiles.FindIndex(p=>p.BuiltInKey=="time.world-clock");
        check(view.FindControl<StackPanel>("WorldClockOptions")!.IsVisible && view.FindControl<Border>("TimeOptionsPanel")!.IsVisible,"Selecting World Clock exposes zone and target settings.");
        zones.SelectedItem=zones.ItemsSource!.Cast<WorldClockZone>().Single(z=>z.Minutes==-420);
        view.FindControl<ToggleSwitch>("TimeTargetSwitch")!.IsChecked=true;
        view.FindControl<TextBox>("TimeTargetBox")!.Text="18:25:00";
        LocalizationManager.SetLanguage(AppLanguage.English);
        view.ApplyLocalization();
        check((zones.SelectedItem as WorldClockZone)?.Minutes==-420 && view.FindControl<TextBox>("TimeTargetBox")!.Text=="18:25:00","Changing UI language retains the unsaved zone and daily target.");
        var saved=view.ExportSettings();
        var kept=saved.Profiles.Single(p=>p.BuiltInKey=="time.world-clock");
        check(kept.TimeUtcOffsetMinutes==-420 && kept.TimeTargetEnabled && kept.TimeTarget=="18:25:00" && saved.Profiles.Count==settings.Profiles.Count,"Saving clock context updates the built-in without an unwanted duplicate.");
        view.FindControl<TextBox>("ProfileNameBox")!.Text="My World Clock";
        saved=view.ExportSettings();
        check(saved.Profiles.Count==settings.Profiles.Count+1 && saved.Profiles.Last().Name=="My World Clock" && saved.Profiles.Last().TimeUtcOffsetMinutes==-420,"Renaming still creates an editable world-clock copy with its zone.");
        profiles.SelectedIndex=settings.Profiles.FindIndex(p=>p.BuiltInKey=="time.day-progress");
        check(!view.FindControl<StackPanel>("WorldClockOptions")!.IsVisible,"Day Progress stays local and does not expose an irrelevant UTC selector.");
        view.FindControl<ToggleSwitch>("TimeTargetSwitch")!.IsChecked=true;
        view.FindControl<TextBox>("TimeTargetBox")!.Text="16:00:00";
        saved=view.ExportSettings();
        check(saved.Profiles.Single(p=>p.BuiltInKey=="time.day-progress").TimeTarget=="16:00:00" && saved.Profiles.Count==settings.Profiles.Count+1,"Existing day-progress target settings also save directly.");
        LocalizationManager.SetLanguage(previous);
    }
}
