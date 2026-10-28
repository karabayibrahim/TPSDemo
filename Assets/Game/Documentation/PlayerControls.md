# Third-person player

The scene's existing `Player` uses `CityThirdPersonController`, a CharacterController, the copied humanoid locomotion controller, and `HumanoidSlopeFootIK`. `Main Camera` follows through `CityThirdPersonCamera`.

- WASD / arrow keys: camera-relative movement
- Shift: run
- Space: jump
- Mouse: orbit camera
- Mouse wheel: zoom
- Escape: release cursor for UI controls
- Right mouse button: capture cursor again
- Gamepad: left stick movement, right stick camera, south button jump, right shoulder or left stick click sprint

Movement and jump settings are on Player's motor. Foot probe range, blending and pelvis correction are on its IK component. The Animator's base layer has IK Pass enabled. IK releases raised/swinging feet and fades out in the air.

`OSMCityCollision` supplies collision meshes for roads/sidewalks/intersections and lightweight building shells. City regeneration refreshes these colliders.

`Tools > Player > Configure Scene Player` can rebuild the scene wiring using the installed Starter Assets humanoid animations. The original Starter Assets controller is preserved; the project's copy is in `Animations/Player`.

Play-mode verification covered flat walking (3.85m in two seconds), jumping (1.09m, airborne IK below 0.01), a 20-degree uphill slope, and an 18-degree cross-slope with independently adjusted feet. No verification failures.
