# Drives the open Hold library through UI Automation (no mouse): filter, Clear, Undo. Prints what it sees after each step.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Hold')
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { 'library window not found'; exit 1 }
function Find($name, $type) {
  $c = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)))
  $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Items { $list = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)))
  if (-not $list) { return 0 }; $list.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition).Count }
function Texts { ($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match 'item|Removed|Nothing' }) -join ' | ' }
"start: items=$(Items) :: $(Texts)"
$filter = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
$filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('sea'); Start-Sleep -Milliseconds 600
"filter 'sea': items=$(Items)"
$filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue(''); Start-Sleep -Milliseconds 600
"filter cleared: items=$(Items)"
(Find 'Clear' ([System.Windows.Automation.ControlType]::Button)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600
"after Clear: items=$(Items) :: $(Texts)"
(Find 'Undo' ([System.Windows.Automation.ControlType]::Hyperlink)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600
"after Undo: items=$(Items) :: $(Texts)"
