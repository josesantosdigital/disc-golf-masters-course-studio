[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$sidecar = $archive + '.sha256'
if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) { throw "SHA-256 sidecar is missing: $sidecar" }

$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToUpperInvariant()
$sidecarText = (Get-Content -LiteralPath $sidecar -Raw).Trim()
$expectedSidecar = "$actualHash  $([IO.Path]::GetFileName($archive))"
if (-not [string]::Equals($sidecarText, $expectedSidecar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHA-256 sidecar does not match the archive. Expected '$expectedSidecar'."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Import-Module (Join-Path $PSScriptRoot 'Modules\ReleaseArchivePolicy.psm1') -Force
$auditedTextExtensions = @('.md', '.txt', '.ps1', '.psm1', '.cs', '.csproj', '.json', '.js', '.html', '.css', '.xml', '.yml', '.yaml', '.editorconfig', '.props', '.targets')
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries)
    $paths = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $pathSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    function Get-ZipEntryByPath([string]$RelativePath) {
        $matches = @($entries | Where-Object {
            [string]::Equals($_.FullName.Replace('\', '/'), $RelativePath.Replace('\', '/'), [StringComparison]::OrdinalIgnoreCase)
        })
        if ($matches.Count -ne 1) { throw "Expected one archive entry for '$RelativePath'; found $($matches.Count)." }
        return $matches[0]
    }

    foreach ($entryPath in $paths) {
        if (-not $pathSet.Add($entryPath)) { throw "Archive contains a duplicate path: $entryPath" }
        if ($entryPath.StartsWith('/') -or $entryPath -match '(^|/)\.\.?(/|$)') {
            throw "Archive contains an unsafe path: $entryPath"
        }
        if ($entryPath -match '(^|/)(bin|obj)(/|$)') {
            throw "Archive contains build output: $entryPath"
        }
        if ($entryPath -match '(^|/)BepInEx/(core|interop)/|(^|/)Disc Golf Masters_Data/|(^|/)MonoBleedingEdge/') {
            throw "Archive contains game or mod-framework files: $entryPath"
        }
        if ($entryPath -match '(^|/)BepInEx/plugins/CustomCourseLoader/Maps(/|$)|(^|/)assets/maps(/|$)') {
            throw "Archive contains imported maps or generated previews: $entryPath"
        }
        $assetViolation = Get-ReleaseAssetBoundaryViolation -EntryPath $entryPath
        if (-not [string]::IsNullOrWhiteSpace($assetViolation)) {
            throw "Archive contains $assetViolation`: $entryPath"
        }
        if ([IO.Path]::GetExtension($entryPath).ToLowerInvariant() -in $auditedTextExtensions) {
            $textEntry = Get-ZipEntryByPath $entryPath
            $textReader = [IO.StreamReader]::new($textEntry.Open())
            try { $entryText = $textReader.ReadToEnd() } finally { $textReader.Dispose() }
            $textViolation = Get-ReleaseTextBoundaryViolation -Text $entryText
            if (-not [string]::IsNullOrWhiteSpace($textViolation)) {
                throw "Archive contains $textViolation in: $entryPath"
            }
        }
        if ([IO.Path]::GetExtension($entryPath) -ieq '.dll' -and
            $entryPath -cne 'BepInEx/plugins/CustomCourseLoader/CustomCourseLoader.dll') {
            throw "Archive contains an unexpected DLL: $entryPath"
        }
    }

    $allowedCourseFiles = @(
        'BepInEx/plugins/CustomCourseLoader/Courses/SampleMeadowNine.json'
    )
    foreach ($coursePath in ($paths | Where-Object { $_ -match '(^|/)BepInEx/plugins/CustomCourseLoader/Courses/|^Source/CustomCourseLoader/Courses/' })) {
        if ($coursePath -notin $allowedCourseFiles) { throw "Archive contains non-sample player course data: $coursePath" }
    }

    $required = @(
        'package-manifest.json',
        'LICENSE',
        'README.md',
        'CHANGELOG.md',
        'Install-CourseStudio.ps1',
        'THIRD_PARTY_NOTICES.md',
        'AGENTS.md',
        'BepInEx/plugins/CustomCourseLoader/CustomCourseLoader.dll',
        'BepInEx/plugins/CustomCourseLoader/Courses/SampleMeadowNine.json',
        'Source/CustomCourseLoader/Tools/Samples/SampleMeadowNine.json',
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/index.html',
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/styles.css',
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/catalog.js',
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/server.js',
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/map-catalog.js',
        'Source/CustomCourseLoader/AGENTS.md',
        'Source/CustomCourseLoader/LICENSE',
        'Source/CustomCourseLoader/Install-CourseStudio.ps1',
        'ThirdPartyLicenses/BepInEx-LICENSE.txt',
        'ThirdPartyLicenses/Il2CppInterop-LICENSE.txt',
        'ThirdPartyLicenses/HarmonyX-LICENSE.txt',
        'ThirdPartyLicenses/Harmony-LICENSE.txt',
        'Source/CustomCourseLoader/ThirdPartyLicenses/BepInEx-LICENSE.txt',
        'Source/CustomCourseLoader/ThirdPartyLicenses/Il2CppInterop-LICENSE.txt',
        'Source/CustomCourseLoader/ThirdPartyLicenses/HarmonyX-LICENSE.txt',
        'Source/CustomCourseLoader/ThirdPartyLicenses/Harmony-LICENSE.txt',
        'Source/CustomCourseLoader/Plugin.cs',
        'Source/CustomCourseLoader/CustomCourseLoader.csproj',
        'Source/CustomCourseLoader/.gitignore',
        'Source/CustomCourseLoader/.gitattributes',
        'Source/CustomCourseLoader/CourseEditor/index.html',
        'Source/CustomCourseLoader/CourseEditor/styles.css',
        'Source/CustomCourseLoader/CourseEditor/catalog.js',
        'Source/CustomCourseLoader/CourseEditor/server.js',
        'Source/CustomCourseLoader/CourseEditor/map-catalog.js',
        'Source/CustomCourseLoader/README.md',
        'Source/CustomCourseLoader/THIRD_PARTY_NOTICES.md',
        'Source/CustomCourseLoader/RENDERING.md',
        'Source/CustomCourseLoader/Services/CourseRegistry.Selection.cs',
        'Source/CustomCourseLoader/Services/CourseCardFactory.cs',
        'Source/CustomCourseLoader/Runtime/Patches/CourseScenePatch.cs',
        'Source/CustomCourseLoader/Runtime/Patches/ImportedMapRendering.cs',
        'Source/CustomCourseLoader/docs/ARCHITECTURE.md',
        'Source/CustomCourseLoader/docs/AI_PROJECT_GUIDE.md',
        'Source/CustomCourseLoader/docs/CONVERTER_ADAPTERS.md',
        'Source/CustomCourseLoader/docs/COURSE_FORMAT.md',
        'Source/CustomCourseLoader/docs/PLAYER_GUIDE.md',
        'Source/CustomCourseLoader/docs/PORTING_GUIDE.md',
        'Source/CustomCourseLoader/docs/RELEASE_CHECKLIST.md',
        'Source/CustomCourseLoader/docs/SUPPORTED_ENVIRONMENT.md',
        'Source/CustomCourseLoader/docs/TROUBLESHOOTING.md',
        'docs/PORTING_GUIDE.md',
        'Source/CustomCourseLoader/Tools/ConvertBonelabPallet.ps1',
        'Source/CustomCourseLoader/Tools/ConvertUnitySceneBundle.ps1',
        'Source/CustomCourseLoader/Tools/ConvertCourseLayoutJson.ps1',
        'Source/CustomCourseLoader/Tools/Modules/ReleaseArchivePolicy.psm1',
        'Source/CustomCourseLoader/Tools/TestReleasePackagePolicy.ps1',
        'Source/CustomCourseLoader/Tools/TestGitIgnore.ps1',
        'Source/CustomCourseLoader/Tools/TestBuildInstall.ps1',
        'Source/CustomCourseLoader/Tools/FoliageGeometryChecks/FoliageGeometryChecks.csproj',
        'Source/CustomCourseLoader/Tools/FoliageGeometryChecks/Program.cs',
        'Source/CustomCourseLoader/Runtime/ImportedFoliageGeometry.cs',
        'Source/CustomCourseLoader/Runtime/ImportedFoliageInteraction.cs',
        'Source/CustomCourseLoader/Runtime/Patches/ImportedFoliageInteractionPatch.cs',
        'Tools/README.md',
        'Tools/Schemas/course-layout-interchange.schema.json',
        'Tools/ValidateCoursePackage.ps1',
        'Tools/TestBuildInstall.ps1',
        'Tools/TestCourseStudioInstaller.ps1',
        'Tools/TestCoursePackageValidation.ps1',
        'Tools/TestCourseConverters.ps1',
        'Tools/TestCourseEditorExport.cjs',
        'Tools/TestMapCatalog.cjs',
        'Tools/TestMapLightingSettings.cjs',
        'Tools/TestReleasePackage.ps1',
        'Tools/TestGitIgnore.ps1'
    )
    foreach ($requiredPath in $required) {
        if (-not $pathSet.Contains($requiredPath)) { throw "Required archive file is missing: $requiredPath" }
    }

    $attributesEntry = Get-ZipEntryByPath 'Source/CustomCourseLoader/.gitattributes'
    $attributesReader = [IO.StreamReader]::new($attributesEntry.Open())
    try { $attributesText = $attributesReader.ReadToEnd() } finally { $attributesReader.Dispose() }
    foreach ($requiredAttribute in @('* text=auto eol=lf', '*.bundle binary', '*.png binary', '*.zip binary')) {
        if (-not $attributesText.Contains($requiredAttribute)) {
            throw "Packaged .gitattributes is missing required rule '$requiredAttribute'."
        }
    }

    $requiredBonelabSourceUrls = @(
        'https://mod.io/g/bonelab/m/teenytiny-isle#description',
        'https://mod.io/g/bonelab/m/rp-southside#description',
        'https://mod.io/g/bonelab/m/gm-hoodcorner-v2#description',
        'https://mod.io/g/bonelab/m/711-alpha#description',
        'https://mod.io/g/bonelab/m/gm-milsim-forest#description',
        'https://mod.io/g/bonelab/m/tree-land#description',
        'https://mod.io/g/bonelab/m/the-simpsons-pack#description'
    )
    foreach ($guidePath in @('docs/PORTING_GUIDE.md', 'Source/CustomCourseLoader/docs/PORTING_GUIDE.md')) {
        $guideEntry = Get-ZipEntryByPath $guidePath
        $guideReader = [IO.StreamReader]::new($guideEntry.Open())
        try { $guideText = $guideReader.ReadToEnd() } finally { $guideReader.Dispose() }
        foreach ($sourceUrl in $requiredBonelabSourceUrls) {
            if ($guideText.IndexOf($sourceUrl, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                throw "Packaged porting guide '$guidePath' is missing BONELAB source reference '$sourceUrl'."
            }
        }
    }

    $ignoreEntry = Get-ZipEntryByPath 'Source/CustomCourseLoader/.gitignore'
    $ignoreReader = [IO.StreamReader]::new($ignoreEntry.Open())
    try { $ignoreText = $ignoreReader.ReadToEnd() } finally { $ignoreReader.Dispose() }
    $ignoreLines = @($ignoreText -split "`r?`n" | ForEach-Object { $_.Trim() })
    $requiredIgnoreRules = @(
        '/bin/',
        '/obj/',
        '**/bin/',
        '**/obj/',
        '/Tools/.gitignore-fixtures/',
        '/dist/',
        '/backups/',
        '/Maps/',
        '/Courses/',
        '/CourseEditor/catalog.js',
        '/CourseEditor/assets/maps/*'
    )
    foreach ($rule in $requiredIgnoreRules) {
        if ($ignoreLines -notcontains $rule) {
            throw "Packaged .gitignore is missing required local-data rule '$rule'."
        }
    }

    function Resolve-MarkdownArchivePath([string]$MarkdownPath, [string]$Target) {
        $pathPart = ($Target -split '\?', 2)[0] -split '#', 2 | Select-Object -First 1
        $pathPart = [Uri]::UnescapeDataString([string]$pathPart).Replace('\', '/')
        $segments = [System.Collections.Generic.List[string]]::new()
        if (-not $pathPart.StartsWith('/')) {
            $lastSlash = $MarkdownPath.LastIndexOf('/')
            if ($lastSlash -ge 0) {
                foreach ($segment in $MarkdownPath.Substring(0, $lastSlash).Split('/')) {
                    if (-not [string]::IsNullOrEmpty($segment)) { $segments.Add($segment) }
                }
            }
        }
        foreach ($segment in $pathPart.TrimStart('/').Split('/')) {
            if ([string]::IsNullOrEmpty($segment) -or $segment -eq '.') { continue }
            if ($segment -eq '..') {
                if ($segments.Count -eq 0) { throw "Markdown link escapes the archive: $MarkdownPath -> $Target" }
                $segments.RemoveAt($segments.Count - 1)
                continue
            }
            $segments.Add($segment)
        }
        if ($segments.Count -eq 0) { return $MarkdownPath }
        return $segments -join '/'
    }

    $markdownLinkPattern = [regex]::new('\[[^\]]+\]\((<[^>]+>|[^)\s]+)(?:\s+[^)]*)?\)')
    foreach ($entry in ($entries | Where-Object { $_.FullName -match '(?i)\.md$' })) {
        $markdownPath = $entry.FullName.Replace('\', '/')
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $markdown = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($match in $markdownLinkPattern.Matches($markdown)) {
            $target = $match.Groups[1].Value.Trim()
            if ($target.StartsWith('<') -and $target.EndsWith('>')) { $target = $target.Substring(1, $target.Length - 2) }
            if ($target -match '^(?i)(https?:|mailto:|tel:|data:)') { continue }

            $resolvedPath = Resolve-MarkdownArchivePath $markdownPath $target
            $directoryPrefix = $resolvedPath.TrimEnd('/') + '/'
            $exists = $pathSet.Contains($resolvedPath) -or
                @($paths | Where-Object { $_.StartsWith($directoryPrefix, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            if (-not $exists) { throw "Markdown link in '$markdownPath' points to a missing archive path: '$target'." }
        }
    }

    $licenseRequirements = @(
        @{ Path = 'ThirdPartyLicenses/BepInEx-LICENSE.txt'; Markers = @('GNU LESSER GENERAL PUBLIC LICENSE', 'Version 2.1, February 1999', 'version 2.1 of the License', '(at your option) any later version') },
        @{ Path = 'ThirdPartyLicenses/Il2CppInterop-LICENSE.txt'; Markers = @('GNU LESSER GENERAL PUBLIC LICENSE', 'Version 3, 29 June 2007', 'GNU GENERAL PUBLIC LICENSE') },
        @{ Path = 'ThirdPartyLicenses/HarmonyX-LICENSE.txt'; Markers = @('MIT License', 'Copyright (c) 2020 BepInEx') },
        @{ Path = 'ThirdPartyLicenses/Harmony-LICENSE.txt'; Markers = @('MIT License', 'Copyright (c) 2017 Andreas Pardeike') }
    )
    foreach ($license in $licenseRequirements) {
        $entry = Get-ZipEntryByPath $license.Path
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $licenseText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($marker in $license.Markers) {
            if ($licenseText -notmatch [regex]::Escape($marker)) {
                throw "Packaged third-party license copy '$($license.Path)' is missing '$marker'."
            }
        }
        $sourceCopy = 'Source/CustomCourseLoader/' + $license.Path
        $sourceEntry = Get-ZipEntryByPath $sourceCopy
        $sourceReader = [IO.StreamReader]::new($sourceEntry.Open())
        try { $sourceLicenseText = $sourceReader.ReadToEnd() } finally { $sourceReader.Dispose() }
        if (-not [string]::Equals($licenseText, $sourceLicenseText, [StringComparison]::Ordinal)) {
            throw "Packaged license copies differ between the archive root and source tree: $($license.Path)."
        }
    }

    $editorScripts = @('app-core.js', 'app-library.js', 'app-inspector.js', 'app-map.js', 'app-files.js', 'app-settings.js', 'app-events.js', 'app-utils.js', 'app.js')
    $htmlEntry = Get-ZipEntryByPath 'BepInEx/plugins/CustomCourseLoader/CourseEditor/index.html'
    $htmlReader = [IO.StreamReader]::new($htmlEntry.Open())
    try { $html = $htmlReader.ReadToEnd() } finally { $htmlReader.Dispose() }
    $loadedScripts = @([regex]::Matches($html, '<script\s+src="(app[^" ]*\.js)"') | ForEach-Object { $_.Groups[1].Value })
    if (($loadedScripts -join '|') -cne ($editorScripts -join '|')) {
        throw 'Course Editor scripts are missing or out of dependency order in index.html.'
    }
    foreach ($script in $editorScripts) {
        if ($html -notmatch ('src="' + [regex]::Escape($script) + '"')) {
            throw "Editor page does not load required script '$script'."
        }
        $installedScript = "BepInEx/plugins/CustomCourseLoader/CourseEditor/$script"
        if (-not $pathSet.Contains($installedScript)) { throw "Editor script is missing from the install payload: $script" }
        if (-not $pathSet.Contains("Source/CustomCourseLoader/CourseEditor/$script")) { throw "Editor source script is missing: $script" }
    }

    foreach ($serverPath in @(
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/server.js',
        'Source/CustomCourseLoader/CourseEditor/server.js'
    )) {
        $serverEntry = Get-ZipEntryByPath $serverPath
        $serverReader = [IO.StreamReader]::new($serverEntry.Open())
        try { $serverText = $serverReader.ReadToEnd() } finally { $serverReader.Dispose() }
        foreach ($requiredServerSetting in @('DGM_COURSE_EDITOR_PLUGIN_DIR', 'DGM_COURSE_EDITOR_READ_ONLY', 'server.listen(port, "127.0.0.1"')) {
            if ($serverText.IndexOf($requiredServerSetting, [StringComparison]::Ordinal) -lt 0) {
                throw "Course Editor server '$serverPath' is missing required local-root or loopback behavior '$requiredServerSetting'."
            }
        }
    }

    foreach ($catalogPath in @(
        'BepInEx/plugins/CustomCourseLoader/CourseEditor/catalog.js',
        'Source/CustomCourseLoader/CourseEditor/catalog.js'
    )) {
        if (-not $pathSet.Contains($catalogPath)) { throw "Empty generated-catalog placeholder is missing: $catalogPath" }
        $catalogEntry = Get-ZipEntryByPath $catalogPath
        $catalogReader = [IO.StreamReader]::new($catalogEntry.Open())
        try { $catalog = $catalogReader.ReadToEnd() } finally { $catalogReader.Dispose() }
        if ($catalog -notmatch 'courses\s*:\s*\[\s*\]') { throw "Archive includes generated course catalog data: $catalogPath" }
    }

    $manifestEntry = Get-ZipEntryByPath 'package-manifest.json'
    $manifestReader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json } finally { $manifestReader.Dispose() }
    $expectedPackage = "DiscGolfMasters-CourseStudio-$ExpectedVersion" + $(if ($Draft) { '-DRAFT' } else { '' })
    if ($manifest.pluginVersion -ne $ExpectedVersion -or $manifest.courseEditorVersion -ne $ExpectedVersion -or $manifest.package -ne $expectedPackage) {
        throw "Package manifest version/name does not match expected version '$ExpectedVersion'."
    }
    if ($manifest.license -ne 'MIT-0') { throw "Package manifest license must be MIT-0; found '$($manifest.license)' instead." }
    if ($manifest.licenseScope -ne 'Original Course Studio code and documentation; see THIRD_PARTY_NOTICES.md and ThirdPartyLicenses/ for dependency terms.') {
        throw 'Package manifest must limit MIT-0 to original project code and documentation and point to third-party terms.'
    }
    if ([bool]$manifest.draft -ne [bool]$Draft) { throw 'Package manifest draft status does not match the requested audit mode.' }
    if ($manifest.bundledUserMapAssets -ne $false -or $manifest.bundledOriginalGameAssets -ne $false) {
        throw 'Package manifest must state that user map and original game assets are excluded.'
    }

    $pluginEntry = Get-ZipEntryByPath 'Source/CustomCourseLoader/Plugin.cs'
    $pluginReader = [IO.StreamReader]::new($pluginEntry.Open())
    try { $pluginSource = $pluginReader.ReadToEnd() } finally { $pluginReader.Dispose() }
    $pluginVersionMatch = [regex]::Match($pluginSource, 'PluginVersion\s*=\s*"([^"]+)"')
    if (-not $pluginVersionMatch.Success -or $pluginVersionMatch.Groups[1].Value -ne $ExpectedVersion) {
        throw "Packaged Plugin.cs version does not match expected version '$ExpectedVersion'."
    }

    $changelogEntry = Get-ZipEntryByPath 'CHANGELOG.md'
    $changelogReader = [IO.StreamReader]::new($changelogEntry.Open())
    try { $changelog = $changelogReader.ReadToEnd() } finally { $changelogReader.Dispose() }
    $latestChangelogMatch = [regex]::Match($changelog, '(?m)^##\s+([0-9]+\.[0-9]+\.[0-9]+)\s+(?:development|candidate|draft)\s*$')
    if (-not $latestChangelogMatch.Success -or $latestChangelogMatch.Groups[1].Value -ne $ExpectedVersion) {
        throw "Changelog top version does not match expected version '$ExpectedVersion'."
    }

    $licenseEntryInfo = Get-ZipEntryByPath 'LICENSE'
    $licenseReader = [IO.StreamReader]::new($licenseEntryInfo.Open())
    try { $licenseText = $licenseReader.ReadToEnd() } finally { $licenseReader.Dispose() }
    if ($licenseText -notmatch '(?m)^MIT No Attribution License \(MIT-0\)$') {
        throw 'The packaged LICENSE file does not identify the selected MIT-0 license.'
    }

    $noticesEntry = Get-ZipEntryByPath 'THIRD_PARTY_NOTICES.md'
    $noticesReader = [IO.StreamReader]::new($noticesEntry.Open())
    try { $noticesText = $noticesReader.ReadToEnd() } finally { $noticesReader.Dispose() }
    foreach ($requiredNotice in @('BepInEx', 'LGPL-2.1-or-later', 'HarmonyX', 'Il2CppInterop', 'LGPL-3.0', 'does not determine whether plugin linking or distribution creates additional obligations')) {
        if ($noticesText -notmatch [regex]::Escape($requiredNotice)) {
            throw "Packaged third-party notices are missing required dependency/license detail: $requiredNotice"
        }
    }

    $draftMarker = $pathSet.Contains('DRAFT-RELEASE-STATUS.txt')
    $licenseEntry = $pathSet.Contains('LICENSE')
    if ($Draft) {
        if (-not $draftMarker) { throw 'Draft archive is missing DRAFT-RELEASE-STATUS.txt.' }
    }
    else {
        if ($draftMarker) { throw 'Public archive must not contain DRAFT-RELEASE-STATUS.txt.' }
        if (-not $licenseEntry) { throw 'Public archive is missing LICENSE.' }
    }

    Write-Host "Release archive audit passed: $($entries.Count) entries, version $ExpectedVersion, draft=$([bool]$Draft)."
    Write-Host "SHA256: $actualHash"
}
finally {
    $zip.Dispose()
}
