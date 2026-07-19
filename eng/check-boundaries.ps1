[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$errors = [System.Collections.Generic.List[string]]::new()
$projectFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.csproj' -File)

function Get-RelativePath {
    param([string] $BasePath, [string] $TargetPath)
    $baseUri = [Uri]::new($BasePath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar)
    $targetUri = [Uri]::new($TargetPath)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

foreach ($projectFile in $projectFiles) {
    [xml] $project = Get-Content -LiteralPath $projectFile.FullName -Raw
    foreach ($reference in $project.SelectNodes('//ProjectReference')) {
        $include = $reference.GetAttribute('Include')
        if ($include -match '\$\(GmaModule(?!AccessControlRoot\))') {
            $relativeProject = Get-RelativePath -BasePath $repositoryRoot -TargetPath $projectFile.FullName
            $errors.Add("$relativeProject references another reusable module through '$include'.")
        }

        if ($projectFile.BaseName -match '^Gma\.Modules\.AccessControl\.(?:Api|AdminApi|AdminCli)$' -and
            $include -match 'Gma\.Modules\.AccessControl\.Domain') {
            $relativeProject = Get-RelativePath -BasePath $repositoryRoot -TargetPath $projectFile.FullName
            $errors.Add("$relativeProject crosses the front-door to domain project boundary.")
        }
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
foreach ($sourceFile in $sourceFiles) {
    $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
    $relativePath = Get-RelativePath -BasePath $repositoryRoot -TargetPath $sourceFile.FullName
    if ($source -match 'Gma\.Modules\.(?!AccessControl(?:\.|;))') {
        $errors.Add("$relativePath names another reusable module implementation or contract.")
    }

    if ($source -match '(?:BunkFy|StayQuest)\.') {
        $errors.Add("$relativePath contains product-specific source.")
    }

    if ($relativePath -match '^src\\Gma\.Modules\.AccessControl\.(?:Api|AdminApi|AdminCli)\\' -and
        $source -match 'Gma\.Modules\.AccessControl\.Domain') {
        $errors.Add("$relativePath crosses the front-door to domain source boundary.")
    }
}

$applicationPorts = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src\Gma.Modules.AccessControl.Application\Ports') -Filter '*.cs' -File)
foreach ($portFile in $applicationPorts) {
    $source = Get-Content -LiteralPath $portFile.FullName -Raw
    if ($source -match 'public\s+interface\s+I\w*Repository') {
        $relativePath = Get-RelativePath -BasePath $repositoryRoot -TargetPath $portFile.FullName
        $errors.Add("$relativePath exposes a persistence repository as a public consumer API.")
    }
}

if ($errors.Count -gt 0) {
    throw "AccessControl boundary checks failed:`n - $($errors -join "`n - ")"
}

Write-Host 'AccessControl boundary checks passed.'
