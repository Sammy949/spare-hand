# Prints every ToggleSwitch (name + state) in a window, via UI Automation.
param([string]$Title)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)))
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
foreach ($t in $all) { "{0,-12} {1}" -f $t.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState, $t.Current.AutomationId }
