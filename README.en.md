# Multiplayer Expansion Rossi

[简体中文](README.md) · **English**

## Mod overview

This multiplayer appearance mod for *White Knuckle* brings Rossi from *Arknights: Endfield* into sessions that use Simple Multi Player. Version 1.0.0 is distributed on Thunderstore and depends on BepInExPack 5.4.2305 and Simple Multi Player 1.8.2. [Release and downloads](https://thunderstore.io/c/white-knuckle/p/AAA_NearFuture/Multiplayer_Expansion_Rossi/)

1. Search for **Multiplayer Expansion Rossi** in r2modman or Gale and install it.
2. Open **Multi Play** from the game's main menu. Open **AVATAR** in the top-right corner, select **Rossi**, and click **APPLY**. Use **DETAILS** to adjust the selected appearance.
3. Ask friends who want to see the Rossi appearance to install the mod and its dependencies too.

CllIEBE's own code is licensed under MIT. The model is credited to 茶叶味香皂. Rights to the character and original work belong to Hypergryph. [MIT license](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

## How it works

- **Loading and synchronization.** The BepInEx plugin builds the appearance from a local PMX model and its companion assets. It hooks into the Simple Multi Player lobby and player lifecycle. A content hash identifies the model. Peers exchange the selection identifier, resolve it against their local assets, and fall back if the assets are missing.
- **Motion and interaction.** Game state and actual movement drive animation sampling. The plugin then applies crouch, contact poses, and third-person item grips. Each frame restores the previous grip offsets before sampling the next pose and positioning the held item, which prevents offsets from accumulating.
- **Cloth, materials, and hands.** The skirt adapts to poses at a fixed length, with secondary bone chains adding motion. Materials and first-person hand overlays are applied separately. The plugin restores the original rendering and grab state when changing appearance or leaving a room.

### Building from source

Use `.NET SDK 8.0.101` and matching local White Knuckle and BepInEx references. Copy the [configuration example](config/Build.Local.props.example) to `local/Build.Local.props` and set `GameRoot`. Supply `PhysicsProfile.json`, `AuthoredGrips.json`, and `AuthoredAssemblyGrips.json` in `profiles/`. The development build also needs `GazeProfile.json`.

```powershell
./Build.ps1 -Flavor PublicRelease
./Build.ps1 -Flavor Development
```

Build output goes to `artifacts/`. Load one build at a time. Supply the models, textures, motions, ShaderBundle, game and multiplayer DLLs, and embedded profiles locally. [Build script](Build.ps1)

## Features

| Feature | Status in 1.0.0 |
|---|---|
| Avatar selection and synchronization | Choose Rossi in the lobby and see the appearance of peers with the matching assets installed |
| PMX animation and interaction | Animation adaptation, crouching, backward dodge, contact poses, and third-person grips. Backward dodge and body-grab alignment are enabled by default |
| Cloth and materials | Pose-driven skirt adjustments and secondary motion. Advanced r7 materials and the scene-lighting option are enabled by default. The first release retains the current character lighting, with ambient-light response still to be improved |
| First-person hand textures and wolf-paw cursor | Separate options that are off by default and still in development |
| Development build | Includes the F8 debug controls and automatic eye-gaze work |

Rossi's motion, cloth, and grip settings are tuned for this model. Other PMX models require separate adaptation.

## Roadmap

- Improve ambient-light response using the launch appearance as a reference.
- Fix first-person hand shape and clipping issues while preserving the established style.
- Improve cloth elasticity, fine contact, and performance while preserving skirt length and fit.
- Test automatic eye gaze in-game before deciding whether to include it in the release build.
- Run compatibility regressions for joining and leaving rooms, switching appearances, and updates to the game or multiplayer dependencies.
- Parameterize the local model import and asset-reproduction tools and review each tool before adding it to the repository.
