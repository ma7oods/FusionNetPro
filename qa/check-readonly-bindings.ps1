param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'

# A WPF binding crashes when the target property is read-only and the source is
# TwoWay / OneWayToSource. Dependency properties that default to TwoWay are the ones
# on these controls. This script lists every read-only view-model property that is
# bound to such a property without an explicit Mode=OneWay.
$twoWayControls = @('ProgressBar', 'Slider', 'TextBox', 'CheckBox', 'ToggleButton', 'ToggleSwitch', 'ComboBox')
$twoWayProps = @('Value', 'Text', 'IsChecked', 'SelectedItem', 'SelectedValue')

# 1. read-only public properties on the view models
$readOnly = @{}
Get-ChildItem (Join-Path $Root 'src\FusionNet.App') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
        $file = $_
        Select-String -Path $file.FullName -Pattern '^\s*public\s+[\w<>\[\]\?,\.\s]+?\s+(\w+)\s*=>\s*' -AllMatches |
            ForEach-Object {
                foreach ($m in $_.Matches) {
                    $name = $m.Groups[1].Value
                    if (-not $readOnly.ContainsKey($name)) { $readOnly[$name] = @() }
                    $readOnly[$name] += $file.Name
                }
            }
    }

# 2. bindings in XAML
$problems = @()
Get-ChildItem (Join-Path $Root 'src\FusionNet.App') -Recurse -Filter '*.xaml' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
        $xaml = $_
        $lines = Get-Content $xaml.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -notmatch '\{Binding\s+(\w+)') { continue }
            $prop = $Matches[1]

            # element name for this line (strip leading whitespace, take the tag)
            $element = ''
            if ($line -match '^\s*<([A-Za-z][\w\.:]*)') { $element = $Matches[1] }
            elseif ($i -gt 0 -and $lines[$i-1] -match '<([A-Za-z][\w\.:]*)') { $element = $Matches[1] }

            $short = $element -replace '^.*:', ''
            if ($twoWayControls -notcontains $short) { continue }

            # which DP is being bound?
            $dp = ''
            if ($line -match '(Value|Text|IsChecked|SelectedItem|SelectedValue)="\{Binding') { $dp = $Matches[1] }
            if ($dp -eq '' -or $twoWayProps -notcontains $dp) { continue }

            if ($line -match 'Mode=(OneWay|OneTime)') { continue }   # explicit opt-out is fine

            if ($readOnly.ContainsKey($prop)) {
                $problems += [pscustomobject]@{
                    File   = $xaml.Name
                    Line   = ($i + 1)
                    Prop   = $prop
                    Dp     = "$element.$dp"
                    Decl   = ($readOnly[$prop] -join ',')
                    Snippet= $line.Trim()
                }
            }
        }
    }

if ($problems.Count -eq 0) {
    Write-Output "OK  no read-only view-model property is bound to a TwoWay-default dependency property."
    exit 0
}
Write-Output "PROBLEMS ($($problems.Count)):"
$problems | ForEach-Object { "  {0}:{1}  {2} -> {3}   [read-only in {4}]" -f $_.File, $_.Line, $_.Prop, $_.Dp, $_.Decl }
exit 1
