using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using MonCore.Monitoring;

namespace WinMon.Preferences;

internal sealed partial class AutoCaptureSection
{
    private static readonly CaptureRuleSetting[] TargetDefaults = CaptureRuleSetting.Defaults
        .Where(rule => rule.IsTargeted).ToArray();

    private readonly List<CaptureRuleSetting> _orderedSettings = [];
    private readonly TextBox _targetThreshold = Number(0);
    private readonly TextBox _targetDuration = Number(0);
    private int? _editingIndex;
    private Button? _editOpener;

    private sealed record TargetRow(int Index, string Name, string Summary)
    {
        public bool Enabled { get; set; }
    }

    private void InitializeTargetRules(IEnumerable<CaptureRuleSetting> rules)
    {
        _orderedSettings.AddRange(rules);
        TargetType.ItemsSource = new[] { Strings.CaptureAppTarget, Strings.CaptureWindowsService };
        TargetMetric.ItemsSource = new[] { Strings.CPU, Strings.ProcessPrivateMemoryLabel, Strings.Network };
        ThresholdHost.Content = _targetThreshold;
        DurationHost.Content = _targetDuration;
        AutomationProperties.SetLabeledBy(_targetThreshold, ThresholdLabel);
        AutomationProperties.SetLabeledBy(_targetDuration, DurationLabel);
        AutomationProperties.SetHelpText(_targetDuration, Strings.CaptureDurationHelp);
        TargetType.SelectedIndex = TargetMetric.SelectedIndex = 0;
        TargetType.SelectionChanged += TargetConditionChanged;
        TargetMetric.SelectionChanged += TargetConditionChanged;
        RefreshTargetRows();
    }

    private void RefreshTargetRows()
    {
        var rows = _orderedSettings.Select((rule, index) => (rule, index))
            // Older preferences include a disabled placeholder for each metric without a target.
            .Where(item => item.rule.IsTargeted && IsConfiguredTarget(item.rule))
            .Select(item =>
            {
                var (rule, index) = item;
                var name = string.IsNullOrWhiteSpace(rule.Target) ? Strings.CaptureTargetType : rule.Target;
                var kind = rule.IsService ? Strings.CaptureWindowsService : Strings.CaptureAppTarget;
                var metric = rule.IsNetwork ? Strings.Network : rule.IsMemory ? Strings.ProcessPrivateMemoryLabel : Strings.CPU;
                var value = (rule.Threshold / (rule.IsNetwork ? 8 : 1)).ToString("G", CultureInfo.CurrentCulture);
                var summary = Strings.Format(Strings.AutoCaptureRuleText, metric, value + " " + ThresholdSuffix(rule), rule.Seconds);
                return new TargetRow(index, name, kind + " · " + summary) { Enabled = rule.Enabled };
            }).ToArray();
        TargetRulesList.ItemsSource = rows;
        NoTargetRules.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsConfiguredTarget(CaptureRuleSetting rule) => rule.Enabled || !string.IsNullOrWhiteSpace(rule.Target);

    private CaptureRuleSetting SelectedTargetDefault => TargetDefaults[(TargetType.SelectedIndex * 3) + TargetMetric.SelectedIndex];

    private void AddRule_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        OpenEditor(((TargetRow)button.DataContext).Index, button);
    }

    private void TargetEnabled_Changed(object sender, RoutedEventArgs e)
    {
        var checkbox = (CheckBox)sender;
        var enabled = checkbox.IsChecked == true;
        if (checkbox.DataContext is not TargetRow row || row.Enabled == enabled ||
            !TargetRulesList.Items.Contains(row)) return;
        row.Enabled = enabled;
        _orderedSettings[row.Index] = _orderedSettings[row.Index] with { Enabled = row.Enabled };
    }

    private void OpenEditor(int? index, Button? opener = null)
    {
        _editingIndex = index;
        _editOpener = opener;
        var rule = index is { } existing ? _orderedSettings[existing] : null;
        TargetType.SelectedIndex = rule?.IsService == true ? 1 : 0;
        TargetMetric.SelectedIndex = rule?.IsNetwork == true ? 2 : rule?.IsMemory == true ? 1 : 0;
        TargetName.Text = rule?.Target ?? "";
        SetTargetCondition(rule ?? DefaultForNewRule());
        EditorHeading.Text = index.HasValue ? Strings.CaptureEditRule : Strings.CaptureNewRule;
        ApplyRuleButton.Content = index.HasValue ? Strings.CaptureUpdateRule : Strings.CaptureAddToList;
        RemoveRuleButton.Visibility = index.HasValue ? Visibility.Visible : Visibility.Collapsed;
        EditorError.Visibility = Visibility.Collapsed;
        AddRuleButton.IsEnabled = TargetRulesList.IsEnabled = false;
        RuleEditor.Visibility = Visibility.Visible;
        RuleEditor.BringIntoView();
        if (index.HasValue) TargetName.Focus();
        else TargetType.Focus();
    }

    private void TargetConditionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetType.SelectedIndex < 0 || TargetMetric.SelectedIndex < 0) return;
        var defaults = DefaultForNewRule();
        // Switching between executable and service keeps the value when the metric's units are the same.
        if (ReferenceEquals(sender, TargetType))
        {
            SetTargetLabels(defaults);
            return;
        }
        SetTargetCondition(defaults);
    }

    private CaptureRuleSetting DefaultForNewRule()
    {
        var fallback = SelectedTargetDefault;
        return _orderedSettings.FirstOrDefault(rule => rule.Metric == fallback.Metric && !IsConfiguredTarget(rule)) ?? fallback;
    }

    private void SetTargetCondition(CaptureRuleSetting rule)
    {
        SetTargetLabels(rule);
        SetThresholdText(_targetThreshold, rule);
        _targetDuration.Text = rule.Seconds.ToString(CultureInfo.CurrentCulture);
    }

    private void SetTargetLabels(CaptureRuleSetting rule)
    {
        TargetNameLabel.Text = rule.IsService ? Strings.CaptureServiceTarget : Strings.CaptureExecutablePath;
        TargetName.ToolTip = rule.IsService ? Strings.CaptureServiceTargetHelp : Strings.CaptureAppTargetHelp;
        AutomationProperties.SetHelpText(TargetName, (string)TargetName.ToolTip);
        ThresholdUnit.Text = ThresholdSuffix(rule);
        AutomationProperties.SetHelpText(_targetThreshold, Strings.Format(Strings.CaptureThresholdHelp, ThresholdUnit.Text));
        FeatureNote.Text = rule.IsService ? Strings.CaptureServiceTargetHelp : Strings.CaptureNetworkTargetHelp;
        FeatureNote.Visibility = rule.IsService || rule.IsNetwork ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyRule_Click(object sender, RoutedEventArgs e)
    {
        var defaults = SelectedTargetDefault;
        if (!TryReadCondition(defaults, _targetThreshold, _targetDuration, out var threshold, out var duration,
                out var error, out var invalid))
        {
            ShowEditorError(error!, invalid!);
            return;
        }

        var target = TargetName.Text.Trim();
        var enabled = _editingIndex is not { } existing || _orderedSettings[existing].Enabled;
        var rule = defaults with { Target = target, Enabled = enabled, Threshold = threshold, Seconds = duration };
        if (target.Length == 0 && enabled)
        {
            ShowEditorError(Strings.CaptureTargetRequired, TargetName);
            return;
        }
        var others = _orderedSettings.Where((item, index) => index != _editingIndex &&
            item.Metric == rule.Metric && IsConfiguredTarget(item)).ToArray();
        if (target.Length != 0 && others.Any(item => string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase)))
        {
            ShowEditorError(Strings.CaptureDuplicateScopedTarget, TargetName);
            return;
        }
        if (IsConfiguredTarget(rule) && others.Length >= AutoCapturePreferences.MaxTargetsPerMetric)
        {
            ShowEditorError(Strings.Format(Strings.CaptureScopedRuleLimit, AutoCapturePreferences.MaxTargetsPerMetric), TargetMetric);
            return;
        }

        var slot = _editingIndex ?? _orderedSettings.FindIndex(item => item.Metric == rule.Metric && !IsConfiguredTarget(item));
        if (slot < 0) _orderedSettings.Add(rule);
        else _orderedSettings[slot] = rule;
        if (IsConfiguredTarget(rule))
            _orderedSettings.RemoveAll(item => item.Metric == rule.Metric && !IsConfiguredTarget(item));
        CloseEditor(refresh: true);
    }

    private void ShowEditorError(string error, Control field)
    {
        EditorError.Text = error;
        EditorError.Visibility = Visibility.Visible;
        UIElementAutomationPeer.FromElement(EditorError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        field.Focus();
        field.BringIntoView();
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex is not { } index) return;
        _orderedSettings.RemoveAt(index);
        CloseEditor(refresh: true);
    }

    private void DiscardRule_Click(object sender, RoutedEventArgs e) => CloseEditor(refresh: false);

    private void CloseEditor(bool refresh)
    {
        _editingIndex = null;
        RuleEditor.Visibility = Visibility.Collapsed;
        AddRuleButton.IsEnabled = TargetRulesList.IsEnabled = true;
        if (refresh) RefreshTargetRows();
        (refresh ? AddRuleButton : _editOpener ?? AddRuleButton).Focus();
        _editOpener = null;
    }

    private bool TryReadTargetRules(out string? error, out Control? invalid)
    {
        if (RuleEditor.Visibility == Visibility.Visible)
        {
            CaptureTabs.SelectedItem = TargetsTab;
            error = Strings.CaptureFinishRule;
            invalid = TargetName;
            return false;
        }

        for (var index = 0; index < _orderedSettings.Count; index++)
        {
            var rule = _orderedSettings[index];
            if (!rule.IsTargeted || !rule.Enabled || !string.IsNullOrWhiteSpace(rule.Target)) continue;
            CaptureTabs.SelectedItem = TargetsTab;
            OpenEditor(index);
            error = Strings.CaptureTargetRequired;
            invalid = TargetName;
            return false;
        }

        error = null;
        invalid = null;
        return true;
    }
}
