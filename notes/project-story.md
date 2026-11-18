# About the project

## What it does

Gesh is a hands-first 3D modeling app based on directly constructing meshes one triangle at a time. The app gives users control over the placement of each vertex, edge, and face. Users can also texture their models with built-in painting tools and models can be exported to share or to use in other programs, which makes Gesh fit into a model creation workflow.

## Inspiration

When using existing VR 3D modeling apps, it always felt like I was sticking shapes together to approximate what I wanted. I wanted a tool that could more directly express the vision I had in my head.

When using flatscreen 3D modeling apps, I wanted more fine-grained control of the topology. The modeling flow in Blender pushes you to start with a predefined topology and move parts around to reach the desired shape. Existing flatscreen programs can construct a mesh from individual triangles, but none of them make it quick to create geometry, as you have to judge 3D positioning from a 2D screen.

## How we built it

Gesh was built using [StereoKit](https://stereokit.net/), a C# framework for creating XR applications. By using a lightweight framework instead of a complete game engine such as Unreal or Godot, I was able to iterate much faster: the whole app could be built and run natively on a headset in under 10 seconds. It also made it easier to optimize for performance: Gesh easily runs at 120 Hz on a Quest 3.

## Challenges we ran into

StereoKit's existing line renderer drew lines that would clip inside the mesh instead of appearing on the outside. To fix this, I had to fork StereoKit to add my own line rendering logic.

Explaining the editing gestures to users turned out to be a struggle. To remedy this, I added an animated tutorial teaching the user how to perform each action. This simplified onboarding and allowed users to build an intuition for how the software works much faster.

## Accomplishments that we're proud of

I am most proud of achieving an editing experience that uses the expressiveness of VR to form a deeper connection to the model. I am proud to have built a tool that lets me reach a flow state while editing.

## What we learned

1. Hands can offer input methods that controllers can't replicate.
2. Passthrough can offer unique advantages in creative apps (e.g. being able to look at reference material on a computer in front of you).
3. Don't be afraid to dive into the internals of a system.

## What's next for Gesh

I plan on polishing the app more and releasing it on the Meta Horizon Store. I'd love to add collaborative multiplayer editing as well.



* What Meta VR capability(ies) did you use in your project?

Hand tracking: Hand tracking is a first-class citizen in Gesh. I use Meta's hand tracking mesh to achieve the most immersive hand presence.

Passthrough: Passthrough is an optional feature, but it can be very helpful for judging the scale of models or looking at reference material.

* Describe how Hand Interactions are implemented into your experience.

Hand tracking is the primary interaction method; controllers are not supported. The UI is used by poking it with the index finger, and pinches with the index or middle finger are used to edit the model. The index finger is used to grab existing parts of the model, while the middle finger is used to create new edges or faces.

* What makes you excited about the future of VR? (Suggested length 150 words or less)

VR opens up new ways to express yourself as an artist.
