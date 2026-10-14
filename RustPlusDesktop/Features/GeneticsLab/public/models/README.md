# 3D models

The planter and plant models in this folder come from
[Facepunch/RustRelay.Assets](https://github.com/Facepunch/RustRelay.Assets), Facepunch's
library of reference-quality Rust models for web previews and tooling. They are used here
unchanged, for the 3D planting guide, under the
[Facepunch Terms of Service](https://facepunch.com/legal/tos).

| File | Source |
|------|--------|
| `planters/large.glb` | `assets/prefabs/Deployable/Planters/planter.large.deployed.glb` |
| `planters/small.glb` | `assets/prefabs/Deployable/Planters/planter.small.deployed.glb` |
| `planters/triangle.glb` | `assets/prefabs/Deployable/Planters/planter.triangle.deployed.glb` |
| `plants/<plant>.glb` | `assets/bundled/prefabs/autospawn/collectable/<plant>/<plant>-collectable.glb` |
| `plants/berry.glb` | `assets/bundled/prefabs/autospawn/collectable/berry-blue/berry-blue-collectable.glb` |
| `plants/pumpkin/<stage>.glb` | `assets/prefabs/Plants/Pumpkin/pumpkin.skin.<n>.<stage>.glb` |

The library's five berry bushes are identical files apart from the material name (the game
colours the berries in a shader), so one `berry.glb` is shipped and the guide recolours its
berry pixels per variety.

Pumpkin is the only plant the library has growth-stage models for; the guide grows the
other plants by scaling their single (fully grown) model.

The files are Draco-compressed; `public/draco/` holds the decoder from three.js so the
guide works offline.
