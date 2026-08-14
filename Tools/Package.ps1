param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ProductVersion = "",
    [switch]$BuildAuthorizationTool
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

function Get-ProjectVersionFromProps([string]$rootPath) {
    $propsPath = Join-Path $rootPath "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "Directory.Build.props was not found."
    }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $version = $props.Project.PropertyGroup.AbbRelaysProductVersion |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Directory.Build.props does not define AbbRelaysProductVersion."
    }

    return $version.Trim()
}

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $ProductVersion = Get-ProjectVersionFromProps $root
}

$productVersionParts = $ProductVersion.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
if ($productVersionParts.Count -ne 3) {
    throw "ProductVersion must use three segments, for example 2.2.6."
}

$assemblyVersion = "$ProductVersion.0"

$outputRoot = Join-Path $root "Generated\Package"
$appPublish = Join-Path $outputRoot "App"
$authPublish = Join-Path $outputRoot "AuthorizationTool"
$installerWork = Join-Path $outputRoot "Installer"
$msiPath = Join-Path $outputRoot "ABBRelaysOfflineConfigurator_$ProductVersion.msi"
$latestMsiPath = Join-Path $outputRoot "ABBRelaysOfflineConfigurator.msi"
$appIconPath = Join-Path $root "AbbRelaysOfflineConfigurator\Assets\abb-relays.ico"
$userDeclarationTextPath = Join-Path $root "Tools\Installer\UserDeclaration.txt"
$userDeclarationRtfPath = Join-Path $installerWork "UserDeclaration.rtf"

Remove-Item -LiteralPath $appPublish, $installerWork -Recurse -Force -ErrorAction SilentlyContinue
if ($BuildAuthorizationTool) {
    Remove-Item -LiteralPath $authPublish -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $outputRoot, $appPublish, $installerWork | Out-Null
if ($BuildAuthorizationTool) {
    New-Item -ItemType Directory -Force -Path $authPublish | Out-Null
}

dotnet publish (Join-Path $root "AbbRelaysOfflineConfigurator\AbbRelaysOfflineConfigurator.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $appPublish `
    /p:Version=$ProductVersion `
    /p:AssemblyVersion=$assemblyVersion `
    /p:FileVersion=$assemblyVersion `
    /p:InformationalVersion=$ProductVersion `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($BuildAuthorizationTool) {
    dotnet publish (Join-Path $root "AbbRelaysAuthorizationTool\AbbRelaysAuthorizationTool.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $authPublish `
        /p:Version=$ProductVersion `
        /p:AssemblyVersion=$assemblyVersion `
        /p:FileVersion=$assemblyVersion `
        /p:InformationalVersion=$ProductVersion `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:DebugType=None `
        /p:DebugSymbols=false
}

$componentGroupPath = Join-Path $installerWork "AppFiles.wxs"
$componentRows = New-Object System.Collections.Generic.List[string]
$componentRefs = New-Object System.Collections.Generic.List[string]
$index = 0

function Escape-Wix([string]$value) {
    return $value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

function New-WixId([string]$prefix, [string]$value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value))
    }
    finally {
        $sha.Dispose()
    }

    $hash = ([BitConverter]::ToString($bytes) -replace '-', '').Substring(0, 16)
    return "$prefix$hash"
}

function ConvertTo-RtfEscapedText([string]$value) {
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $value.ToCharArray()) {
        $code = [int][char]$character
        if ($code -eq 13) {
            continue
        }

        if ($code -eq 10) {
            [void]$builder.Append('\par ')
            [void]$builder.Append("`r`n")
            continue
        }

        if ($code -eq 92) {
            [void]$builder.Append('\\')
            continue
        }

        if ($code -eq 123) {
            [void]$builder.Append('\{')
            continue
        }

        if ($code -eq 125) {
            [void]$builder.Append('\}')
            continue
        }

        if ($code -gt 127) {
            $signedCode = if ($code -gt 32767) { $code - 65536 } else { $code }
            [void]$builder.Append('\u')
            [void]$builder.Append($signedCode)
            [void]$builder.Append('?')
            continue
        }

        [void]$builder.Append($character)
    }

    return $builder.ToString()
}

function ConvertTo-RtfDocument([string]$value) {
    $escapedText = ConvertTo-RtfEscapedText $value
    return "{\rtf1\ansi\uc1\deff0{\fonttbl{\f0 Microsoft YaHei UI;}}\viewkind4\pard\f0\fs20 $escapedText}"
}

function Add-DirectoryXml([System.IO.DirectoryInfo]$directory, [int]$level) {
    $indent = ' ' * $level
    foreach ($file in (Get-ChildItem -LiteralPath $directory.FullName -File | Sort-Object Name)) {
        $script:index++
        $componentId = "AppFile$script:index"
        $fileId = "AppFilePayload$script:index"
        $source = Escape-Wix $file.FullName
        $name = Escape-Wix $file.Name
        $componentRows.Add("$indent<Component Id=`"$componentId`" Guid=`"*`"><File Id=`"$fileId`" Source=`"$source`" Name=`"$name`" KeyPath=`"yes`" /></Component>")
        $componentRefs.Add("      <ComponentRef Id=`"$componentId`" />")
    }

    foreach ($child in (Get-ChildItem -LiteralPath $directory.FullName -Directory | Sort-Object Name)) {
        $relativePath = $child.FullName.Substring($appPublish.Length).TrimStart('\')
        $directoryId = New-WixId "Dir" $relativePath
        $name = Escape-Wix $child.Name
        $componentRows.Add("$indent<Directory Id=`"$directoryId`" Name=`"$name`">")
        Add-DirectoryXml $child ($level + 2)
        $componentRows.Add("$indent</Directory>")
    }
}

Add-DirectoryXml (Get-Item -LiteralPath $appPublish) 6

$appFilesWxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="INSTALLFOLDER">
$($componentRows -join "`r`n")
    </DirectoryRef>
  </Fragment>
  <Fragment>
    <ComponentGroup Id="PublishedAppFiles">
$($componentRefs -join "`r`n")
    </ComponentGroup>
  </Fragment>
</Wix>
"@
Set-Content -Path $componentGroupPath -Value $appFilesWxs -Encoding UTF8

if (-not (Test-Path -LiteralPath $userDeclarationTextPath)) {
    throw "User declaration file was not found: $userDeclarationTextPath"
}

$userDeclarationText = Get-Content -Raw -Encoding UTF8 -LiteralPath $userDeclarationTextPath
Set-Content -Path $userDeclarationRtfPath -Value (ConvertTo-RtfDocument $userDeclarationText) -Encoding ASCII

$wixCommand = Get-Command wix -ErrorAction SilentlyContinue
if ($null -eq $wixCommand) {
    Write-Warning "WiX Toolset CLI was not found. Self-contained publish is complete; install WiX and rerun this script to generate MSI."
    Write-Host "App publish directory: $appPublish"
    if ($BuildAuthorizationTool) {
        Write-Host "Authorization tool EXE: $(Join-Path $authPublish 'ABBRelaysAuthorizationTool.exe')"
    }
    else {
        Write-Host "Authorization tool build skipped. Existing files under $authPublish were preserved."
    }
    return
}

$wixBuildStartedAt = Get-Date
& $wixCommand.Source build `
    (Join-Path $root "Tools\Installer\Product.wxs") `
    $componentGroupPath `
    -d "ProductVersion=$ProductVersion" `
    -d "AppIconPath=$appIconPath" `
    -d "UserDeclarationRtfPath=$userDeclarationRtfPath" `
    -ext WixToolset.UI.wixext `
    -out $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $msiPath)) {
    $shortNameMsi = Get-ChildItem -LiteralPath $outputRoot -Filter "*.MSI" |
        Where-Object { $_.LastWriteTime -ge $wixBuildStartedAt.AddSeconds(-5) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -ne $shortNameMsi) {
        Move-Item -LiteralPath $shortNameMsi.FullName -Destination $msiPath -Force
    }
}

if (-not (Test-Path -LiteralPath $msiPath)) {
    throw "WiX completed but MSI was not produced: $msiPath"
}

$msiValidationTarget = Join-Path $installerWork "MsiValidation"
$msiValidationLog = Join-Path $installerWork "MsiValidation.log"
Remove-Item -LiteralPath $msiValidationTarget -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $msiValidationTarget | Out-Null
$msiValidationArgs = @(
    "/a",
    "`"$msiPath`"",
    "TARGETDIR=`"$msiValidationTarget`"",
    "/qn",
    "/l*v",
    "`"$msiValidationLog`""
)
$msiValidation = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiValidationArgs -Wait -PassThru -WindowStyle Hidden
if ($msiValidation.ExitCode -ne 0) {
    throw "MSI validation failed with exit code $($msiValidation.ExitCode). See log: $msiValidationLog"
}
Remove-Item -LiteralPath $msiValidationTarget -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item -LiteralPath $msiPath -Destination $latestMsiPath -Force
$msiHash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash

Write-Host "MSI: $msiPath"
Write-Host "MSI latest copy: $latestMsiPath"
Write-Host "MSI validation: administrative extraction succeeded."
Write-Host "MSI SHA256: $msiHash"
if ($BuildAuthorizationTool) {
    Write-Host "Authorization tool EXE: $(Join-Path $authPublish 'ABBRelaysAuthorizationTool.exe')"
}
else {
    Write-Host "Authorization tool build skipped. Existing files under $authPublish were preserved."
}
