using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace EndfieldChargePlus.Customization;

public partial class HudCustomizerView
{
    private List<HudTriggerRule> _triggerRules=new();
    private int _triggerIndex=-1;
    private bool _loadingTriggers;
    private int _triggerCheckVersion;
    private CancellationTokenSource? _triggerCheckCts;
    private string? _triggerValidationIdentity;
    private sealed record TriggerChoice(string Id,string Label) { public override string ToString()=>Label; }

    private void InitializeTriggerEditor()
    {
        // Updating a rule label must not bring its list row into the outer page.
        TriggerList.AddHandler(Control.RequestBringIntoViewEvent,(_,e)=>
        {
            if(!TriggerList.IsKeyboardFocusWithin)e.Handled=true;
        },RoutingStrategies.Bubble);
        TriggerList.SelectionChanged+=(_,_)=>
        {
            if(_loadingTriggers)return;
            int selected=TriggerList.SelectedIndex;CommitTriggerEditor();_triggerIndex=selected;ShowTriggerEditor();
        };
        TriggerModeCombo.SelectionChanged+=(_,_)=>UpdateTriggerModeHint();
        TriggerVariableBox.TextChanged+=(_,_)=>UpdateTriggerValidation();
        CheckTriggerVariableBtn.Click+=async (_,_)=>await CheckTriggerVariableAsync();
        DetachedFromVisualTree+=(_,_)=>{_triggerValidationIdentity=null;_triggerCheckVersion++;_triggerCheckCts?.Cancel();};
        AddTriggerBtn.Click+=(_,_)=>
        {
            CommitTriggerEditor();if(_triggerRules.Count>=64)return;
            var rule=new HudTriggerRule { Name=LocalizationManager.Text("新规则","New rule") };
            _triggerRules.Add(rule);_triggerIndex=_triggerRules.Count-1;RefreshTriggerList();ShowTriggerEditor();
        };
        RemoveTriggerBtn.Click+=(_,_)=>
        {
            if(_triggerIndex<0 || _triggerIndex>=_triggerRules.Count)return;
            _triggerRules.RemoveAt(_triggerIndex);_triggerIndex=Math.Min(_triggerIndex,_triggerRules.Count-1);RefreshTriggerList();ShowTriggerEditor();
        };
        TriggerNameBox.LostFocus+=(_,_)=> { CommitTriggerEditor();RefreshTriggerList(); };
        TriggerVariableBox.LostFocus+=(_,_)=> { CommitTriggerEditor();RefreshTriggerList(); };
        TriggerEnabledCheck.IsCheckedChanged+=(_,_)=> { if(!_loadingTriggers){CommitTriggerEditor();RefreshTriggerList();} };
        RebuildTriggerChoices();ShowTriggerEditor();
    }
    private void LoadTriggerRules(IEnumerable<HudTriggerRule>? rules)
    {
        _triggerRules=HudTriggerRule.Normalize(rules);_triggerIndex=_triggerRules.Count>0 ? 0 : -1;
        RefreshTriggerList();ShowTriggerEditor();
    }
    private List<HudTriggerRule> ExportTriggerRules() { CommitTriggerEditor();return HudTriggerRule.Normalize(_triggerRules); }
    private void CommitTriggerEditor()
    {
        if(_loadingTriggers || _triggerIndex<0 || _triggerIndex>=_triggerRules.Count)return;
        _triggerRules[_triggerIndex]=_triggerRules[_triggerIndex] with {
            Enabled=TriggerEnabledCheck.IsChecked==true,Name=TriggerNameBox.Text?.Trim() ?? "",Variable=TriggerVariableBox.Text?.Trim() ?? "",
            Mode=TriggerModeCombo.SelectedIndex switch {1=>"Change",2=>"BecomesTrue",_=>"Threshold"},
            Operator=TriggerOperatorCombo.SelectedItem?.ToString() ?? ">=",Value=(double)(TriggerValueBox.Value ?? 90),CooldownSeconds=(int)(TriggerCooldownBox.Value ?? 30) };
        UpdateTriggerValidation();
    }
    private void RefreshTriggerList()
    {
        bool previous=_loadingTriggers;_loadingTriggers=true;
        TriggerList.ItemsSource=_triggerRules.Select(r=>new TriggerChoice(r.Id,$"{(r.Enabled ? "☑" : "☐")} {r.Name} · {r.Variable}")).ToList();
        TriggerList.SelectedIndex=_triggerIndex;RemoveTriggerBtn.IsEnabled=_triggerIndex>=0;AddTriggerBtn.IsEnabled=_triggerRules.Count<64;
        _loadingTriggers=previous;
    }
    private void ShowTriggerEditor()
    {
        bool previous=_loadingTriggers;_loadingTriggers=true;
        TriggerEditorPanel.IsEnabled=_triggerIndex>=0 && _triggerIndex<_triggerRules.Count;
        if(TriggerEditorPanel.IsEnabled)
        {
            var r=_triggerRules[_triggerIndex];TriggerEnabledCheck.IsChecked=r.Enabled;TriggerNameBox.Text=r.Name;TriggerVariableBox.Text=r.Variable;
            TriggerModeCombo.SelectedIndex=r.Mode switch {"Change"=>1,"BecomesTrue"=>2,_=>0};TriggerOperatorCombo.SelectedItem=r.Operator;
            TriggerValueBox.Value=(decimal)Math.Clamp(r.Value,-1e12,1e12);TriggerCooldownBox.Value=r.CooldownSeconds;
        }
        _loadingTriggers=previous;UpdateTriggerModeHint();
    }
    private void RebuildTriggerChoices()
    {
        CommitTriggerEditor();bool previous=_loadingTriggers;_loadingTriggers=true;
        int mode=TriggerModeCombo.SelectedIndex;string op=TriggerOperatorCombo.SelectedItem?.ToString() ?? ">=";
        TriggerModeCombo.Items.Clear();
        foreach(var text in new[] {LocalizationManager.Text("阈值比较","Threshold"),LocalizationManager.Text("取值变化","Value changes"),LocalizationManager.Text("由假变真","Becomes true")}) TriggerModeCombo.Items.Add(text);
        TriggerModeCombo.SelectedIndex=Math.Max(0,mode);
        TriggerOperatorCombo.Items.Clear();foreach(var value in new[] {">=",">","<=","<","==","!="})TriggerOperatorCombo.Items.Add(value);
        TriggerOperatorCombo.SelectedItem=op;_loadingTriggers=previous;UpdateTriggerModeHint();RefreshTriggerList();
        CheckTriggerVariableBtn.Content=LocalizationManager.Text("刷新","Refresh");
    }
    private void UpdateTriggerModeHint()
    {
        bool numeric=TriggerModeCombo.SelectedIndex==0;TriggerOperatorCombo.IsEnabled=numeric;TriggerValueBox.IsEnabled=numeric;
        TriggerModeHint.Text=TriggerModeCombo.SelectedIndex switch {
            1=>LocalizationManager.Text("取值改变时触发；不比较运算符和阈值。没有数据或恢复后的第一次采样不触发。","Fires when the value changes. Operator and threshold are ignored. Missing data and the first recovered sample do not trigger."),
            2=>LocalizationManager.Text("布尔值由假变真时触发一次；支持 true/false 和数值 0/非 0。","Fires on a false-to-true edge. Accepts true/false and numeric zero/nonzero."),
            _=>LocalizationManager.Text("满足比较条件时触发；条件持续成立时，经过冷却后可再次触发。","Fires when the comparison matches; a sustained condition can trigger again after cooldown.") };
        UpdateTriggerValidation();
    }

    private void SetTriggerValidation(string text,bool warning)
    {
        TriggerValidationText.Text=text;TriggerValidationText.IsVisible=text.Length>0;
        TriggerValidationText.Foreground=warning ? Brushes.Orange : Brushes.LightGreen;
    }
    private void UpdateTriggerValidation()
    {
        if(_loadingTriggers)return;
        string identity=$"{_triggerIndex}|{TriggerVariableBox.Text}|{TriggerModeCombo.SelectedIndex}|{LocalizationManager.CurrentLanguage.Code}|{_httpSources.GetHashCode()}";
        if(identity==_triggerValidationIdentity)return;
        _triggerValidationIdentity=identity;
        _triggerCheckVersion++;_triggerCheckCts?.Cancel();
        bool selected=_triggerIndex>=0 && _triggerIndex<_triggerRules.Count;
        CheckTriggerVariableBtn.IsEnabled=selected;
        if(!selected){SetTriggerValidation("",false);return;}
        string key=TriggerVariableBox.Text?.Trim() ?? "";
        var definition=VariableCatalog.Find(key);
        bool custom=_httpSources.Any(s=>new[]{"error","status"}.Concat(s.Fields.Select(f=>Sanitize(f.Variable)))
            .Any(field=>string.Equals(key,$"custom.{Sanitize(s.Name)}.{field}",StringComparison.OrdinalIgnoreCase)));
        if(definition is null && !custom)
        {SetTriggerValidation(LocalizationManager.Text("未找到匹配变量。","No matching variable.")+" "+key,true);return;}
        if(definition is null){SetTriggerValidation("",false);return;}
        bool text=definition.ValueType.Contains("文本",StringComparison.Ordinal);
        bool boolean=definition.ValueType.Contains("布尔",StringComparison.Ordinal);
        if(TriggerModeCombo.SelectedIndex==0 && (text || boolean))
            SetTriggerValidation(NotNumeric(key),true);
        else if(TriggerModeCombo.SelectedIndex==2 && text)
            SetTriggerValidation(ExpectedBoolean(),true);
        else SetTriggerValidation(LocalizationManager.Text("类型","Type")+": "+VariableLocalization.Localize(definition).ValueType,false);
    }
    private static string NotNumeric(string key)=>LocalizationManager.Formatted($"'{key}' 不是数值",(FormattableString)$"'{key}' is not numeric");
    private static string ExpectedBoolean()=>LocalizationManager.Text("类型","Type")+": "+LocalizationManager.Text("文本","Text")+" → "+LocalizationManager.Text("布尔值","Boolean")+" / "+LocalizationManager.Text("数值","Number");
    private async Task CheckTriggerVariableAsync()
    {
        if(_triggerIndex<0 || _triggerIndex>=_triggerRules.Count)return;
        CustomHudSettings settings;
        try
        {
            if(!TryParseHttpJson(showErrors:true)){SetTriggerValidation(HttpStatusText.Text ?? LocalizationManager.Text("不可用","Unavailable"),true);return;}
            CommitTriggerEditor();
            // A read check must not save profiles or write a new credential to
            // Keychain/the encryption store. Use an ephemeral profile snapshot.
            var profiles=_profiles.Select(CloneProfile).ToList();
            if(_selectedIndex>=0 && _selectedIndex<profiles.Count)profiles[_selectedIndex]=BuildEditedProfile(profiles[_selectedIndex]);
            settings=_loaded with {Profiles=profiles,HttpSources=_httpSources.Select(CloneHttp).ToList(),
                CycleProfileIds=GetSanitizedCycleProfileIds(),
                ActiveProfileId=_selectedIndex>=0 && _selectedIndex<profiles.Count ? profiles[_selectedIndex].Id : _loaded.ActiveProfileId};
        }
        catch{SetTriggerValidation(LocalizationManager.Text("不可用","Unavailable"),true);return;}
        var rule=_triggerRules[_triggerIndex];int version=_triggerCheckVersion;
        if(rule.Variable.StartsWith("deepseek.",StringComparison.OrdinalIgnoreCase) && !rule.Variable.StartsWith("deepseek.period.",StringComparison.OrdinalIgnoreCase)
            && (DeepSeekKeyBox.Text ?? "").Trim()!=SecretStore.Unprotect(_loaded.DeepSeekApiKeyProtected).Trim())
        {SetTriggerValidation("DeepSeek API: "+LocalizationManager.Text("保存并应用","Save & Apply"),true);return;}
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(10));_triggerCheckCts=cts;
        CheckTriggerVariableBtn.IsEnabled=false;
        SetTriggerValidation(LocalizationManager.Text("检查中…","Checking…"),false);
        try
        {
            using var variables=new VariableHub();
            var active=settings.Profiles.FirstOrDefault(p=>string.Equals(p.Id,settings.ActiveProfileId,StringComparison.OrdinalIgnoreCase));
            var probe=(settings.CycleProfileIds ?? new List<string>()).Select(id=>settings.Profiles.FirstOrDefault(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase))).FirstOrDefault(p=>p?.BuiltInKey=="network.ping") ?? active;
            var values=await variables.SnapshotAsync(settings,new[]{rule.Variable},active?.GpuAdapterId,probe?.PingTarget,probe?.ProbeProtocol,probe?.ProbePort ?? 443,cts.Token);
            if(version!=_triggerCheckVersion)return;
            values.TryGetValue(rule.Variable,out var value);
            if(!HudTriggerEvaluator.Valid(value))SetTriggerValidation(LocalizationManager.Text("不可用","Unavailable")+": "+rule.Variable,true);
            else if(!HudTriggerEvaluator.IsUsable(rule,value))SetTriggerValidation(rule.Mode=="BecomesTrue" ? ExpectedBoolean() : NotNumeric(rule.Variable),true);
            else
            {
                string type=value is bool ? LocalizationManager.Text("布尔值","Boolean")
                    : value is string ? LocalizationManager.Text("文本","Text") : LocalizationManager.Text("数值","Number");
                SetTriggerValidation(LocalizationManager.Text("类型","Type")+": "+type,false);
            }
        }
        catch{if(version==_triggerCheckVersion)SetTriggerValidation(LocalizationManager.Text("不可用","Unavailable"),true);}
        finally
        {
            if(ReferenceEquals(_triggerCheckCts,cts))_triggerCheckCts=null;
            if(version==_triggerCheckVersion)CheckTriggerVariableBtn.IsEnabled=true;
        }
    }
}
