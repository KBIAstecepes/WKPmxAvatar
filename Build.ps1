param([ValidateSet('Development','PublicRelease')][string]$Flavor='PublicRelease')
$ErrorActionPreference='Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
  if (!(Test-Path -LiteralPath 'local/Build.Local.props' -PathType Leaf)) { throw 'Create local/Build.Local.props from config/Build.Local.props.example first.' }
  $inputs=@('PhysicsProfile.json','AuthoredGrips.json','AuthoredAssemblyGrips.json')
  if ($Flavor -eq 'Development') { $inputs += 'GazeProfile.json' }
  foreach ($name in $inputs) { if (!(Test-Path -LiteralPath (Join-Path 'profiles' $name) -PathType Leaf)) { throw ('Missing private input: profiles/'+$name) } }
  & dotnet build WKPmxAvatar.csproj -c Release --nologo ('-p:BuildFlavor='+$Flavor)
  if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }
