// Makes client/public/models/cat.glb from Kenney's Cube Pets cat (CC0; see client/public/models/README.md):
//
//   node scripts/cat-model.mjs "<Cube Pets>/Models/GLB format/animal-cat.glb" public/models/cat.glb
//
// Run it again only to change the model; the game loads the result, never the original. What it does:
// - Drops the colour-map texture, UVs and tangents (the game draws flat colours) and splits every part into one mesh
//   per colour, named "<part>.<colour>" (coat, accent, eyes, pupils, dark), so the game can paint each cat's fur on
//   them.
// - Keeps the idle and walk clips, and adds sit, sleep, wave and meow, keyed on the same nodes.
import { readFileSync, writeFileSync } from "node:fs";

const [input, output] = process.argv.slice(2);
if (!input || !output) {
  throw new Error("usage: node scripts/cat-model.mjs <animal-cat.glb> <cat.glb>");
}

const source = readGlb(readFileSync(input));
const gltf = source.json;

// The colour map's cells (16 across, 4 down) the cat's faces use, by what they are on the cat.
const cellRoles = { "13,3": "coat", "1,2": "accent", "15,2": "eyes", "15,3": "dark" };
// Parts painted in the accent colour all over, apart from their dark soles: the tail and the legs (the coat's "paws").
const accentParts = new Set(["tail", "leg-back-left", "leg-back-right", "leg-front-left", "leg-front-right"]);
// Kenney's "Group" is the mouth.
const partName = (name) => (name === "Group" ? "mouth" : name);

const out = { nodes: [], meshes: [], accessors: [], bufferViews: [], animations: [] };
const chunks = [];
let byteLength = 0;

function addView(typed, target) {
  const bytes = new Uint8Array(typed.buffer, typed.byteOffset, typed.byteLength);
  const padding = (4 - (byteLength % 4)) % 4;
  if (padding > 0) {
    chunks.push(new Uint8Array(padding));
    byteLength += padding;
  }
  out.bufferViews.push({ buffer: 0, byteOffset: byteLength, byteLength: bytes.byteLength, ...(target ? { target } : {}) });
  chunks.push(bytes);
  byteLength += bytes.byteLength;
  return out.bufferViews.length - 1;
}

// Accessors by their contents: the clips share their key times and their held values.
const accessorsSeen = new Map();

function addAccessor(typed, type, target, withBounds) {
  const seenKey = `${type} ${target} ${withBounds} ${typed.constructor.name} ${typed.join(",")}`;
  if (accessorsSeen.has(seenKey)) {
    return accessorsSeen.get(seenKey);
  }
  accessorsSeen.set(seenKey, out.accessors.length);
  const size = { SCALAR: 1, VEC3: 3, VEC4: 4 }[type];
  const componentType = typed instanceof Float32Array ? 5126 : 5123;
  const accessor = { bufferView: addView(typed, target), componentType, count: typed.length / size, type };
  if (withBounds) {
    accessor.min = [...Array(size)].map((_, k) => Math.min(...typed.filter((_, i) => i % size === k)));
    accessor.max = [...Array(size)].map((_, k) => Math.max(...typed.filter((_, i) => i % size === k)));
  }
  out.accessors.push(accessor);
  return out.accessors.length - 1;
}

// The face (eyes, pupils, mouth) and the insides of the ears lie on the front of the head, nearer than the dark
// outline the game draws around the coat (renderOutline: the coat pushed out along its normals by about 0.033 here,
// whose depth hides what's behind it): they come forward to clear it.
const faceForward = 0.045;
function forward(part, role) {
  return (part === "body" && role !== "coat") || part === "mouth" ? faceForward : 0;
}

// The meshes, one per part and colour.
const oldToNew = new Map();
const firstPass = gltf.nodes.map((node) => ({ ...node, name: partName(node.name) }));
for (const [index, node] of firstPass.entries()) {
  const copy = { name: node.name };
  for (const key of ["translation", "rotation", "scale"]) {
    if (node[key]) {
      copy[key] = node[key];
    }
  }
  out.nodes.push(copy);
  oldToNew.set(index, out.nodes.length - 1);
}
for (const [index, node] of firstPass.entries()) {
  const copy = out.nodes[oldToNew.get(index)];
  const children = (node.children ?? []).map((child) => oldToNew.get(child));
  if (node.mesh !== undefined) {
    for (const [role, primitive] of splitByRole(gltf.meshes[node.mesh], node.name)) {
      out.meshes.push({ name: `${node.name}.${role}`, primitives: [primitive] });
      out.nodes.push({ name: `${node.name}.${role}`, mesh: out.meshes.length - 1 });
      children.push(out.nodes.length - 1);
    }
  }
  if (children.length > 0) {
    copy.children = children;
  }
}

function splitByRole(mesh, part) {
  const primitive = mesh.primitives[0];
  const position = source.read(primitive.attributes.POSITION);
  const normal = source.read(primitive.attributes.NORMAL);
  const uv = source.read(primitive.attributes.TEXCOORD_0);
  const indices = source.read(primitive.indices);
  const byRole = new Map();
  for (let t = 0; t < indices.length; t += 3) {
    const corners = [indices[t], indices[t + 1], indices[t + 2]];
    const u = corners.reduce((sum, i) => sum + uv[i * 2], 0) / 3;
    const v = corners.reduce((sum, i) => sum + uv[i * 2 + 1], 0) / 3;
    const cell = `${Math.floor(u * 16)},${Math.floor(v * 4)}`;
    let role = cellRoles[cell];
    if (role === undefined) {
      throw new Error(`${part}: a face on an unknown colour cell ${cell}`);
    }
    if (role === "coat" && accentParts.has(part)) {
      role = "accent";
    }
    // The dark on the body is the eyes' pupils (shut with the eyes when the cat sleeps); elsewhere it is the mouth and
    // the soles of the paws.
    if (role === "dark" && part === "body") {
      role = "pupils";
    }
    if (!byRole.has(role)) {
      byRole.set(role, []);
    }
    byRole.get(role).push(...corners);
  }
  return [...byRole].map(([role, used]) => {
    const remap = new Map();
    const positions = [];
    const normals = [];
    const newIndices = used.map((i) => {
      if (!remap.has(i)) {
        remap.set(i, remap.size);
        positions.push(position[i * 3], position[i * 3 + 1], position[i * 3 + 2] + forward(part, role));
        normals.push(normal[i * 3], normal[i * 3 + 1], normal[i * 3 + 2]);
      }
      return remap.get(i);
    });
    return [role, {
      attributes: {
        POSITION: addAccessor(new Float32Array(positions), "VEC3", 34962, true),
        NORMAL: addAccessor(new Float32Array(normals), "VEC3", 34962, false),
      },
      indices: addAccessor(new Uint16Array(newIndices), "SCALAR", 34963, false),
    }];
  });
}

// The animated nodes and where they rest.
const animated = ["root", "body", "mouth", "tail", "leg-back-left", "leg-back-right", "leg-front-left", "leg-front-right"];
const nodeIndex = (name) => out.nodes.findIndex((node) => node.name === name);
const rest = Object.fromEntries(animated.map((name) => {
  const node = out.nodes[nodeIndex(name)];
  return [name, { translation: node.translation ?? [0, 0, 0], rotation: node.rotation ?? [0, 0, 0, 1], scale: node.scale ?? [1, 1, 1] }];
}));

// Kenney's clips, as they are.
const keep = { idle: "idle", walk: "walk" };
for (const animation of gltf.animations) {
  const name = keep[animation.name];
  if (!name) {
    continue;
  }
  const samplers = [];
  const channels = [];
  for (const channel of animation.channels) {
    const sampler = animation.samplers[channel.sampler];
    const target = partName(gltf.nodes[channel.target.node].name);
    const type = channel.target.path === "rotation" ? "VEC4" : "VEC3";
    let times = source.read(sampler.input);
    let values = source.read(sampler.output);
    // A value held the whole clip needs one key, not thirty.
    const size = type === "VEC4" ? 4 : 3;
    if (values.every((value, i) => Math.abs(value - values[i % size]) < 1e-6)) {
      times = times.slice(0, 1);
      values = values.slice(0, size);
    }
    samplers.push({
      input: addAccessor(times, "SCALAR", undefined, true),
      output: addAccessor(values, type, undefined, false),
      interpolation: sampler.interpolation ?? "LINEAR",
    });
    channels.push({ sampler: samplers.length - 1, target: { node: nodeIndex(target), path: channel.target.path } });
  }
  out.animations.push({ name, samplers, channels });
}

// Rotations, from angles in radians about each axis, applied x first, then y, then z.
function quat(x = 0, y = 0, z = 0) {
  const qx = [Math.sin(x / 2), 0, 0, Math.cos(x / 2)];
  const qy = [0, Math.sin(y / 2), 0, Math.cos(y / 2)];
  const qz = [0, 0, Math.sin(z / 2), Math.cos(z / 2)];
  return multiply(qz, multiply(qy, qx));
}

function multiply([ax, ay, az, aw], [bx, by, bz, bw]) {
  return [
    aw * bx + ax * bw + ay * bz - az * by,
    aw * by - ax * bz + ay * bw + az * bx,
    aw * bz + ax * by - ay * bx + az * bw,
    aw * bw - ax * bx - ay * by - az * bz,
  ];
}

/**
 * A clip of our own: <keys> maps a node and path to [time, value] pairs; every other animated node holds its rest
 * transform, so the clip sets the whole pose whatever played before it.
 */
function clip(name, keys) {
  const samplers = [];
  const channels = [];
  for (const node of animated) {
    for (const path of ["translation", "rotation", "scale"]) {
      const frames = keys[`${node}.${path}`] ?? [[0, rest[node][path]]];
      samplers.push({
        input: addAccessor(new Float32Array(frames.map(([time]) => time)), "SCALAR", undefined, true),
        output: addAccessor(new Float32Array(frames.flatMap(([, value]) => value)), path === "rotation" ? "VEC4" : "VEC3", undefined, false),
        interpolation: "LINEAR",
      });
      channels.push({ sampler: samplers.length - 1, target: { node: nodeIndex(node), path } });
    }
  }
  out.animations.push({ name, samplers, channels });
}

const bodyY = rest.body.translation[1];
const tailRest = rest.tail.translation;

// Sitting: leaning back on its haunches, front legs straight, back legs folded under, the tail curled round on the
// floor; the tail's tip twitches now and then.
const sitLean = -0.25;
clip("sit", {
  "body.translation": [[0, [0, bodyY + 0.04, 0]]],
  "body.rotation": [[0, quat(sitLean)]],
  "leg-back-left.translation": [[0, [0.25, 0.12, -0.2]]],
  "leg-back-left.rotation": [[0, quat(-Math.PI / 2)]],
  "leg-back-right.translation": [[0, [-0.25, 0.12, -0.2]]],
  "leg-back-right.rotation": [[0, quat(-Math.PI / 2)]],
  "tail.translation": [[0, [0.1, 0.05, tailRest[2] - 0.1]]],
  "tail.rotation": [[0, quat(-0.2, 0, -1.45)], [1.5, quat(-0.2, 0, -1.45)], [1.8, quat(-0.2, 0.25, -1.45)], [2.1, quat(-0.2, 0, -1.45)], [4, quat(-0.2, 0, -1.45)]],
});

// Asleep: lying flat with the front paws out in front, the tail along its side, breathing slowly.
const breathe = (low, high) => [[0, low], [1.5, high], [3, low]];
clip("sleep", {
  "root.translation": [[0, [0, -0.17, 0]]],
  "body.scale": breathe([1.04, 0.86, 1], [1.04, 0.9, 1]),
  "leg-front-left.translation": [[0, [0.25, 0.27, 0.42]]],
  "leg-front-left.rotation": [[0, quat(-Math.PI / 2)]],
  "leg-front-right.translation": [[0, [-0.25, 0.27, 0.42]]],
  "leg-front-right.rotation": [[0, quat(-Math.PI / 2)]],
  "tail.translation": [[0, [0.35, 0.22, tailRest[2]]]],
  "tail.rotation": [[0, quat(-0.9, 0, -1.4)]],
});

// Waving: leaning back a little, the front left paw raised beside the head and waving side to side.
const pawOut = (angle) => quat(0, 0, Math.PI - angle);
clip("wave", {
  "body.rotation": [[0, quat(-0.1)]],
  "leg-front-left.translation": [[0, [0.6, 0.85, 0.3]]],
  "leg-front-left.rotation": [[0, pawOut(0.2)], [0.3, pawOut(0.75)], [0.6, pawOut(0.2)]],
  "tail.rotation": [[0, quat(0, 0, 0.15)], [0.3, quat(0, 0, -0.15)], [0.6, quat(0, 0, 0.15)]],
});

// Meowing: the head goes up, holds while it meows, and comes back down; the tail goes up with it. Played once.
const meowUp = -0.22;
clip("meow", {
  "body.rotation": [[0, quat()], [0.15, quat(meowUp)], [0.6, quat(meowUp)], [0.8, quat()]],
  "mouth.scale": [[0, [1, 1, 1]], [0.15, [1, 2.2, 1]], [0.6, [1, 2.2, 1]], [0.8, [1, 1, 1]]],
  "tail.rotation": [[0, quat()], [0.15, quat(0.25)], [0.6, quat(0.25)], [0.8, quat()]],
});

const json = {
  asset: { version: "2.0", generator: "pspsps scripts/cat-model.mjs", copyright: "Kenney (www.kenney.nl), CC0" },
  scene: 0,
  scenes: [{ name: "cat", nodes: [nodeIndex("animal-cat")] }],
  nodes: out.nodes,
  meshes: out.meshes,
  accessors: out.accessors,
  bufferViews: out.bufferViews,
  buffers: [{ byteLength }],
  animations: out.animations,
};
writeFileSync(output, writeGlb(json, chunks, byteLength));
console.log(`${output}: ${readFileSync(output).length} bytes, ${out.meshes.length} meshes, clips ${out.animations.map((a) => a.name).join(", ")}`);

function readGlb(bytes) {
  const jsonLength = bytes.readUInt32LE(12);
  const json = JSON.parse(bytes.subarray(20, 20 + jsonLength).toString("utf8"));
  const bin = bytes.subarray(20 + jsonLength + 8);
  const types = { 5126: Float32Array, 5123: Uint16Array, 5121: Uint8Array };
  const sizes = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };
  return {
    json,
    read(index) {
      const accessor = json.accessors[index];
      const view = json.bufferViews[accessor.bufferView];
      const Type = types[accessor.componentType];
      const start = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
      const count = accessor.count * sizes[accessor.type];
      const copy = new Uint8Array(bin.subarray(start, start + count * Type.BYTES_PER_ELEMENT));
      const values = new Type(copy.buffer);
      // Indices go out as 16 bits whatever they came in as.
      return Type === Uint8Array ? new Uint16Array(values) : values;
    },
  };
}

function writeGlb(json, binChunks, binLength) {
  let text = JSON.stringify(json);
  text += " ".repeat((4 - (text.length % 4)) % 4);
  const jsonBytes = Buffer.from(text, "utf8");
  const binPadded = binLength + ((4 - (binLength % 4)) % 4);
  const total = 12 + 8 + jsonBytes.length + 8 + binPadded;
  const header = Buffer.alloc(12);
  header.writeUInt32LE(0x46546c67, 0);
  header.writeUInt32LE(2, 4);
  header.writeUInt32LE(total, 8);
  const jsonHeader = Buffer.alloc(8);
  jsonHeader.writeUInt32LE(jsonBytes.length, 0);
  jsonHeader.writeUInt32LE(0x4e4f534a, 4);
  const binHeader = Buffer.alloc(8);
  binHeader.writeUInt32LE(binPadded, 0);
  binHeader.writeUInt32LE(0x004e4942, 4);
  return Buffer.concat([header, jsonHeader, jsonBytes, binHeader, ...binChunks.map((c) => Buffer.from(c)), Buffer.alloc(binPadded - binLength)]);
}
