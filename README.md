# HFF VRoid — play Human: Fall Flat as your VRM avatar

A mod for the **Steam version of Human: Fall Flat** that adds an **IMPORT VRM** button to the
*Customize Character* menu. Pick any `.vrm` avatar (VRoid Studio, VRoid Hub, Booth, …) and it becomes a
normal entry in **LOAD PRESET → Load My Preset**, with a thumbnail. Load it and your Human is that avatar,
in every level, flopping around with full ragdoll physics.

```
Customize Character            Load My Preset
  EDIT                         [Darth Vader] [Skeleton] [My VRoid] ...
  TOGGLE PLAYER                  LOAD
  SAVE PRESET                    DELETE
  LOAD PRESET                    SUBSCRIBED PRESETS
  IMPORT VRM   <- new            BACK
  BACK
```

## Install (pick one)

### A. One-click installer (recommended)
1. Download **[`release/HFF-VRoid-Installer.exe`](release/HFF-VRoid-Installer.exe)**.
2. Run it. It finds your Steam copy of Human: Fall Flat automatically (or click *Browse…*).
3. Click **Install**, then start the game from Steam.

The installer adds [BepInEx](https://github.com/BepInEx/BepInEx) (the standard Unity mod loader) if you don't have it,
picks the 32- or 64-bit version to match your game, and copies the mod. *Uninstall* removes it again.
Windows SmartScreen may warn because the exe isn't code-signed: click *More info → Run anyway*.

### B. Copy files into the game folder
1. Download **[`release/HFF-VRoid-manual-install.zip`](release/HFF-VRoid-manual-install.zip)**.
2. Steam → right-click *Human: Fall Flat* → *Manage* → *Browse local files*.
3. Extract the zip's contents into that folder (next to `Human.exe`) and start the game.

Already using BepInEx 5 (e.g. *BepInExPack HumanFallFlat* from Thunderstore)? Use
[`release/HFF-VRoid-plugin-only.zip`](release/HFF-VRoid-plugin-only.zip) instead, and make sure
`BepInEx/config/BepInEx.cfg` has `HideManagerGameObject = true`.

## Use
- **Main menu → Customize Character → IMPORT VRM**, then choose a `.vrm` file.
  The *Load My Preset* page opens with your avatar selected — press **LOAD**.
  (If the file window doesn't show up, press **Alt+Tab**: it may be behind the game.)
- Or drop `.vrm` files into `Human Fall Flat/BepInEx/VRoid/Import/` and open *Customize Character*;
  they are imported automatically.
- Remove an avatar like any other preset (*LOAD PRESET → DELETE*).
- Works for both local players (*TOGGLE PLAYER*), in single player and online.

## Multiplayer
The mod is multiplayer-safe: it sends no extra network data and doesn't change physics. The game only shares your saved
character preset (part names, colors, paint), exactly as without the mod.

What others see while you wear a VRM:
- **Players with this mod who imported the same `.vrm`**: your VRM avatar.
- **Everyone else**: your **online look**, a normal preset you choose. Go to *LOAD PRESET*, select any of your normal
  presets and press **SET AS ONLINE LOOK**. It's marked "(online)" in the list, and pressing the button again
  (*CLEAR ONLINE LOOK*) goes back to the plain default Human, which is what they see if none is set.

The online look's parts, colors and paint are copied into each VRM preset (and into the skin you're wearing), so the
game sends that look to others. On your own screen it stays hidden behind the VRM. Changing the online look updates
every VRM preset. If you repaint the tagged preset, the change is picked up next time you open *Customize Character*.

## Settings
After the first launch, edit `BepInEx/config/com.hffvroid.vrmimport.cfg`:

| Setting | Default | What it does |
|---|---|---|
| `SizeMode` | `HeadTop` | `HeadTop` scales every avatar so the top of its head (hair and ears included) is level with the top of a default Human's head, with feet on the ground. `Legs` matches leg length; `Height` matches ankle-to-head-joint height. |
| `ScaleMultiplier` | `1` | Extra size factor. |
| `VerticalOffset` | `0` | Raise (+) or lower (−) the avatar, in metres. |
| `MatchRestPose` | `true` | Bend the VRM's limbs to the game character's rest pose before following it. |
| `SpringBones` | `true` | Hair / skirt / accessory physics from the VRM. |
| `SwapLeftRight` | `false` | Only if limbs move on the wrong side. |
| `ShadeLift` | `0.35` | Brightens shadowed areas to approximate VRM toon shading (0 = plain lighting). |
| `Shaders` | `Standard,…` | Shaders tried for VRM materials. |
| `OnlineLookPreset` | *(empty)* | Set from the game (see Multiplayer). Folder of the preset others see. |
| `DeleteUnusedFiles` | `true` | Deleting a VRM preset also deletes its stored `.vrm` copy when unused. |

## How it works
- **Loader** (`src/HFFVRoid/VrmLoader.cs`): a self-contained glTF 2.0 binary reader. It supports VRM 0.x and VRM 1.0
  humanoids, skinned meshes, textures, alpha modes, double-sided materials, MToon base colours and spring bones.
  No UniVRM dependency, so it works on the game's old Unity 2017 runtime.
- **Presets**: each import creates a normal game preset (`pr:Presets/N`) on the default body. Its description holds a
  marker `[vrm:<sha1>]`, and the `.vrm` itself is stored in `BepInEx/VRoid/Models/<sha1>.vrm` (VRMs are too large for
  the game's Steam Cloud preset storage). The game copies a preset's description into your active skin, so the
  marker follows you into every level. The game's own thumbnail camera photographs the avatar.
- **Sizing**: the loader finds the highest vertex of the VRM's head (skinned rest pose). The top of the default Human's head is measured from the game's own body mesh while it stands in its T-pose on the Customize screen, and saved in `BepInEx/VRoid/hff_head_top.txt` for use in levels. The avatar is scaled so ankle-to-head-top distances match.
- **Avatar** (`VrmAvatar.cs`, `Retargeter.cs`): a hook on `RagdollCustomization.ApplyPreset` hides the game's
  body meshes on any character whose skin carries the marker and spawns the VRM. Every frame, each VRM bone copies
  the rotation of its ragdoll segment (hips, waist, chest, head, arms, forearms, hands, thighs, legs, feet) relative
  to both T-poses. Fingers, neck and toes follow their parents, and spring bones animate hair and clothes.
- **Menu** (`MenuInjector.cs`): clones the game's own *LOAD PRESET* button, so the new button looks native and works
  with keyboard, mouse and controller navigation.

## Limitations
- VRM facial expressions, blinking and look-at aren't animated. MToon is approximated with Unity's Standard shader
  (no outlines).
- The game's physics body is unchanged. A tall, thin avatar still collides like the chunky Human.
- Uploading a VRM preset to the Steam Workshop only uploads the default body.

## Building
Requires the .NET SDK 8+, `curl` and `zip`/`unzip` (Linux, macOS or Windows with Git Bash).

1. Copy the game's assemblies into `lib/game/` (they are copyrighted and are not in this repo):
   `Human Fall Flat/Human_Data/Managed/Assembly-CSharp.dll` and `UnityEngine*.dll`
   (plus `HumanAPI.dll` if it exists).
2. `./tools/build.sh` builds the plugin and installer into `release/`.
3. `./tools/test.sh` runs the automated tests:
   - **LoaderTest**: runs the real loader and retargeting code on public VRM 0.x / 1.0 samples, using a tiny Unity
     stand-in. It checks bind poses against the skeleton, mesh and weight ranges, humanoid bones, spring-bone
     stability, and that a VRM follows a posed and fallen ragdoll skeleton (with random segment axes) within a few
     degrees, feet on the ground.
   - **InstallerTest**: install, reinstall, config patching and uninstall against fake 32/64-bit game folders.

Project layout:
```
src/HFFVRoid/     BepInEx plugin (net46, Unity 2017 / Mono)
src/Installer/    Windows installer (net472 WinForms, BepInEx + plugin embedded)
tests/            LoaderTest (VRM loader + retargeting), InstallerTest
tools/            build.sh, test.sh, files for the manual zip
release/          ready-to-use installer and zips
```

## License
MIT for this mod (see `LICENSE`). Bundled BepInEx is LGPL-2.1 (see `THIRD-PARTY-NOTICES.md`).
Unofficial fan project, not affiliated with No Brakes Games or Curve Games.
Respect the license of the avatars you import.
