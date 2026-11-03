using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;

namespace EndfieldChargePlus.Settings;

internal static class WorkspaceTheme
{
    internal static WorkspacePreferences Current {get;private set;}=new();
    private sealed record Applied(WorkspacePreferences Preferences,Color Canvas);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,Applied> AppliedThemes=new();
    internal sealed record Palette(Color Canvas, Color Sidebar, Color Card, Color Input, Color Border, Color Text, Color Muted, Color Accent, Color OnAccent)
    {
        internal SolidColorBrush Brush(Color value) => new(value);
    }

    internal static Palette Colours(WorkspacePreferences? preferences)
    {
        // Fixed dark palette; imported legacy themes and OS appearance cannot override it.
        return new(Color.Parse("#141414"), Color.Parse("#1B1B1B"), Color.Parse("#202020"), Color.Parse("#171717"), Color.Parse("#3B3B3B"), Color.Parse("#F2F2F2"), Color.Parse("#A1A1A1"), Color.Parse("#FFFA00"), Color.Parse("#141414"));
    }

    internal static void Apply(Control root, WorkspacePreferences? preferences)
    {
        var settings = WorkspacePreferences.Normalize(preferences);
        var c = Colours(settings);
        Current=settings;
        if(AppliedThemes.TryGetValue(root,out var previous) && previous.Preferences==settings && previous.Canvas==c.Canvas)return;
        AppliedThemes.Remove(root);AppliedThemes.Add(root,new(settings,c.Canvas));
        if(Application.Current is {} app) {
            app.RequestedThemeVariant=ThemeVariant.Dark;
            foreach(var theme in app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>())
                foreach(var variant in new[]{ThemeVariant.Dark}) {
                    if(!theme.Palettes.TryGetValue(variant,out var palette))theme.Palettes[variant]=palette=new Avalonia.Themes.Fluent.ColorPaletteResources();
                    palette.Accent=c.Accent;
                }
        }
        root.Resources["EcpCanvas"] = c.Brush(c.Canvas);
        root.Resources["EcpSidebar"] = c.Brush(c.Sidebar);
        root.Resources["EcpCard"] = c.Brush(c.Card);
        root.Resources["EcpInput"] = c.Brush(c.Input);
        root.Resources["EcpBorder"] = c.Brush(c.Border);
        root.Resources["EcpText"] = c.Brush(c.Text);
        root.Resources["EcpMuted"] = c.Brush(c.Muted);
        root.Resources["EcpAccent"] = c.Brush(c.Accent);
        root.Resources["EcpOnAccent"] = c.Brush(c.OnAccent);
        root.Resources["EcpSelection"] = new SolidColorBrush(Color.FromArgb(40, c.Accent.R, c.Accent.G, c.Accent.B));
        root.Resources["EcpRadius"] = new CornerRadius(4);
        if(Application.Current is {} application)
            foreach(var key in new[]{"EcpCanvas","EcpSidebar","EcpCard","EcpInput","EcpBorder","EcpText","EcpMuted","EcpAccent","EcpOnAccent","EcpSelection","EcpRadius"})application.Resources[key]=root.Resources[key];
        if (root is Window window)
            window.RequestedThemeVariant = ThemeVariant.Dark;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> MigratedControls=new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemsControl,object> InstantLists=new();
    internal static void ApplyLegacyColours(Control root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Concat(root.GetLogicalDescendants().OfType<Control>()).Distinct())
        {
            if(control.Classes.Contains("ecp-native") || control.GetVisualAncestors().OfType<Control>().Any(c=>c.Classes.Contains("ecp-native")))continue;
            if(control is ItemsControl items && (control is ListBox or ComboBox) && !InstantLists.TryGetValue(items,out _))
            {
                InstantLists.Add(items,new object());
                items.ContainerPrepared+=(_,e)=>e.Container.Transitions=null;
            }
            if(control is ListBoxItem or ComboBoxItem)control.Transitions=null;
            // Template parts are owned by :pointerover/:pressed/:selected styles.
            // A local colour binding here would freeze whichever state was hovered.
            if(control.TemplatedParent is not null || MigratedControls.TryGetValue(control,out _))continue;
            MigratedControls.Add(control,new object());
            void Bind(AvaloniaProperty property, IBrush? brush, bool background = false)
            {
                // Never replace an explicit resource binding or a template/inherited value.
                if (!control.IsSet(property) || Avalonia.Data.BindingOperations.GetBindingExpressionBase(control,property) is not null) return;
                if (brush is not ISolidColorBrush solid) return;
                var c = solid.Color;
                if (c.A < 250 || Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 24) return;
                string? token = background ? c.R < 65 ? "EcpCard" : null : c.R > 155 ? "EcpText" : "EcpMuted";
                if (token is not null) control.Bind(property, control.GetResourceObservable(token));
            }
            if (control is TextBlock text) Bind(TextBlock.ForegroundProperty, text.Foreground);
            if (control is Border border) { Bind(Border.BackgroundProperty, border.Background, true); Bind(Border.BorderBrushProperty, border.BorderBrush, true); }
            if (control is TemplatedControl templated && control is not ListBoxItem)
            {
                Bind(TemplatedControl.BackgroundProperty, templated.Background, true);
                Bind(TemplatedControl.ForegroundProperty, templated.Foreground);
            }
        }
    }
}
