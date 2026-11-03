using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class ProfileScrollChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(2));Exception? failure=null;bool done=false;
        Dispatcher.UIThread.Post(async()=>{try{await Exercise(check);done=true;}catch(Exception ex){failure=ex;}finally{stop.Cancel();}});
        Dispatcher.UIThread.MainLoop(stop.Token);
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(!done)throw new TimeoutException("Profile scrolling checks timed out.");
    }
    private static async Task Exercise(Action<bool,string> check)
    {
        string output=Environment.GetEnvironmentVariable("ECP_PROFILE_SCROLL_OUTPUT") ?? Path.Combine(Path.GetTempPath(),"ecp-profile-scroll");Directory.CreateDirectory(output);
        LocalizationManager.Initialize("zh-CN");
        var defaults=CustomHudSettings.CreateDefault();
        var settings=new AppSettings{HudEnabled=false,UiLanguage="zh-CN",CustomHud=defaults with{AutoCycle=false,CycleProfileIds=defaults.Profiles.Take(12).Select(p=>p.Id).ToList()}};
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var window=new SettingsWindow(settings,hud,runtime);window.PersistLanguagePreference=_=>{};window.Show();window.NavigateToSettingsSection("hud");
        var samples=new List<object>();var requests=new List<string>();
        try
        {
            await Task.Delay(500);
            var editor=window.FindControl<HudCustomizerView>("Customizer")!;
            var add=editor.FindControl<Button>("AddProfileBtn")!;
            var workspace=editor.FindControl<Grid>("ProfileWorkspaceColumns")!;
            var scroll=workspace.Children.OfType<ScrollViewer>().Single();
            scroll.AddHandler(Control.RequestBringIntoViewEvent,(_,e)=>requests.Add((e.Source?.GetType().Name ?? "?")+" "+(e.Source is ContentControl c ? c.Content?.ToString() : (e.Source as Control)?.Name)),RoutingStrategies.Bubble,true);
            int step=0;
            async Task Click(string name,double offset=0,Action? edit=null)
            {
                scroll.Offset=new Vector(0,offset);await Task.Delay(100);edit?.Invoke();
                var button=editor.FindControl<Button>(name)!;button.Focus(NavigationMethod.Pointer);await Task.Delay(80);requests.Clear();
                var viewers=button.GetVisualAncestors().OfType<ScrollViewer>().Append(scroll).Distinct().ToArray();
                // Establish a reachable pointer-click fixture; Focus may temporarily
                // scroll horizontally by the width of an overlay scrollbar on macOS.
                foreach(var viewer in viewers)viewer.Offset=new Vector(0,viewer.Offset.Y);
                await Task.Delay(120);
                var before=scroll.Offset;var positions=viewers.Select(s=>s.Offset).ToArray();
                var point=button.TranslatePoint(default,window)!.Value;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
                var after=scroll.Offset;samples.Add(new{name,offset,before=before.ToString(),after=after.ToString(),viewers=viewers.Select((s,i)=>new{before=positions[i].ToString(),after=s.Offset.ToString()}).ToArray(),requests=requests.ToArray()});
                File.WriteAllText(Path.Combine(output,"scroll.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
                using(var bmp=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height))){bmp.Render(window);bmp.Save(Path.Combine(output,$"{step++:D2}-{name}.png"));}
                check(Math.Abs(before.Y-after.Y)<1,$"{name} keeps the profile editor in place: {before} -> {after}; requests: {string.Join("; ",requests)}");
                check(viewers.Select((s,i)=>(s.Offset-positions[i]).Length<1).All(v=>v),name+" also preserves outer UI-scale scrolling: "+string.Join("; ",viewers.Select((s,i)=>positions[i]+" -> "+s.Offset)));
                var afterPoint=button.TranslatePoint(default,window)!.Value;
                check(Math.Abs(afterPoint.X-point.X)<1 && Math.Abs(afterPoint.Y-point.Y)<1,name+" stays at the same visible position.");
            }
            foreach(var scales in new[]{(1d,1d),(1.25,1.5)})
            {
                GlobalAppearance.Apply(window,scales.Item1,scales.Item2);await Task.Delay(180);
                foreach(double offset in new[]{0d,80d})
                {
                    int original=editor.FindControl<ComboBox>("ProfileCombo")!.Items.Count;
                    await Click("AddProfileBtn",offset);
                    check(editor.FindControl<ComboBox>("ProfileCombo")!.Items.Count==original+1,"Create still adds and selects the new profile.");
                    await Click("SaveProfileBtn",offset,()=>editor.FindControl<TextBox>("ProfileNameBox")!.Text="Scroll audit custom");
                    check(editor.FindControl<ComboBox>("ProfileCombo")!.SelectedItem?.ToString()=="Scroll audit custom","Save still updates the profile and selector.");
                    await Click("SaveProfileBtn",offset); // No changes to save.
                    await Click("DeleteProfileBtn",offset);
                    check(editor.FindControl<ComboBox>("ProfileCombo")!.Items.Count==original,"Delete still removes the selected custom profile.");
                }
            }
            int builtInCount=editor.FindControl<ComboBox>("ProfileCombo")!.Items.Count;
            await Click("SaveProfileBtn",edit:()=>editor.FindControl<TextBox>("TitleBox")!.Text="Scroll audit built-in copy");
            check(editor.FindControl<ComboBox>("ProfileCombo")!.Items.Count==builtInCount+1,"Saving edits to a built-in profile still creates a copy.");
            await Click("DeleteProfileBtn");
            window.NavigateToSettingsSection("cycle");await Task.Delay(150);
            var cycle=editor.FindControl<ToggleSwitch>("AutoCycleSwitch")!;
            var cycleScroll=cycle.GetVisualAncestors().OfType<ScrollViewer>().First();
            cycle.BringIntoView();cycle.Focus();await Task.Delay(150);var beforeToggle=cycleScroll.Offset;
            cycle.IsChecked=cycle.IsChecked!=true;await Task.Delay(250);
            check((cycleScroll.Offset-beforeToggle).Length<1,"Toggling auto cycle also keeps the surrounding page in place.");
            GlobalAppearance.Apply(window,1,1);await Task.Delay(180);
            var queue=editor.FindControl<ListBox>("CycleProfileList")!;
            // Force overflow independently of platform fonts and display size.
            queue.MinHeight=0;queue.Height=160;queue.MaxHeight=160;await Task.Delay(180);
            // Navigate into the visible queue controls before keyboard selection;
            // virtualized rows outside the effective viewport are not realized.
            var queueAction=editor.FindControl<Button>("CycleMoveDownBtn")!;
            queueAction.BringIntoView();queueAction.Focus();await Task.Delay(200);
            queue.Focus();queue.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.End});await Task.Delay(350);
            check(queue.SelectedIndex==queue.Items.Count-1,"End-key navigation still selects the last queue item.");
            var inner=queue.GetVisualDescendants().OfType<ScrollViewer>().First();
            check(inner.Offset.Y>0,"Keyboard/focus selection can still reveal the last queue item inside the list.");
            var item=queue.ContainerFromIndex(queue.SelectedIndex)!;var position=item.TranslatePoint(default,inner);
            File.WriteAllText(Path.Combine(output,"navigation.json"),JsonSerializer.Serialize(new{queue.SelectedIndex,queue.IsKeyboardFocusWithin,offset=inner.Offset.ToString(),viewport=inner.Viewport.ToString(),bounds=inner.Bounds.ToString(),position=position?.ToString(),item=item.Bounds.ToString()},new JsonSerializerOptions{WriteIndented=true}));
            using(var bmp=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height))){bmp.Render(window);bmp.Save(Path.Combine(output,"navigation.png"));}
            check(position.HasValue,"The selected queue row is realized in the visible editor.");
            check(position!.Value.Y>=-1 && position.Value.Y+item.Bounds.Height<=inner.Viewport.Height+1,"Active queue navigation still reveals the selected row inside its own scroller.");
            editor.Load(settings.CustomHud with{CycleProfileIds=new(),AutoCycle=false},hud);await Task.Delay(150);
            check(queue.Items.Count==0,"An empty cycle queue is retained for the second regression scenario.");
            window.NavigateToSettingsSection("hud");await Task.Delay(120);
            await Click("AddProfileBtn");await Click("SaveProfileBtn",edit:()=>editor.FindControl<TextBox>("ProfileNameBox")!.Text="Empty queue audit");await Click("DeleteProfileBtn");
        }
        finally{window.Close();hud.Close();LocalizationManager.Initialize("zh-CN");}
    }
}
