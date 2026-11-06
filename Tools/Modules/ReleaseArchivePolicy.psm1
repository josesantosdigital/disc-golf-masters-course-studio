$script:ForbiddenReleaseAssetExtensions = @(
    '.bundle', '.assetbundle', '.unity3d', '.pdb', '.log', '.manifest', '.pallet',
    '.zip', '.7z', '.rar', '.tar', '.gz',
    '.png', '.jpg', '.jpeg', '.webp', '.gif', '.bmp', '.tif', '.tiff', '.tga', '.dds', '.ktx', '.psd', '.exr', '.hdr',
    '.fbx', '.obj', '.blend', '.gltf', '.glb', '.dae', '.stl', '.usd', '.usdz',
    '.unity', '.prefab', '.asset', '.mat', '.terrainlayer', '.shader', '.cginc', '.hlsl', '.compute',
    '.ttf', '.otf', '.woff', '.woff2',
    '.wav', '.mp3', '.ogg', '.flac', '.aiff', '.aif', '.m4a', '.aac',
    '.mp4', '.mov', '.mkv', '.avi', '.webm',
    '.resource', '.ress', '.bytes'
)

function Get-ReleaseAssetBoundaryViolation {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$EntryPath)

    $normalizedPath = $EntryPath.Replace('\', '/')
    if ([IO.Path]::GetExtension($normalizedPath) -in $script:ForbiddenReleaseAssetExtensions) {
        return 'a local or imported game asset'
    }
    if ($normalizedPath -match '(^|/)(catalog_[^/]*\.json|[^/]+\.pallet\.json)$') {
        return 'a BONELAB pallet catalog or manifest'
    }
    return $null
}

function Get-ReleaseTextBoundaryViolation {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $machineProfilePathPattern = '(?i)C:\\Users\\(?!(?:you|yourname|username|me)(?:\\|$)|<)[^\\]+\\'
    if ($Text -match $machineProfilePathPattern) {
        return 'a machine-specific Windows user-profile path'
    }
    return $null
}

Export-ModuleMember -Function Get-ReleaseAssetBoundaryViolation, Get-ReleaseTextBoundaryViolation
