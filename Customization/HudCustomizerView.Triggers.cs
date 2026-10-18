using Avalonia.Controls;

namespace EndfieldChargePlus.Customization;

public partial class HudCustomizerView
{
    private List<HudTriggerRule> _triggerRules=new();
    private int _triggerIndex=-1;
    private bool _loadingTriggers;
    private sealed record TriggerChoice(string Id,string Label) { public override string ToString()=>Label; }

    private void InitializeTriggerEditor()
    {
        TriggerList.SelectionChanged+=(_,_)=>
        {
            if(_loadingTriggers)return;
            int selected=TriggerList.SelectedIndex;CommitTriggerEditor();_triggerIndex=selected;ShowTriggerEditor();
        };
        TriggerModeCombo.SelectionChanged+=(_,_)=>UpdateTriggerModeHint();
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
    }
    private void UpdateTriggerModeHint()
    {
        bool numeric=TriggerModeCombo.SelectedIndex==0;TriggerOperatorCombo.IsEnabled=numeric;TriggerValueBox.IsEnabled=numeric;
        TriggerModeHint.Text=TriggerModeCombo.SelectedIndex switch {
            1=>LocalizationManager.Text("取值改变时触发；不比较运算符和阈值。没有数据或恢复后的第一次采样不触发。","Fires when the value changes. Operator and threshold are ignored. Missing data and the first recovered sample do not trigger."),
            2=>LocalizationManager.Text("布尔值由假变真时触发一次；支持 true/false 和数值 0/非 0。","Fires on a false-to-true edge. Accepts true/false and numeric zero/nonzero."),
            _=>LocalizationManager.Text("满足比较条件时触发；条件持续成立时，经过冷却后可再次触发。","Fires when the comparison matches; a sustained condition can trigger again after cooldown.") };
    }
}
