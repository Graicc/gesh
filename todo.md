# TODO

Check off todos when done.
Feel free to tackle the todos in whatever order seems logical.
Try not to overcomplicated logic. state should have one clear source of truth. in general, avoid unnecessary state. prefer to pass function arguments over global state.
Invalid logic should be flagged (throw, fail the fuzzer) so it gets fixed, instead of being silently paved over. Only handle bad input explicitly when it can legitimately happen (tracking glitches, degenerate geometry, the other hand removing a held part).

- [x] rename two and three finger pinch to index and middle pinch, since thats what they are
- [x] when merging a vertex, if two faces that would end up the same are facing in different directions, delete the face instead of keeping the existing one.
- [x] when pulling out a edge into a face, orient the face to face the camera.
- [x] when looking at a palm with the hands spread out (palm with hands curled wouldn't do this), spawn a menu in front of the hand that you can interact with with the other hand.
- [x] add a global settings object thats passed into the hands.
- [x] add the ability to pan the world around by pinching the air. two fingers pinching the air should let you rotate and zoom as well. The mesh and hand grab logic shouldn't know about this, but update the getclosest method to keep the user irl distance the same. this should probably use the global settings object
- [x] it seems like when creating a new vertex it won't do the merge behavior.
- [x] when merging a toRemove vertex that only has one face, that face should replace any face that it would normally cancel out with from target.
- [x] The line and vertex size should scale with the world
- [x] add sliders to adjust the hand flatness threshold to open the menu, the pinch threshold
  - [x] add a toggle to use an expiremental pinch mode where the pinch releases when you get 0.01 from the closest distance so far. like if you pinched to distance 0.005, it'd release when you get to 0.015. add a slider to control the 0.01 variable (release delta distance).  
- [x] the bottom of the menu should be at the bottom of the palm, instead of the top of the menu being at the top of the palm.
- [x] I should be able to grab the menu off of the palm and place it in the world. have an X in the top right corner to get rid of it.
- [x] Increase the texture resolution for painting. right now the pixels are really obvious. anti-aliasing on painting would help too, so that the underlying texture resolution is better utilized. maybe have a slider to adjust resolution, maybe scale resolution based on surface area, idk. Try to get a compromise between performance and quality. though this is the only real texture used by the app, so maybe you can get away with making it huge. measure, don't guess.
- [x] the new lines have some weird clipping with the vertex spheres. theres also some weird stereo effects, like the line in each eye is rotated differently on the end. worst case these problems should be solvable by just making the spheres black too (which, now that I think about it, is probably what I want anyways, but still look for the underlying problem because it may come up somewhere else).
- [x] default to new gripping mode. put settings like that that wont ship in their own experimental tab so it'll be easier to hide it when we go to release.
- [x] set the palm open setting (which should also be in experimental) to 0.75 by default.
- [x] fill paint mode should let you paint as many faces as you want while pinching. its indicator should be bigger and a different shape (e.g. a box) so it's obviously different from the brush.
- [x] add readme file with setup instructions, how to run in headset, how to create a final apk
- [x] the scale indicator when moving the world should have a pill background, like the existing ui
- [x] double tapping the middle pinch should create a new vertex at that location (that isn't attached to anything). this is how you can bootstrap an empty scene.
- [x] try making the position held also relative to the hand root position, like how rotation is from the whole hand rotation not just the finger rotation.
- [x] is there a way to use the native meta hand tracking mesh instead of the stereokit one? (yes, planned in plans/hand-mesh.md)
- [x] I think updating deletes saved files? make it not do that ideally
- [x] can you get all the publishing data you need from me? I see that the apk name is companyname etc. (questions in plans/publishing.md)
- [x] can you present me screenshots of all the ui views so I can give you feedback on each of them and change the copy (the text). ideally with a way to unambigiously reference the parts. this should include all text in the app (including tutorial etc)
- [x] copy the fox model from downloads back to the app

menu improvements:
- [x] tilt the menu back a little instead of having it be totally vertical. it should be a comfortable reading angle.
- [x] improve the ui. make the UI something you're proud of. this will probably require edits to stereokit itself, don't be afraid to change things where it improves the UX. some changes you'll def need:
  - [x] there should be real tabs on the bottom, maybe floating below the main region, put the x in the corner down there too
  - [x] the region should be a lot more rectangular, right now its wayyyy to tall, and its a little too wide. think like oversized sheet of paper, maybe a3 ish size. we don't want it to be cumbersome to look at.
  - [x] have a real color picker. like show the circle with the colors in it and have the sliders show color preview on them. add a color picker to to pick a color from the texture
  - [x] the presets should be a button of the solid color that they represent
  - [x] save menu should be a scrollable list
  - [x] When functionality is obvious, don't add text. in general, user facing UI's generated by ai have too much text, and especially too much filler text that doesn't really make sense to be there.
  - [x] maybe use icons in the tab bar, e.g. a save icon for files, gear for settings, paint brush for painting, hammer for editing. don't limit the organization to the current structure, there are better ones (like the tools being tabs).
  - [x] credits tab for libraries and licenses etc
  - [x] in general, just think empathetically about the UX. think about how a suer would accomplish various actions, and aim to make them as easy and intuitive as possible.
- [x] make hover highlight highlight the whole edge or face, not just a sphere.
- [ ] I get a Can't read the saves folder error. make sure the permissions dialogue will show up (not a permission: the folder was created over adb, so the app didn't own it. It's deleted; the app makes its own on next launch. Then copy the fox back.)
- [ ] turn the path after Models saved to into a link that opens the files app (the Quest's Files app can't see an app's Android/data folder; needs the saves moved to a shared folder, see the chat)

gesh UI review
- [x] swap the locations of undo/redo and home
- [x] get rid of the background for undo/redo, home, and the tool selection
- [x] 9 should read: Pinch to grab. the icon should be two arrows pointing inwards
- [x] 10 should read: Middle pinch to pull out a new part
- [x] 11 should read: Middle pinch twice to add a vertex
- [x] 12: Pinch the air to move
- [x] 13: get rid of the icon
- [x] remove 14-18
- [x] add a delete button (icon) to the left of 20 when there is a selection
- [x] remove 21-23.
- [x] vertically align to center: 24, 25, 26
- [x] center 28 horizontally below 27
- [x] make all the padding between 24-28 symmetric
- [x] 20/30, the selection icon should be a highlight around the square, not a circle
- [x] same thing for the fill tool obv
- [x] leave 31 as a dot when using the pipette its fine
- [x] make sure 32 is always floating to the right top aligned of the ui, like the home button is (but at the top instead of the bottom)
- [x] 34 should have all 6 recent colors always
- [x] 38: too long for field, figure out a way to give it more space. maybe flip the layout so the details about the current file are in a top panel instead of a left panel?
- [x] 42,43,44 should be aligned with the bottom of the panel
- [x] make sure the list of files scrolls vertically when theres too many
- [x] 47 etc: instead of having a double press, spawn a modal with "are you sure you want to delete <filename>?"
  - [x] This should be the case anywhere you have the double press to confirm. e.g. saving a file that exists. M11 should say "Are you sure you want to open <file>? Unsaved changes are lost". M9 should say "Are you sure you want to make a new file? Unsaved changes are lost"
  - [x] I don't think you should have text under the file name change at all. if you need to give feedback to user (i.e. an error), show another modal. if its telling the user something they already know (e.g. saved file) you don't need to do anything
- [x] 49 should say: No saved files
- [x] 50 remove icon
- [x] 51 remove
- [x] 53 move to expiremental
- [x] 61: should be Gesh v0.1 (as that is the current version)
- [x] 61 below should read `made with <3 by Graic` Graic is bold and a hyperlink to graic.net
- [x] 62: put this in a modal called "copyright notices". just make it a big blob of scrollable monospace text with the full license notices (e.g. there should be a bunch of copies of the MIT license with the copyright notices in there). it'll be ugly but I want it to legally comply. whats up with "zlib" under the license column?
- [x] 63: move to the bottom center of the list of current models. should read "Models saved to <folder>"
- [x] 64: should read "pinch a corner to grab it" and use the same icon change as 9
- [x] 65: should read "middle pinch an edge to pull out a face"
- [x] 66: should read "look at your palm to open the menu"
- [x] theres a drop shadow on the tool bar that goes onto the air (it also clips things behind it)
- [x] make scrollbara also let you touch the region to scroll (e.g. on credits view)
- [x] look at the updated home screen, the layout has clipping. it should be picture top left, file name the rest of the top right, save in the very top right, new below it
- [x] play tutorial text should be left aligned. it should also respawn the origional mesh (and do the unsaved progress modal)
- [x] credits view should be right below the made with <3
- [x] capitalize Made in made with <3
- [x] Gesh v0.1 should be larger
- [x] The X button still clips into the right side of the panel. it needs to move a little right and down
- [x] inconsistent X button on modals and panels
- [x] the tutorial popups should rotate to face straight at the user (in pitch and yaw)
- [x] Pressing home when home is open should close it
- [x] in the home panel, the home button should be a file icon

stereokit changes:

- [x] big refactor: build against our own fork of stereokit instead of using the library. ideally do a monorepo so that I don't have to keep two repos up to date. maybe a submodule? idk.
- [x] use the most up to date branch for stereokit, you might have to update our code a little to work with it.
- [x] Right now lines clip the mesh, because the quad faces the camera so sometimes half of it ends up inside the mesh. get rid of the clipping. ideally lines would render like a cylinder instead of a quad, but make sure whatever you do is performant. maybe theres a shader trick you can do. maybe theres a better way to show edges entirely. idk your call.
- [x] idk if this would need stereokit changes, but make the mesh shader render backfaces as transparency with red grid pattern on it, so you can still see backfaces but its obvious they aren't going to be visible in exported meshes.

after all that, plan design for these, but don't implement. write plans to separate files to review:

- [x] save feature: you should be able to save files. They should have a thumbnail that is what the pov was when you saved. this should use standard mesh format to be able to import into other programs. make sure its easy to get off of the quest. this should live as a tab on the palm menu. you should be able to see/set the name of the file and browse existing files.
- [x] paint faces feature. allow painting on each face with brush tools. there should be a paint brush with scale size, when painting the color should appear on a sphere on your index finger. the overlap of the sphere with the mesh should do the painting. a fill tool should be available that paints a whole face at a time. This should live as a tab on the palm menu. make sure this texture information can be stored in the mesh file.
- [x] multi-select: a way to select multiple mesh parts and move them together.
- [x] implement multi-select (plans/multiselect.md)
- [x] implement undo and redo, including painting (plans/undo.md)