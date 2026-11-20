# Multiplayer Expansion Rossi

[简体中文](README.md) · **English**

## Mod overview

This multiplayer appearance mod for *White Knuckle* uses Simple Multi Player to let you play as Rossi from *Arknights: Endfield* in multiplayer.

[Release and downloads](https://thunderstore.io/c/white-knuckle/p/AAA_NearFuture/Multiplayer_Expansion_Rossi/)

The project's own code is licensed under MIT. The model is credited to 茶叶味香皂. Rights to the character and original work belong to Hypergryph. [MIT license](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

## How it works

- **Loading and synchronization.** The BepInEx plugin builds the appearance from a local PMX model and its companion assets. It integrates with Simple Multi Player's player lifecycle and lobby menus. A content hash identifies the model. Peers exchange the selection identifier, resolve it against local assets, and fall back if the assets are missing.
- **Motion and interaction.** Actual movement and game state drive animation sampling. The plugin then applies crouching, contact poses, and third-person item grips. Each frame restores the previous grip offsets before calculating the current pose and item position, which prevents offsets from accumulating.
- **Skirt, materials, and hands.** The skirt adjusts to poses at a fixed length, and secondary bone chains add motion. Materials and first-person hand textures are applied independently. Changing appearance or leaving a room restores the original rendering and grab state.

### Building from source

1. Use `.NET SDK 8.0.101` and matching local White Knuckle and BepInEx references.
2. Copy the [configuration example](config/Build.Local.props.example) to `local/Build.Local.props` and set `GameRoot`.
3. Place the following files in `profiles/`.

   | Build | Configuration files |
   |---|---|
   | Release and development | `PhysicsProfile.json`, `AuthoredGrips.json`, `AuthoredAssemblyGrips.json` |
   | Additional file for development | `GazeProfile.json` |

```powershell
./Build.ps1 -Flavor PublicRelease
./Build.ps1 -Flavor Development
```

Build output goes to `artifacts/`. Load one build at a time. Supply the models, textures, animations, ShaderBundle, game and multiplayer DLLs, and embedded profiles locally. [Build script](Build.ps1)

## Features

| Feature | Status in 1.0.0 |
|---|---|
| Avatar selection and synchronization | Select Rossi in the lobby and see the matching custom appearance on peers who have the required assets installed |
| PMX animation and interaction | Motion adaptation, crouching, backward dodge, contact poses, and third-person grips. Backward dodge and body-grab alignment are enabled by default |
| Skirt and materials | Pose-driven skirt adjustments and secondary motion. Advanced materials and scene lighting are enabled by default |
| First-person hand textures and wolf-paw cursor | Independent options, both off by default and on hold for now |

## Roadmap

- Make the character respond to in-game lighting, including lights and flares.
- Fix first-person hand shape and clipping issues.
- Improve cloth elasticity, fine contact, and performance, especially for the skirt, while preserving skirt length and fit.
- Add another selectable character model. Gilberta is the tentative choice.
