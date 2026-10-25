$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/FusionNet.Windows/Monitoring/InventoryQueries.ps1'))
$match=[regex]::Match($source,"Add-Type -TypeDefinition @'\r?\n(?<body>[\s\S]*?)\r?\n'@")
if(!$match.Success){throw 'Missing embedded File History contract'}
# Compile declarations only: do not invoke Read(), COM creation, service or backup APIs.
$code=$match.Groups['body'].Value.Replace('FnpFileHistory','FnpFileHistoryContract')
Add-Type -TypeDefinition $code
$config=[FnpFileHistoryContract].GetNestedType('Config',[Reflection.BindingFlags]::NonPublic)
$methods=@($config.GetMethods() | Sort-Object MetadataToken | ForEach-Object Name)
$expected=@('LoadConfiguration','CreateDefaultConfiguration','SaveConfiguration','AddRemoveExcludeRule','GetIncludeExcludeRules','GetLocalPolicy','SetLocalPolicy','GetBackupStatus','SetBackupStatus','GetDefaultTarget','ValidateTarget','ProvisionAndSetNewTarget','ChangeDefaultTargetRecommendation','QueryProtectionStatus')
if(($methods -join ',') -ne ($expected -join ',')){throw 'File History SDK vtable order mismatch'}
if($config.GUID.ToString() -ne '6a5fea5b-bf8f-4ee5-b8c3-44d8a0d7331c'){throw 'Wrong configuration IID'}
foreach($name in @('CreateDefaultConfiguration','SaveConfiguration','AddRemoveExcludeRule','SetLocalPolicy','SetBackupStatus','ValidateTarget','ProvisionAndSetNewTarget','ChangeDefaultTargetRecommendation')) {
    if($code -match ('\.'+$name+'\s*\(')){throw ('Mutator invoked: '+$name)}
}
'File History contract: SDK vtable/IID and read-only call set verified; compiled only, no COM or OS queries.'
