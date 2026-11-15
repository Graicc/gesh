# Building a store APK

How to build the APK that gets uploaded to the Meta Horizon Store. What's
still needed for the listing itself is in `plans/publishing.md`.

## Once: make the upload key

The store only accepts an APK signed with your own key, not the Android debug
key that `just play` and `just apk` use. Every update must be signed with the
same key, so losing it (or its password) means the store app can never be
updated again. Keep it outside the repo and back it up somewhere safe, along
with the password. It's at `~/OneDrive/gesh.keystore` (alias `gesh`).

```
keytool -genkeypair -v -keystore ~/OneDrive/gesh.keystore -alias gesh -keyalg RSA -keysize 2048 -validity 10000
```

`keytool` comes with the JDK (it's on the path in a Visual Studio developer
shell, or under `C:\Program Files\Microsoft\jdk-*\bin`). It asks for a
password and a name; use one password for both the store and the key.

## Each build

1. Bump `ApplicationVersion` in `Projects/Android/gesh.Android.csproj` (a
   whole number; the store refuses an upload that doesn't go up). If the
   version people see changes, edit `version.props` too.
2. In PowerShell, point the build at the key and give it the password for
   this session only. Don't put the password in a file in the repo, a
   script, or your shell history:

   ```
   $env:GESH_KEYSTORE = "$HOME\OneDrive\gesh.keystore"
   $env:GESH_KEY_PASS = Read-Host "Keystore password" -MaskInput
   just apk-store
   ```

   `just apk-store` stops with a message if either variable is missing.
3. Upload `dist-store/com.graic.gesh-Signed.apk`. The other `.apk` in that
   folder is unsigned; ignore it.

## Checking the APK before uploading

With the Android SDK build tools (`$env:LOCALAPPDATA\Android\Sdk\build-tools\<version>`):

```
aapt dump badging dist-store/com.graic.gesh-Signed.apk
apksigner verify --print-certs -v dist-store/com.graic.gesh-Signed.apk
```

Expect:

- `package: name='com.graic.gesh' versionCode='<the new number>' versionName='0.1'`
- `targetSdkVersion:'32'`, `native-code: 'arm64-v8a'`
- Permissions: only the two OpenXR ones, `HAND_TRACKING` and `USE_SCENE`.
- `Verified using v3 scheme ... true`, and the certificate DN is your
  name, not `CN=Android Debug`. v2 shows false: with a minimum SDK of 29
  apksigner only writes v3, which every Quest checks.
- If the certificate is still `CN=Android Debug`, an old debug signed APK was
  reused; `just apk-store` deletes those first, so this means the build ran
  some other way.

The upload checks the manifest too. These are required and already set;
keep them if the manifest changes:

- `android:installLocation="auto"` on `<manifest>` (`AndroidManifest.xml`).
- `android.hardware.vulkan.version` declared (`0x401000`, Vulkan 1.1, what
  sk_renderer asks for).
- `android:allowBackup="false"` (not required, but the store flags it as a
  security warning otherwise; the saves are in the external files folder,
  which Android backup doesn't cover anyway).
- On the activity (attributes in `Platforms/Android/MainActivity.cs`):
  landscape `screenOrientation`, and the `com.oculus.vr.focusaware` meta-data
  set to true.

A failed check doesn't use up the version code; upload again with the same
one.

Meta's `ovr-platform-util` can also check and upload it (`ovr-platform-util
upload-quest-build --app-id <id> --app-secret <secret> --apk <apk> --channel
ALPHA`); the app id and secret are on the app's page in the developer
dashboard. Uploading through the dashboard in the browser works too.

## Installing the store build on your own headset

Your headset has the debug-signed build. Android won't replace an app with
one signed by a different key, so installing the store build means a full
uninstall first, which deletes the saves. Run `just pull` first to copy them
to `./saves`, then:

```
adb uninstall com.graic.gesh
adb install dist-store/com.graic.gesh-Signed.apk
```

and push the saves back with `adb push saves/. /sdcard/Android/data/com.graic.gesh/files/gesh/`.
After that `just play` won't install over it (debug key again) until you
uninstall once more, so for day to day development stay on the debug build,
and only install the store build to check it.

## Store art

`store/icon_1024.png` and `store/icon_512.png` are the logo on a transparent
background, rendered from `logo.glb` with the save thumbnail's camera and a
narrower field of view. The app's own launcher icon in
`Platforms/Android/Resources/mipmap-*` is the same render.

The cover art (`cover_landscape_2560x1440.png`, `cover_square_1440x1440.png`,
`cover_portrait_1008x1440.png`, `hero_3000x900.png`) is the fox
(`saves/2026-11-14/fox2.glb`) on a blue gradient in the accent color. The
fox alone on a transparent background, for laying out other art, is
`fox_side_transparent.png` and `fox_front_transparent.png`. Check the sizes
against the dashboard's asset page before uploading; Meta changes them now
and then.
