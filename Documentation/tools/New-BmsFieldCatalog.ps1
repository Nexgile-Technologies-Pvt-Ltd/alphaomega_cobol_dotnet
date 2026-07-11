param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot "..\..\Old_Cobol_Code"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\Appendix-BMS-Field-Catalog.md")
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$files = Get-ChildItem -LiteralPath (Join-Path $source "app") -Recurse -File -Filter "*.bms" | Sort-Object FullName
$records = @()

foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length + 1).Replace("\", "/")
    $lines = Get-Content -LiteralPath $file.FullName
    $mapName = $file.BaseName

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*([A-Z0-9]+)\s+DFHMDI\b') {
            $mapName = $Matches[1]
        }

        if ($lines[$index] -notmatch '^\s*([A-Z0-9-]+)\s+DFHMDF\b') {
            continue
        }

        $fieldName = $Matches[1]
        $startLine = $index + 1
        $statementParts = @($lines[$index].TrimEnd().TrimEnd('-'))

        while ($index -lt ($lines.Count - 1) -and $lines[$index].TrimEnd().EndsWith('-')) {
            $index++
            $statementParts += $lines[$index].TrimEnd().TrimEnd('-')
        }

        $statement = (($statementParts -join ' ') -replace '\s+', ' ').Trim()
        $row = $null
        $column = $null
        $length = $null
        $attributes = ""
        $color = ""
        $highlight = ""
        $initial = ""

        if ($statement -match 'POS=\((\d+),(\d+)\)') {
            $row = [int]$Matches[1]
            $column = [int]$Matches[2]
        }
        if ($statement -match 'LENGTH=(\d+)') { $length = [int]$Matches[1] }
        if ($statement -match 'ATTRB=\(([^)]*)\)') { $attributes = $Matches[1] -replace '\s+', '' }
        if ($statement -match 'COLOR=([A-Z]+)') { $color = $Matches[1] }
        if ($statement -match 'HILIGHT=([A-Z]+)') { $highlight = $Matches[1] }
        if ($statement -match "INITIAL='([^']*)'") { $initial = $Matches[1] }

        $records += [pscustomobject]@{
            relative_path = $relative
            map = $mapName
            field = $fieldName
            row = $row
            column = $column
            length = $length
            attributes = $attributes
            color = $color
            highlight = $highlight
            initial = $initial
            source_line = $startLine
        }
    }
}

function Escape-Markdown([object]$value) {
    if ($null -eq $value) { return "" }
    return ([string]$value).Replace("|", "\|").Replace("`r", " ").Replace("`n", " ")
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine("# Appendix: BMS named-field catalog")
[void]$builder.AppendLine()
[void]$builder.AppendLine("[Online screens](04-Online-Screens-and-Navigation.md) | [Home](Home.md) | [Program catalog](Appendix-Program-Catalog.md)")
[void]$builder.AppendLine()
[void]$builder.AppendLine("This generated Markdown page enumerates every **named DFHMDF** field in all shipped core and optional BMS maps. Unnamed literal fields remain visible in the linked BMS source and are rendered through the screen templates; they are not input/output data fields. Positions are one-based 3270 row/column coordinates.")
[void]$builder.AppendLine()
[void]$builder.AppendLine("Generated from $($files.Count) BMS files with $($records.Count) named fields by [tools/New-BmsFieldCatalog.ps1](tools/New-BmsFieldCatalog.ps1).")
[void]$builder.AppendLine()
[void]$builder.AppendLine("## Catalog")
[void]$builder.AppendLine()

foreach ($fileGroup in ($records | Group-Object relative_path | Sort-Object Name)) {
    $relative = $fileGroup.Name
    $displayName = [System.IO.Path]::GetFileNameWithoutExtension($relative)
    [void]$builder.AppendLine("## $displayName")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("Source: [$relative](../Old_Cobol_Code/$relative)")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("| Map | Field | Row | Column | Length | ATTRB | Color | Highlight | Initial | Source |")
    [void]$builder.AppendLine("|---|---|---:|---:|---:|---|---|---|---|---|")

    foreach ($record in $fileGroup.Group) {
        $sourceLink = "[L$($record.source_line)](../Old_Cobol_Code/$relative#L$($record.source_line))"
        $values = @(
            (Escape-Markdown $record.map),
            (Escape-Markdown $record.field),
            (Escape-Markdown $record.row),
            (Escape-Markdown $record.column),
            (Escape-Markdown $record.length),
            (Escape-Markdown $record.attributes),
            (Escape-Markdown $record.color),
            (Escape-Markdown $record.highlight),
            (Escape-Markdown $record.initial),
            $sourceLink
        )
        [void]$builder.AppendLine("| $($values -join ' | ') |")
    }
    [void]$builder.AppendLine()
}

[void]$builder.AppendLine("---")
[void]$builder.AppendLine()
[void]$builder.AppendLine("[Online screens](04-Online-Screens-and-Navigation.md) | [Home](Home.md) | [Program catalog](Appendix-Program-Catalog.md)")

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Wrote $($records.Count) named fields from $($files.Count) BMS files to $OutputPath"
