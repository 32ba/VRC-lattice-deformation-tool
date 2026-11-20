param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$CecilPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$AllowUnityJobInitialization
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath $CecilPath
$violations = [System.Collections.Generic.List[string]]::new()
$allowedInitializers = [System.Collections.Generic.List[string]]::new()
$editorAttribute = 'UnityEditor.InitializeOnLoadMethodAttribute'
function Is-ExternalScope([string]$Name) {
    $Name.StartsWith('UnityEditor', [StringComparison]::Ordinal) -or
        $Name.StartsWith('nadena.dev.ndmf', [StringComparison]::Ordinal)
}
function Check-Attributes($Attributes, [string]$Location, [bool]$AllowGenerated = $false) {
    foreach ($attribute in $Attributes) {
        if (-not (Is-ExternalScope $attribute.AttributeType.Scope.Name)) { continue }
        if ($AllowUnityJobInitialization -and $AllowGenerated -and
            $attribute.AttributeType.FullName -eq $editorAttribute) {
            $allowedInitializers.Add($Location)
        } else { $violations.Add("Attribute on ${Location}: $($attribute.AttributeType.FullName)") }
    }
}
function Check-Type($Type) {
    Check-Attributes $Type.CustomAttributes $Type.FullName
    foreach ($method in $Type.Methods) {
        $generated = $Type.Namespace -eq '' -and $Type.Name -match '^__JobReflectionRegistrationOutput__[0-9]+$' -and
            $method.Name -eq 'EarlyInit' -and $method.IsStatic -and $method.Parameters.Count -eq 0
        Check-Attributes $method.CustomAttributes $method.FullName $generated
        Check-Attributes $method.MethodReturnType.CustomAttributes ($method.FullName + ' return')
        foreach ($parameter in $method.Parameters) {
            Check-Attributes $parameter.CustomAttributes ($method.FullName + '/' + $parameter.Name)
        }
        if ($method.HasBody) {
            foreach ($instruction in $method.Body.Instructions) {
                $operand = $instruction.Operand
                $scope = if ($operand -is [Mono.Cecil.TypeReference]) { $operand.Scope.Name }
                    elseif ($operand -is [Mono.Cecil.MemberReference]) { $operand.DeclaringType.Scope.Name } else { '' }
                if (Is-ExternalScope $scope) {
                    $violations.Add("IL in $($method.FullName): $instruction")
                }
            }
        }
    }
    foreach ($member in @($Type.Fields) + @($Type.Properties) + @($Type.Events)) {
        Check-Attributes $member.CustomAttributes $member.FullName
    }
    foreach ($nested in $Type.NestedTypes) { Check-Type $nested }
}
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path -LiteralPath $AssemblyPath).Path)
try {
    foreach ($reference in $module.AssemblyReferences) {
        if ((Is-ExternalScope $reference.Name) -and
            -not ($AllowUnityJobInitialization -and $reference.Name -eq 'UnityEditor.CoreModule')) {
            $violations.Add("Assembly reference: $($reference.Name)")
        }
    }
    $externalTypes = @($module.GetTypeReferences() | Where-Object { Is-ExternalScope $_.Scope.Name })
    foreach ($reference in $externalTypes) {
        if (-not ($AllowUnityJobInitialization -and $reference.Scope.Name -eq 'UnityEditor.CoreModule' -and
            $reference.FullName -eq $editorAttribute)) {
            $violations.Add("Type reference: $($reference.FullName)")
        }
    }
    foreach ($reference in $module.GetMemberReferences()) {
        if ((Is-ExternalScope $reference.DeclaringType.Scope.Name) -and
            -not ($AllowUnityJobInitialization -and $reference.DeclaringType.FullName -eq $editorAttribute -and
                $reference.Name -eq '.ctor')) {
            $violations.Add("Member reference: $($reference.FullName)")
        }
    }
    Check-Attributes $module.CustomAttributes 'module'
    Check-Attributes $module.Assembly.CustomAttributes 'assembly'
    foreach ($type in $module.Types) { Check-Type $type }
    if ($externalTypes.Count -gt 0 -and $allowedInitializers.Count -eq 0 -and $violations.Count -eq 0) {
        $violations.Add('Editor type reference has no permitted generated Jobs initializer.')
    }
    $report = [ordered]@{
        schemaVersion = 1
        assemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
        assemblySha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        allowUnityJobInitialization = [bool]$AllowUnityJobInitialization
        assemblyReferences = @($module.AssemblyReferences.Name)
        externalTypes = @($externalTypes | ForEach-Object { $_.FullName })
        allowedInitializers = @($allowedInitializers)
        violationCount = $violations.Count
        violations = @($violations)
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    [pscustomobject]@{ violationCount = $violations.Count; allowedInitializerCount = $allowedInitializers.Count } |
        ConvertTo-Json -Compress
} finally { $module.Dispose() }
if ($violations.Count -gt 0) { exit 1 }
