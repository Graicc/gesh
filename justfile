set windows-shell := ["pwsh","-c"]

alias r := run
alias p := play

# StereoKit is built from source in ./stereokit (a git subtree of StereoKit's
# develop branch). Desktop builds need Visual Studio 2022 C++ tools and CMake.
# Android builds additionally need ninja, Android NDK r25c in the NDK env var,
# and the android-32 SDK platform, see stereokit/BUILDING.md.

run:
	dotnet run

# Build, install and run on the connected headset. Release, since Debug
# measured about 4x slower per frame on the headset (plans/perf-log.md).
play: (_android "Release")

# Same as play, but a Debug build, for attaching a debugger.
play-debug: (_android "Debug")

# Android needs a native host compiler for StereoKit's shader compiler, so this
# runs inside a Visual Studio developer shell.
_android config: (_vs "dotnet build ./Projects/Android/gesh.Android.csproj -c " + config + " -t:Run")

# Build a release APK into ./dist, to install with `adb install` or share.
# Signed with the debug key; for the store build see apk-store.
apk: (_vs "dotnet publish ./Projects/Android/gesh.Android.csproj -c Release -p:AndroidPackageFormat=apk -o ./dist")

# Build the store APK into ./dist-store, signed with the upload key, see
# notes/publishing.md. The key and its password come from the GESH_KEYSTORE
# and GESH_KEY_PASS environment variables and never touch the repo.
apk-store: _check-key _unsign (_vs "dotnet publish ./Projects/Android/gesh.Android.csproj -c Release -p:AndroidPackageFormat=apk -o ./dist-store -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=$env:GESH_KEYSTORE -p:AndroidSigningKeyAlias=gesh -p:AndroidSigningKeyPass=env:GESH_KEY_PASS -p:AndroidSigningStorePass=env:GESH_KEY_PASS")

# Signing is incremental and doesn't notice a different key, so an APK
# signed earlier (with the debug key, by apk or play) would be reused.
_unsign:
	Get-ChildItem ./Projects/Android/bin/Release/*/*-Signed.apk* | Remove-Item; if (Test-Path ./dist-store) { Remove-Item ./dist-store -Recurse }

_check-key:
	if (-not $env:GESH_KEYSTORE -or -not (Test-Path $env:GESH_KEYSTORE)) { throw "GESH_KEYSTORE must point at the upload keystore, see notes/publishing.md" }; if (-not $env:GESH_KEY_PASS) { throw "GESH_KEY_PASS must hold the keystore password, see notes/publishing.md" }

# Runs a command inside a Visual Studio developer shell.
_vs command:
	$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath; Import-Module "$vs\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"; Enter-VsDevShell -VsInstallPath $vs -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null; Remove-Item Env:Platform -ErrorAction Ignore; {{command}}

# Pull the latest StereoKit develop into ./stereokit
sk-pull:
	git subtree pull --prefix=stereokit https://github.com/StereoKit/StereoKit.git develop --squash

# Measure performance on the connected headset with a scripted scene, see
# plans/perf.md. Pass a run count and face count, e.g. `just bench 3 3000`.
bench runs="1" faces="1000":
	./scripts/bench.ps1 -Runs {{runs}} -Faces {{faces}}

# Copy the saves off the headset into ./saves
pull:
	adb pull /sdcard/Android/data/com.graic.gesh/files/gesh ./saves
