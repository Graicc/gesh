# Publishing

Status: 1 to 3, 5 and the cleanups are done: the id is `com.graic.gesh`,
the name Gesh, and the version 0.1 (in `version.props`). The manifest is
Quest only (Quest 2, 3, 3S, Pro) and asks only for hand tracking and the
scene, with hand tracking required. The icon is the G logo (`store/`). The
store build is `just apk-store`, see `notes/publishing.md`; it needs the
signing key (4). Still open: 4 and 6 to 11.

## What I need from you

Goes in the build:

1. **Application id**, e.g. `com.yourname.gesh`. Reverse domain style,
   lowercase. It can never change after the first store upload, and changing
   it now makes the headset treat it as a new app (the old install, and its
   saves, stay under the old id; `just pull` the saves first).
2. **App name** shown in the headset's library. Now "gesh" (the
   `app_name` string).
3. **Version**: the store needs `ApplicationVersion` (a whole number) to go
   up on each upload. `ApplicationDisplayVersion` is what people see; now
   `1.0`. Is the first release 1.0, or 0.1?
4. **Signing key**: make one yourself (README, "A final APK") and keep it and
   its password out of the repo and out of chat. Losing it means you can't
   update the store app. Changing from the debug key to it also means one
   full uninstall on your headset, which deletes the saves: `just pull`
   first.
5. **App icon**: now the template's icon. A square image (1024x1024 is
   plenty) and I'll make the Android sizes.
6. **License** for the code, for the credits page and the repo: MIT, or
   closed?

Goes in the store listing (Meta Horizon Store, or App Lab):

7. Developer or organization name, and a support email or URL.
8. Short and long description.
9. A privacy policy URL. The store requires one even for an app that sends
   nothing anywhere (gesh doesn't use the network).
10. Price, and the category (probably Art & Creativity or Productivity).
11. Store art: cover images in Meta's fixed sizes, plus screenshots and a
    trailer. I can take in-app screenshots from the harness; the cover art is
    yours.

## Things to clean up before the first upload (I can do these)

- The manifest still has the StereoKit template's permissions:
  `RECORD_AUDIO`, eye, face and body tracking, and others gesh doesn't use.
  Store review asks why each one is there, and some show the user a
  permission prompt. Keep hand tracking, passthrough and scene; drop the
  rest.
- Drop the other vendors' manifest entries (Pico, Vive, Snapdragon,
  Android XR) if it's Quest only.
- `supportedDevices` lists the Quest 1, which the store no longer accepts
  new apps for.


## Answers

1. com.graic.gesh
2. Gesh
3. Current version is v0.1
do those cleanups. its quest only