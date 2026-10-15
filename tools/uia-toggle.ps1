# Flips one ToggleSwitch (by AutomationId) in a window via UI Automation.
param([string]$Title, [string]$Id)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)))
$t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
$p = $t.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); $p.Toggle(); Start-Sleep -Milliseconds 500
"$Id -> $($p.Current.ToggleState)"
