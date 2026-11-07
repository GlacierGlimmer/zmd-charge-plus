using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Settings;

internal static class LayerPromptChecks
{
    internal static async Task Run(SettingsWindow owner,Action<bool,string> check,string output)
    {
        Directory.CreateDirectory(output);string language=LocalizationManager.Preference;
        LocalizationManager.SetPreference("zh-CN");typeof(SettingsWindow).GetMethod("RebuildLocalizedChoiceItems",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,null);owner.NavigateToSettingsSection("display");await Task.Delay(80);
        var combo=owner.FindControl<ComboBox>("PersistentLayerCombo")!;int original=combo.SelectedIndex;
        try
        {
            check(new AppSettings().PersistentLayer==PersistentHudLayer.Desktop && (int)PersistentHudLayer.Topmost==0 && (int)PersistentHudLayer.Desktop==1,"old defaults and persisted enum numbers are unchanged");
            check(combo.Items.Select(i=>i?.ToString()).SequenceEqual(new[]{"桌面层（不置顶）","始终置顶","超级置顶（实验性）"}),"three layer choices have the requested order: "+string.Join(" | ",combo.Items));
            check(combo.MinWidth>=240,"layer selector has room for experimental caption");
            var collect=typeof(SettingsWindow).GetMethod("CollectEditorSettings",BindingFlags.NonPublic|BindingFlags.Instance)!;
            foreach(var choice in new[]{(0,PersistentHudLayer.Desktop),(1,PersistentHudLayer.Topmost),(2,PersistentHudLayer.SuperTopmost)})
            {combo.SelectedIndex=choice.Item1;check(((AppSettings)collect.Invoke(owner,null)!).PersistentLayer==choice.Item2,"layer selection maps to the correct saved mode "+choice.Item2);}
            check(owner.FindControl<TextBlock>("LayerDescription")!.Text!.Contains("不抢焦点"),"enhanced layer has its behavior and system limits explained");
            combo.IsDropDownOpen=true;await Task.Delay(90);
            var selected=(ComboBoxItem)combo.ContainerFromIndex(2)!;
            var presenter=selected.GetVisualDescendants().OfType<ContentPresenter>().First(p=>p.Name=="PART_ContentPresenter");
            check((presenter.Background as ISolidColorBrush)?.Color==Color.Parse("#343400"),"selected dropdown uses a dark surface instead of bright yellow");
            check((presenter.Foreground as ISolidColorBrush)?.Color==Color.Parse("#F2F2F2"),"selected dropdown text has a readable light foreground");
            combo.IsDropDownOpen=false;
            await InteractionPerformanceChecks.Run(owner,check);
            var list=new ListBox{Width=250,Height=110,ItemsSource=new[]{"First profile","Second profile"},SelectedIndex=0};
            var sample=new Window{Width=290,Height=150,Content=list};sample.Show(owner);await Task.Delay(90);
            try
            {
                var item=(ListBoxItem)list.ContainerFromIndex(0)!;
                foreach(var hover in new[]{false,true,false})
                {
                    ((IPseudoClasses)item.Classes).Set(":pointerover",hover);await Task.Delay(30);
                    var part=item.GetVisualDescendants().OfType<ContentPresenter>().First(p=>p.Name=="PART_ContentPresenter");
                    check((part.Background as ISolidColorBrush)?.Color==Color.Parse(hover?"#454600":"#343400"),"selected list surface remains dark through hover transitions");
                }
            }finally{sample.Close();}
            var dialog=SaveErrorDialog.Create(new IOException("Isolated drag test"));dialog.Show(owner);await Task.Delay(100);
            try
            {
                check(!dialog.Topmost,"error prompt does not stay above other applications");
                var title=dialog.GetLogicalDescendants().OfType<Border>().Single(b=>b.Name=="PromptDragHandle");
                var point=title.TranslatePoint(new Point(8,8),dialog)!.Value;var before=dialog.Position;
                if(OperatingSystem.IsWindows())
                {
                    var screen=dialog.PointToScreen(point);var packed=new IntPtr((screen.X&65535)|((screen.Y&65535)<<16));
                    check(SendMessage(dialog.TryGetPlatformHandle()!.Handle,0x84,IntPtr.Zero,packed).ToInt64()==2,"prompt header is a real native HTCAPTION drag area");
                }
                else if(OperatingSystem.IsMacOS())check(dialog.SystemDecorations==SystemDecorations.Full && dialog.ExtendClientAreaToDecorationsHint,"prompt retains Cocoa native window movement with hidden chrome");
                else check(title.MinHeight>=36,"native move handle spans a practical title-bar height");
                dialog.Position=new PixelPoint(before.X+42,before.Y+30);await Task.Delay(70);
                check(dialog.Position.X>before.X+20 && dialog.Position.Y>before.Y+15,"prompt is a separately positioned native window");
                using var image=new RenderTargetBitmap(PixelSize.FromSize(dialog.ClientSize,dialog.RenderScaling));image.Render(dialog);image.Save(Path.Combine(output,"draggable-prompt.png"));
            }finally{dialog.Close();}
            var settings=new AppSettings();var languages=new LanguageSelectionWindow(()=>settings,_=>{},(_,_)=>Task.CompletedTask);languages.Show(owner);await Task.Delay(100);
            try
            {
                var done=languages.GetLogicalDescendants().OfType<Button>().Single(x=>x.Name=="LanguageDoneButton");
                var doneOrigin=done.TranslatePoint(default,languages)!.Value;
                check(doneOrigin.Y>=0 && doneOrigin.Y+done.Bounds.Height<=languages.ClientSize.Height,"language footer remains visible inside the window after adding its draggable title");
                check(!languages.Topmost && languages.GetLogicalDescendants().OfType<Border>().Any(b=>b.Name=="PromptDragHandle"),"language chooser uses the current draggable frame without a native colored title bar");
                var languageList=languages.GetLogicalDescendants().OfType<ListBox>().Single(x=>x.Name=="LanguageList");
                var item=(ListBoxItem)languageList.ContainerFromIndex(languageList.SelectedIndex)!;
                var part=item.GetVisualDescendants().OfType<ContentPresenter>().First(p=>p.Name=="PART_ContentPresenter");
                check((part.Background as ISolidColorBrush)?.Color==Color.Parse("#343400"),"language selection uses the same dark selected surface");
                using var image=new RenderTargetBitmap(PixelSize.FromSize(languages.ClientSize,languages.RenderScaling));image.Render(languages);image.Save(Path.Combine(output,"language-contrast.png"));
            }finally{languages.Close();}
        }
        finally{combo.SelectedIndex=original;LocalizationManager.SetPreference(language);}
    }
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
