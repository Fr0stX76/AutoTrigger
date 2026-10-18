[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Output,
    [Parameter(Mandatory = $true)][string]$Icon,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$FileVersion,
    [Parameter(Mandatory = $true)][string]$ProductVersion,
    [Parameter(Mandatory = $true)][string]$Description,
    [Parameter(Mandatory = $true)][string]$Product,
    [Parameter(Mandatory = $true)][string]$CopyrightYear,
    [Parameter(Mandatory = $true)][string]$CopyrightHolder,
    [Parameter(Mandatory = $true)][string]$OriginalFilename
)

$ErrorActionPreference = 'Stop'

$LangEnUs = 0x0409
$CodePageUnicode = 1200
$RT_ICON = 3
$RT_GROUP_ICON = 14
$RT_VERSION = 16
$RT_MANIFEST = 24
$IconGroupId = 32512

function Write-Padding([System.IO.BinaryWriter]$Writer) {
    while (($Writer.BaseStream.Position % 4) -ne 0) { $Writer.Write([byte]0) }
}

function Get-Bytes([System.IO.MemoryStream]$Stream) {
    return , $Stream.ToArray()
}

function New-VersionBlock {
    param(
        [string]$Key,
        [byte[]]$Value,
        [int]$ValueLength,
        [int]$Type,
        [object[]]$Children
    )
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]$ValueLength)
    $bw.Write([uint16]$Type)
    $bw.Write([System.Text.Encoding]::Unicode.GetBytes($Key + [char]0))
    Write-Padding $bw
    if ($null -ne $Value -and $Value.Length -gt 0) { $bw.Write($Value) }
    if ($null -ne $Children) {
        foreach ($child in $Children) {
            Write-Padding $bw
            $bw.Write([byte[]]$child)
        }
    }
    $bw.Flush()
    $bytes = $ms.ToArray()
    $length = $bytes.Length
    $bytes[0] = [byte]($length -band 0xFF)
    $bytes[1] = [byte](($length -shr 8) -band 0xFF)
    return , $bytes
}

function New-StringEntry([string]$Name, [string]$Text) {
    $value = [System.Text.Encoding]::Unicode.GetBytes($Text + [char]0)
    return , (New-VersionBlock -Key $Name -Value $value -ValueLength ($Text.Length + 1) -Type 1 -Children $null)
}

function ConvertTo-VersionParts([string]$Text) {
    $parts = @(0, 0, 0, 0)
    $split = $Text.Split('.')
    for ($i = 0; $i -lt [Math]::Min(4, $split.Length); $i++) {
        $n = 0
        if ([int]::TryParse(($split[$i] -replace '[^0-9].*$', ''), [ref]$n)) { $parts[$i] = $n }
    }
    return , $parts
}

function New-VersionResource {
    $fv = ConvertTo-VersionParts $FileVersion
    $pv = ConvertTo-VersionParts $ProductVersion

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]4277077181)
    $bw.Write([uint32]0x00010000)
    $bw.Write([uint32](($fv[0] -shl 16) -bor $fv[1]))
    $bw.Write([uint32](($fv[2] -shl 16) -bor $fv[3]))
    $bw.Write([uint32](($pv[0] -shl 16) -bor $pv[1]))
    $bw.Write([uint32](($pv[2] -shl 16) -bor $pv[3]))
    $bw.Write([uint32]0x3F)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0x00040004)
    $bw.Write([uint32]1)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)
    $bw.Flush()
    $fixed = $ms.ToArray()

    $copyright = 'Copyright ' + [char]0x00A9 + ' ' + $CopyrightYear + ' ' + $CopyrightHolder
    $internalName = [System.IO.Path]::GetFileNameWithoutExtension($OriginalFilename)
    $strings = @(
        (New-StringEntry 'FileDescription' $Description),
        (New-StringEntry 'FileVersion' $FileVersion),
        (New-StringEntry 'InternalName' $internalName),
        (New-StringEntry 'LegalCopyright' $copyright),
        (New-StringEntry 'OriginalFilename' $OriginalFilename),
        (New-StringEntry 'ProductName' $Product),
        (New-StringEntry 'ProductVersion' $ProductVersion),
        (New-StringEntry 'Assembly Version' $FileVersion)
    )
    $tableKey = '{0:X4}{1:X4}' -f $LangEnUs, $CodePageUnicode
    $table = New-VersionBlock -Key $tableKey -Value $null -ValueLength 0 -Type 1 -Children $strings
    $stringFileInfo = New-VersionBlock -Key 'StringFileInfo' -Value $null -ValueLength 0 -Type 1 -Children @(, $table)

    $translation = [byte[]]@(($LangEnUs -band 0xFF), ($LangEnUs -shr 8), ($CodePageUnicode -band 0xFF), ($CodePageUnicode -shr 8))
    $var = New-VersionBlock -Key 'Translation' -Value $translation -ValueLength 4 -Type 0 -Children $null
    $varFileInfo = New-VersionBlock -Key 'VarFileInfo' -Value $null -ValueLength 0 -Type 1 -Children @(, $var)

    return , (New-VersionBlock -Key 'VS_VERSION_INFO' -Value $fixed -ValueLength $fixed.Length -Type 0 -Children @($stringFileInfo, $varFileInfo))
}

function Write-ResEntry {
    param([System.IO.BinaryWriter]$Writer, [int]$Type, [int]$Name, [int]$Language, [int]$MemoryFlags, [byte[]]$Data)
    $size = 0
    if ($null -ne $Data) { $size = $Data.Length }
    $Writer.Write([uint32]$size)
    $Writer.Write([uint32]32)
    $Writer.Write([uint16]0xFFFF); $Writer.Write([uint16]$Type)
    $Writer.Write([uint16]0xFFFF); $Writer.Write([uint16]$Name)
    $Writer.Write([uint32]0)
    $Writer.Write([uint16]$MemoryFlags)
    $Writer.Write([uint16]$Language)
    $Writer.Write([uint32]0)
    $Writer.Write([uint32]0)
    if ($size -gt 0) { $Writer.Write($Data) }
    Write-Padding $Writer
}

$ico = [System.IO.File]::ReadAllBytes($Icon)
if ($ico.Length -lt 6 -or [BitConverter]::ToUInt16($ico, 2) -ne 1) { throw "Not an .ico file: $Icon" }
$count = [BitConverter]::ToUInt16($ico, 4)
$frames = @()
for ($i = 0; $i -lt $count; $i++) {
    $e = 6 + 16 * $i
    $length = [BitConverter]::ToUInt32($ico, $e + 8)
    $offset = [BitConverter]::ToUInt32($ico, $e + 12)
    $data = New-Object byte[] $length
    [Array]::Copy($ico, [int]$offset, $data, 0, [int]$length)
    $frames += , @{
        Width = $ico[$e]; Height = $ico[$e + 1]; Colors = $ico[$e + 2]; Reserved = $ico[$e + 3]
        Planes = [BitConverter]::ToUInt16($ico, $e + 4); BitCount = [BitConverter]::ToUInt16($ico, $e + 6)
        Data = $data
    }
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

Write-ResEntry -Writer $bw -Type 0 -Name 0 -Language 0 -MemoryFlags 0 -Data $null

for ($i = 0; $i -lt $frames.Count; $i++) {
    Write-ResEntry -Writer $bw -Type $RT_ICON -Name ($i + 1) -Language $LangEnUs -MemoryFlags 0x1010 -Data $frames[$i].Data
}

$g = New-Object System.IO.MemoryStream
$gw = New-Object System.IO.BinaryWriter($g)
$gw.Write([uint16]0); $gw.Write([uint16]1); $gw.Write([uint16]$frames.Count)
for ($i = 0; $i -lt $frames.Count; $i++) {
    $f = $frames[$i]
    $gw.Write([byte]$f.Width); $gw.Write([byte]$f.Height); $gw.Write([byte]$f.Colors); $gw.Write([byte]$f.Reserved)
    $gw.Write([uint16]$f.Planes); $gw.Write([uint16]$f.BitCount)
    $gw.Write([uint32]$f.Data.Length); $gw.Write([uint16]($i + 1))
}
$gw.Flush()
Write-ResEntry -Writer $bw -Type $RT_GROUP_ICON -Name $IconGroupId -Language $LangEnUs -MemoryFlags 0x1030 -Data $g.ToArray()

Write-ResEntry -Writer $bw -Type $RT_VERSION -Name 1 -Language $LangEnUs -MemoryFlags 0x0030 -Data (New-VersionResource)

Write-ResEntry -Writer $bw -Type $RT_MANIFEST -Name 1 -Language 0 -MemoryFlags 0x0030 -Data ([System.IO.File]::ReadAllBytes($Manifest))

$bw.Flush()
$dir = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Output))
if (-not [System.IO.Directory]::Exists($dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
[System.IO.File]::WriteAllBytes($Output, $ms.ToArray())
Write-Host "Win32 resources written: $Output"

