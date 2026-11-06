[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SceneBundlePath,
    [Parameter(Mandatory = $true)][string]$ScenePath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_-]{1,48}$')][string]$Id,
    [Parameter(Mandatory = $true)][ValidateLength(1, 64)][string]$CourseName,
    [ValidateSet('CanonPark', 'MinokaSprings', 'BaldurDgc', 'MapleHill', 'ChampionsLanding', 'WinthropGold', 'NokiaDiscGolfPark', 'Terminalen')][string]$BaseCourse = 'CanonPark',
    [ValidateRange(1, 18)][int]$HoleCount = 9,
    [string[]]$DependencyBundlePaths = @(),
    [ValidateSet('Auto', 'Forest', 'Urban', 'Abstract')][string]$PreviewStyle = 'Auto',
    [Parameter(Mandatory = $true)][string]$PluginDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Modules\CoursePackage.psm1') -Force

$result = New-DgmCoursePackage `
    -SceneBundlePath $SceneBundlePath `
    -ScenePath $ScenePath `
    -Id $Id `
    -CourseName $CourseName `
    -BaseCourse $BaseCourse `
    -HoleCount $HoleCount `
    -DependencyBundlePaths $DependencyBundlePaths `
    -PluginDirectory $PluginDirectory `
    -PreviewStyle $PreviewStyle

Write-Host "Course definition: $($result.CoursePath)"
Write-Host "Map package: $($result.MapPath)"
Write-Host "Course preview card: $($result.CourseCardPath)"
Write-Host "Unity version: $($result.UnityVersion)"
Write-Warning 'This tool packages the original Unity scene bundle; it does not convert its engine version, game scripts, or internal course data.'
