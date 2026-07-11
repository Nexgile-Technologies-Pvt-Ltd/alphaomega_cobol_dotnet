param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot "..\..\Old_Cobol_Code"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\appendices\source-inventory.csv")
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$binaryExtensions = @(".zip", ".png", ".dat", ".ps", ".init")

function Get-Area([string]$relativePath) {
    $path = $relativePath.Replace("\", "/")
    if ($path.StartsWith("app/app-authorization-ims-db2-mq/")) { return "optional-authorization-ims-db2-mq" }
    if ($path.StartsWith("app/app-transaction-type-db2/")) { return "optional-transaction-type-db2" }
    if ($path.StartsWith("app/app-vsam-mq/")) { return "optional-vsam-mq" }
    if ($path.StartsWith("app/")) { return "core-application" }
    if ($path.StartsWith("samples/")) { return "build-samples" }
    if ($path.StartsWith("scripts/")) { return "developer-automation" }
    if ($path.StartsWith("diagrams/")) { return "reference-diagrams" }
    return "repository-metadata"
}

function Get-Kind([System.IO.FileInfo]$file) {
    $extension = $file.Extension.ToLowerInvariant()
    switch ($extension) {
        ".cbl" { return "COBOL source" }
        ".cpy" { return "COBOL copybook or generated BMS copybook" }
        ".bms" { return "BMS map source" }
        ".jcl" { return "JCL" }
        ".prc" { return "cataloged procedure" }
        ".csd" { return "CICS resource definitions" }
        ".ddl" { return "Db2 DDL" }
        ".dcl" { return "Db2 declaration copybook" }
        ".dbd" { return "IMS DBD" }
        ".psb" { return "IMS PSB" }
        ".ctl" { return "utility control statements" }
        ".asm" { return "assembler source" }
        ".mac" { return "assembler macro" }
        ".txt" { return "text data or captured output" }
        ".sh" { return "shell automation" }
        ".awk" { return "AWK automation" }
        ".png" { return "reference image" }
        ".drawio" { return "diagram source" }
        ".zip" { return "runtime archive" }
        ".dat" { return "binary sample data" }
        ".ps" { return "EBCDIC sample data" }
        ".init" { return "EBCDIC initialization data" }
        ".controlm" { return "Control-M schedule" }
        ".ca7" { return "CA 7 schedule" }
        ".template" { return "build template" }
        ".md" { return "repository documentation" }
        default {
            if ($file.Length -eq 0) { return "marker or placeholder" }
            return "other"
        }
    }
}

$rows = Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($source.Length + 1).Replace("\", "/")
    $extension = $_.Extension.ToLowerInvariant()
    $isBinary = $binaryExtensions -contains $extension
    [pscustomobject]@{
        relative_path = $relative
        area          = Get-Area $relative
        artifact_kind = Get-Kind $_
        bytes         = $_.Length
        lines         = if ($isBinary) { $null } else { (Get-Content -LiteralPath $_.FullName).Count }
        sha256        = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
Write-Output "Wrote $($rows.Count) source artifacts to $OutputPath"
