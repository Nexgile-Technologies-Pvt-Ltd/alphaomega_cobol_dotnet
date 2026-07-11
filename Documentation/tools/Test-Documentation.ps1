[CmdletBinding()]
param(
    [string]$DocumentationRoot,
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
$errors = New-Object System.Collections.Generic.List[string]

if ([string]::IsNullOrWhiteSpace($DocumentationRoot)) {
    $DocumentationRoot = Split-Path -Parent $PSScriptRoot
}
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Old_Cobol_Code'
}

function Add-Failure([string]$Message) {
    $errors.Add($Message)
}

function Get-RelativePath([string]$BasePath, [string]$TargetPath) {
    $base = New-Object System.Uri(($BasePath.TrimEnd('\') + '\'))
    $target = New-Object System.Uri($TargetPath)
    return [System.Uri]::UnescapeDataString($base.MakeRelativeUri($target).ToString()).Replace('\', '/')
}

$script:headingAnchorCache = @{}

function ConvertTo-GitHubHeadingSlug([string]$Heading) {
    $plain = [System.Net.WebUtility]::HtmlDecode($Heading)
    $plain = [regex]::Replace($plain, '!\[([^\]]*)\]\([^)]+\)', '$1')
    $plain = [regex]::Replace($plain, '\[([^\]]+)\]\([^)]+\)', '$1')
    $plain = [regex]::Replace($plain, '<[^>]+>', '')
    $plain = $plain.Replace([string][char]96, '')
    $plain = [regex]::Replace($plain, '[*~]', '')
    $slug = $plain.Trim().ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^\p{L}\p{Nd}\s_-]', '')
    return [regex]::Replace($slug, '\s', '-')
}

function Get-MarkdownHeadingAnchors([string]$Path) {
    if ($script:headingAnchorCache.ContainsKey($Path)) {
        return $script:headingAnchorCache[$Path]
    }

    $anchors = @{}
    $slugCounts = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -notmatch '^\s{0,3}#{1,6}[ \t]+(.+?)[ \t]*$') {
            continue
        }

        $heading = [regex]::Replace($Matches[1], '[ \t]+#+[ \t]*$', '')
        $baseSlug = ConvertTo-GitHubHeadingSlug $heading
        if ([string]::IsNullOrWhiteSpace($baseSlug)) {
            continue
        }

        if ($slugCounts.ContainsKey($baseSlug)) {
            $slugCounts[$baseSlug] = [int]$slugCounts[$baseSlug] + 1
            $slug = "$baseSlug-$($slugCounts[$baseSlug])"
        } else {
            $slugCounts[$baseSlug] = 0
            $slug = $baseSlug
        }
        $anchors[$slug] = $true
    }

    $script:headingAnchorCache[$Path] = $anchors
    return $anchors
}

$DocumentationRoot = (Resolve-Path -LiteralPath $DocumentationRoot).Path
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$workspaceRoot = Split-Path -Parent $DocumentationRoot
$markdownFiles = @(Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File -Filter '*.md')

$requiredPages = @(
    'Home.md', 'Documentation-Conventions.md', '01-Product-Scope.md',
    '02-System-Context-and-Architecture.md', '03-Functional-Requirements.md',
    '04-Online-Screens-and-Navigation.md', '05-Batch-Processing.md',
    '06-Domain-Data-Model.md', '07-Optional-Modules-and-Integrations.md',
    '08-Security-and-Controls.md', '09-DotNet-Target-Architecture.md',
    '10-Implementation-Plan.md', '11-Test-and-Acceptance-Plan.md',
    '12-Operations-and-Deployment.md', '13-Traceability-and-Coverage.md',
    '14-Known-Defects-and-Open-Decisions.md', 'Appendix-Program-Catalog.md',
    'Appendix-File-and-Record-Layouts.md', 'Appendix-BMS-Field-Catalog.md',
    'Appendix-Source-Inventory.md', 'Glossary.md', '_Sidebar.md', '_Footer.md'
)

foreach ($page in $requiredPages) {
    if (-not (Test-Path -LiteralPath (Join-Path $DocumentationRoot $page) -PathType Leaf)) {
        Add-Failure "Missing required page: $page"
    }
}

$linkCount = 0
$sourceAnchorCount = 0
$markdownAnchorCount = 0
$lineCountCache = @{}

foreach ($markdownFile in $markdownFiles) {
    $content = [System.IO.File]::ReadAllText($markdownFile.FullName)
    $matches = [regex]::Matches($content, '\[[^\]]*\]\(([^)]+)\)')
    foreach ($match in $matches) {
        $rawTarget = $match.Groups[1].Value.Trim()
        if ($rawTarget -match '^(https?://|mailto:)') {
            continue
        }

        $linkCount++
        if ($rawTarget.StartsWith('<') -and $rawTarget.EndsWith('>')) {
            $rawTarget = $rawTarget.Substring(1, $rawTarget.Length - 2)
        }

        $hashIndex = $rawTarget.IndexOf([char]35)
        if ($hashIndex -ge 0) {
            $pathPart = [System.Uri]::UnescapeDataString($rawTarget.Substring(0, $hashIndex))
            $fragment = [System.Uri]::UnescapeDataString($rawTarget.Substring($hashIndex + 1))
        } else {
            $pathPart = [System.Uri]::UnescapeDataString($rawTarget)
            $fragment = ''
        }

        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            $candidate = $markdownFile.FullName
        } else {
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $pathPart))
        }

        if (-not (Test-Path -LiteralPath $candidate)) {
            Add-Failure "Broken link in $($markdownFile.Name): $rawTarget"
            continue
        }

        if ($fragment -match '^L([0-9]+)(?:-L?([0-9]+))?$' -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $sourceAnchorCount++
            $start = [int]$Matches[1]
            $end = if ($Matches[2]) { [int]$Matches[2] } else { $start }
            if (-not $lineCountCache.ContainsKey($candidate)) {
                $lineCountCache[$candidate] = @([System.IO.File]::ReadLines($candidate)).Count
            }
            $available = [int]$lineCountCache[$candidate]
            if ($start -lt 1 -or $end -lt $start -or $end -gt $available) {
                Add-Failure "Invalid source line anchor in $($markdownFile.Name): $rawTarget (file has $available lines)"
            }
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($fragment) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf) -and
            [System.IO.Path]::GetExtension($candidate) -ieq '.md') {
            $markdownAnchorCount++
            $anchors = Get-MarkdownHeadingAnchors $candidate
            if (-not $anchors.ContainsKey($fragment.ToLowerInvariant())) {
                Add-Failure "Invalid Markdown heading anchor in $($markdownFile.Name): $rawTarget"
            }
        }
    }
}

$inventoryPath = Join-Path $DocumentationRoot 'appendices\source-inventory.csv'
$inventoryRows = @()
if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
    Add-Failure 'Missing generated source inventory CSV.'
} else {
    $inventoryRows = @(Import-Csv -LiteralPath $inventoryPath -Encoding UTF8)
    $actualFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File)
    $binaryExtensions = @('.zip', '.png', '.dat', '.ps', '.init')
    if ($inventoryRows.Count -ne $actualFiles.Count) {
        Add-Failure "Inventory count $($inventoryRows.Count) does not match source file count $($actualFiles.Count)."
    }

    $inventoryPaths = @{}
    foreach ($row in $inventoryRows) {
        $relative = [string]$row.relative_path
        if ([string]::IsNullOrWhiteSpace($relative)) {
            Add-Failure 'Inventory contains a row with an empty relative_path.'
            continue
        }
        if ($inventoryPaths.ContainsKey($relative)) {
            Add-Failure "Inventory contains duplicate path: $relative"
            continue
        }
        $inventoryPaths[$relative] = $row
    }

    $actualPaths = @{}
    foreach ($file in $actualFiles) {
        $relative = Get-RelativePath -BasePath $SourceRoot -TargetPath $file.FullName
        $actualPaths[$relative] = $file
        if (-not $inventoryPaths.ContainsKey($relative)) {
            Add-Failure "Source file absent from inventory: $relative"
        }
    }

    foreach ($entry in $inventoryPaths.GetEnumerator()) {
        $relative = [string]$entry.Key
        $row = $entry.Value
        if (-not $actualPaths.ContainsKey($relative)) {
            Add-Failure "Inventory path no longer exists in source: $relative"
            continue
        }

        $file = [System.IO.FileInfo]$actualPaths[$relative]
        $expectedBytes = [long]0
        if (-not [long]::TryParse([string]$row.bytes, [ref]$expectedBytes)) {
            Add-Failure "Inventory bytes is not an integer for $($relative): $($row.bytes)"
        } elseif ($expectedBytes -ne $file.Length) {
            Add-Failure "Inventory bytes mismatch for $($relative): inventory $expectedBytes, source $($file.Length)."
        }

        $extension = $file.Extension.ToLowerInvariant()
        $isBinary = $binaryExtensions -contains $extension
        if ($isBinary) {
            if (-not [string]::IsNullOrWhiteSpace([string]$row.lines)) {
                Add-Failure "Inventory lines must be blank for binary file $($relative); found $($row.lines)."
            }
        } else {
            $actualLines = @(Get-Content -LiteralPath $file.FullName).Count
            $expectedLines = [long]0
            if (-not [long]::TryParse([string]$row.lines, [ref]$expectedLines)) {
                Add-Failure "Inventory lines is not an integer for $($relative): $($row.lines)"
            } elseif ($expectedLines -ne $actualLines) {
                Add-Failure "Inventory lines mismatch for $($relative): inventory $expectedLines, source $actualLines."
            }
        }

        $expectedHash = [string]$row.sha256
        if ($expectedHash -cnotmatch '^[0-9a-f]{64}$') {
            Add-Failure "Inventory SHA-256 is not 64 lowercase hexadecimal characters for $($relative): $expectedHash"
        } else {
            $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expectedHash -cne $actualHash) {
                Add-Failure "Inventory SHA-256 mismatch for $($relative): inventory $expectedHash, source $actualHash."
            }
        }
    }
}

$cobolFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | Where-Object { $_.Extension -in '.cbl', '.cob' })
$jclFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Filter '*.jcl')
$bmsFiles = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Filter '*.bms')

if ($cobolFiles.Count -ne 44) { Add-Failure "Expected 44 COBOL files for this snapshot; found $($cobolFiles.Count)." }
if ($jclFiles.Count -ne 55) { Add-Failure "Expected 55 JCL files for this snapshot; found $($jclFiles.Count)." }
if ($bmsFiles.Count -ne 21) { Add-Failure "Expected 21 BMS files for this snapshot; found $($bmsFiles.Count)." }

$catalogPath = Join-Path $DocumentationRoot 'Appendix-Program-Catalog.md'
if (Test-Path -LiteralPath $catalogPath -PathType Leaf) {
    $catalog = [System.IO.File]::ReadAllText($catalogPath).Replace('\', '/')
    foreach ($file in @($cobolFiles + $jclFiles)) {
        $relative = Get-RelativePath -BasePath $SourceRoot -TargetPath $file.FullName
        if (-not $catalog.Contains($relative)) {
            Add-Failure "Program/job catalog does not mention: $relative"
        }
    }
}

$bmsCatalogPath = Join-Path $DocumentationRoot 'Appendix-BMS-Field-Catalog.md'
$bmsHeadingCount = 0
$bmsSectionCount = 0
$bmsFieldRowCount = 0
if (Test-Path -LiteralPath $bmsCatalogPath -PathType Leaf) {
    $bmsLines = [System.IO.File]::ReadAllLines($bmsCatalogPath)
    $bmsHeadingCount = @($bmsLines | Where-Object { $_ -match '^## ' }).Count
    $bmsSectionCount = @($bmsLines | Where-Object { $_ -match '^Source: \[app/.+\.bms\]\(\.\./Old_Cobol_Code/app/.+\.bms\)$' }).Count
    $bmsFieldRowCount = @($bmsLines | Where-Object { $_ -match '^\| .*\| \[L[0-9]+\]\(' }).Count
    if ($bmsHeadingCount -ne 22) { Add-Failure "Expected 22 BMS catalog headings (Catalog plus 21 maps); found $bmsHeadingCount." }
    if ($bmsSectionCount -ne 21) { Add-Failure "Expected 21 BMS map source sections; found $bmsSectionCount." }
    if ($bmsFieldRowCount -ne 585) { Add-Failure "Expected 585 BMS field rows; found $bmsFieldRowCount." }
}

$forbiddenPattern = '\$relative|Escape-Markdown|(?i)\b(TODO|TBD|FIXME)\b'
foreach ($markdownFile in $markdownFiles) {
    $hits = @(Select-String -LiteralPath $markdownFile.FullName -Pattern $forbiddenPattern -AllMatches)
    foreach ($hit in $hits) {
        Add-Failure "Unresolved authoring token in $($markdownFile.Name):$($hit.LineNumber)"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Documentation validation FAILED with $($errors.Count) issue(s):" -ForegroundColor Red
    foreach ($failure in $errors) { Write-Host " - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host 'Documentation validation passed.' -ForegroundColor Green
Write-Host "Markdown pages checked: $($markdownFiles.Count)"
Write-Host "Local links checked: $linkCount"
Write-Host "Source line anchors checked: $sourceAnchorCount"
Write-Host "Markdown heading anchors checked: $markdownAnchorCount"
Write-Host "Inventory rows: $($inventoryRows.Count)"
Write-Host "COBOL/JCL/BMS: $($cobolFiles.Count)/$($jclFiles.Count)/$($bmsFiles.Count)"
Write-Host "BMS catalog headings: $bmsHeadingCount"
Write-Host "BMS sections/named fields: $bmsSectionCount/$bmsFieldRowCount"
