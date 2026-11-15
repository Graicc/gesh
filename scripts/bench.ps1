# Measures gesh's performance on the connected Quest, see plans/perf.md.
# Builds and installs a Release build (unless -NoInstall), runs the scripted
# bench scene with nobody wearing the headset, and prints the app's CPU frame
# time and the runtime's GPU time.
param(
	[int]$Runs = 1,
	[int]$Faces = 1000,
	[string]$Texel = "0.003",
	[switch]$NoInstall,
	[string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$package = "com.graic.gesh"
$folder = "/sdcard/Android/data/$package/files/gesh"

if (-not $NoInstall) {
	# Pick up PATH and NDK as saved, in case this shell started before ninja or
	# the NDK were installed.
	$env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
	if (-not $env:NDK) { $env:NDK = [Environment]::GetEnvironmentVariable("NDK", "User") }

	# Android builds need a native host compiler for StereoKit's shader tools.
	$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
	Import-Module "$vs\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
	Enter-VsDevShell -VsInstallPath $vs -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null
	Remove-Item Env:Platform -ErrorAction Ignore
	dotnet build ./Projects/Android/gesh.Android.csproj -c $Configuration -t:Install | Select-String -Pattern "error|Error\(s\)|Warn.*\(s\)"
	if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$activity = (adb shell cmd package resolve-activity --brief $package | Select-Object -Last 1).Trim()

# Keep rendering with nobody wearing it, don't block launches on the
# boundary dialog, and fix the clocks so runs compare.
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close | Out-Null
adb shell setprop debug.oculus.guardian_pause 1
adb shell setprop debug.oculus.cpuLevel 2
adb shell setprop debug.oculus.gpuLevel 2

function Median($values) {
	$sorted = $values | Sort-Object
	if ($sorted.Count -eq 0) { return [double]::NaN }
	return $sorted[[int](($sorted.Count - 1) / 2)]
}

for ($run = 1; $run -le $Runs; $run++) {
	adb shell am force-stop $package
	# The saves folder has to be made by the app: one made by adb belongs to
	# the shell user, and the app can't write to it.
	if (-not (adb shell "[ -d $folder ] && echo yes")) {
		adb shell am start -n $activity | Out-Null
		Start-Sleep -Seconds 8
		adb shell am force-stop $package
	}
	adb shell "echo $Faces $Texel > $folder/bench"
	adb logcat -c
	adb shell am start -n $activity | Out-Null

	$deadline = (Get-Date).AddSeconds(150)
	$log = @()
	while ((Get-Date) -lt $deadline) {
		Start-Sleep -Seconds 3
		$log = adb logcat -d -v brief
		if ($log | Select-String "BENCH saved|Save failed|FATAL EXCEPTION") { break }
	}

	$benchLine = $log | Select-String "BENCH cpu_ms" | Select-Object -Last 1
	$saved = $log | Select-String "BENCH saved|Save failed|FATAL EXCEPTION" | Select-Object -Last 1
	# The runtime prints a VrApi line every second. Use the 30 before the
	# BENCH line, which is the measured window.
	$benchIndex = if ($benchLine) { $benchLine.LineNumber } else { $log.Count }
	$vrapi = $log[0..($benchIndex - 1)] | Select-String "VrApi.*FPS=" | Select-Object -Last 30
	$app = $vrapi | ForEach-Object { if ($_ -match "App=([\d.]+)ms") { [double]$Matches[1] } }
	$total = $vrapi | ForEach-Object { if ($_ -match "CPU&GPU=([\d.]+)ms") { [double]$Matches[1] } }
	$fps = $vrapi | ForEach-Object { if ($_ -match "FPS=(\d+)") { [double]$Matches[1] } }
	$stale = $vrapi | ForEach-Object { if ($_ -match "Stale=(\d+)") { [double]$Matches[1] } }

	Write-Output "run $run"
	Write-Output "  $($benchLine.Line -replace '.*BENCH', 'BENCH')"
	$log | Select-String "BENCH paint memory|BENCH big face" | ForEach-Object { Write-Output "  $($_.Line -creplace "^.*?BENCH", "BENCH")" }
	$sections = $log | Select-String "BENCH sections" | Select-Object -Last 1
	Write-Output "  $($sections.Line -creplace '^.*?BENCH', 'BENCH')"
	Write-Output ("  VrApi over {0} s: fps {1}, GPU App {2} ms, CPU&GPU {3} ms, stale frames/s {4}" -f $vrapi.Count, (Median $fps), (Median $app), (Median $total), (Median $stale))
	Write-Output "  $($saved.Line)"
}

adb shell rm -f "$folder/bench"
adb shell am force-stop $package
adb shell setprop debug.oculus.cpuLevel -1
adb shell setprop debug.oculus.gpuLevel -1
adb shell setprop debug.oculus.guardian_pause 0
