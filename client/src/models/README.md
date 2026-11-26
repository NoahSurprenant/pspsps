# Models

## cat.glb

The cat every player is: Kenney's "animal-cat" from the [Cube Pets](https://kenney.nl/assets/cube-pets) pack
(version 2.0), by Kenney (www.kenney.nl), released under
[Creative Commons Zero (CC0 1.0)](https://creativecommons.org/publicdomain/zero/1.0/): free to use, change and
share, no credit required (we credit Kenney anyway). The licence is stated on the pack's page and in the
`License.txt` that comes with it.

`client/scripts/cat-model.mjs` makes this file from the pack's `Models/GLB format/animal-cat.glb`:

```sh
cd client
node scripts/cat-model.mjs "<Cube Pets>/Models/GLB format/animal-cat.glb" src/models/cat.glb
```

It drops the texture (the game paints flat colours: each cat's fur, `src/fur.ts`), splits the model into one mesh per
colour, named `<part>.<colour>` (`coat`, `accent`, `eyes`, `pupils`, `dark`), keeps Kenney's `idle` and `walk`
clips, and adds `sit`, `sleep`, `wave` and `meow`. About 65 KB (12 KB gzipped); it loads on the way into the first
room, never on the sign-in screen or the world list.

## townHouse.json

The "town-house" mesh (`src/meshes.ts`, drawn by a prop design's `model` part): Kenney's "building-small-a" from the
[City Builder starter kit](https://github.com/KenneyNL/Starter-Kit-City-Builder), by Kenney (www.kenney.nl), whose
models are Creative Commons Zero (CC0 1.0). `client/scripts/mesh-model.mjs` makes it: each vertex takes the colour of
the colour-map texel it points at, and the game draws it flat and outlined like its other parts.

```sh
cd client
node scripts/mesh-model.mjs "<starter kit>/models/building-small-a.glb" src/models/townHouse.json
```

## townHotel.json, townMall.json

The "town-hotel" and "town-mall" meshes, from the same starter kit as the town house: Kenney's "building-small-c" and
"building-garage" (CC0), made the same way with `scripts/mesh-model.mjs`.

## townSquareCoffee.json

The "town-square-coffee" mesh: the Town Square's coffee shop front, modelled for this game in Blender (our own work, no
outside assets). The scene is a Python script, `client/scripts/blender/townSquareCoffee.py`, built on
`client/scripts/blender/lib.py`, which also writes the mesh (a colour for every vertex, from each material). With
the `bpy` module in a container:

```sh
pip install bpy   # Python 3.11
python townSquareCoffee.py   # writes /work/out/coffee.json: copy it to src/models/townSquareCoffee.json
```

The front is -y in Blender (-z in the game, toward the camera); the shop is 6 m wide and 1.4 m deep, origin at the
middle of its base.
