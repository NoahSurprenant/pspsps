// Makes a built-in mesh (src/meshes.ts) from a Kenney glTF model that uses a colour-map texture (CC0; see
// src/models/README.md), from client/:
//
//   node scripts/mesh-model.mjs <model.glb> <out.json>
//
// The texture is read from where the model says, next to it. Every vertex gets the colour of the texel it points at; the
// game draws the mesh flat and outlined, like its other parts.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { inflateSync } from "node:zlib";

const [input, output] = process.argv.slice(2);
if (!input || !output) {
  throw new Error("usage: node scripts/mesh-model.mjs <model.glb> <out.json>");
}

const file = readFileSync(input);
const jsonLength = file.readUInt32LE(12);
const gltf = JSON.parse(file.subarray(20, 20 + jsonLength).toString());
const binary = file.subarray(20 + jsonLength + 8);

const componentTypes = { 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const widths = { SCALAR: 1, VEC2: 2, VEC3: 3 };

/** The values of accessor <index>, one array per element (a plain number for a scalar). */
function read(index) {
  const accessor = gltf.accessors[index];
  const view = gltf.bufferViews[accessor.bufferView];
  const Type = componentTypes[accessor.componentType];
  const width = widths[accessor.type];
  const stride = view.byteStride ?? width * Type.BYTES_PER_ELEMENT;
  const start = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
  const values = [];
  for (let i = 0; i < accessor.count; i++) {
    const element = [];
    for (let c = 0; c < width; c++) {
      const at = start + i * stride + c * Type.BYTES_PER_ELEMENT;
      element.push(new Type(binary.buffer.slice(binary.byteOffset + at, binary.byteOffset + at + Type.BYTES_PER_ELEMENT))[0]);
    }
    values.push(width === 1 ? element[0] : element);
  }
  return values;
}

/** The colour map as { width, height, channels, pixels }: an 8-bit RGB or RGBA PNG. */
function readPng(bytes) {
  let offset = 8;
  let header;
  const data = [];
  while (offset < bytes.length) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.toString("latin1", offset + 4, offset + 8);
    const body = bytes.subarray(offset + 8, offset + 8 + length);
    if (type === "IHDR") {
      header = { width: body.readUInt32BE(0), height: body.readUInt32BE(4), channels: body[9] === 6 ? 4 : 3 };
    } else if (type === "IDAT") {
      data.push(body);
    }
    offset += 12 + length;
  }
  const { width, height, channels } = header;
  const raw = inflateSync(Buffer.concat(data));
  const row = width * channels;
  const pixels = Buffer.alloc(row * height);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (row + 1)];
    for (let i = 0; i < row; i++) {
      const value = raw[y * (row + 1) + 1 + i];
      const left = i >= channels ? pixels[y * row + i - channels] : 0;
      const up = y > 0 ? pixels[(y - 1) * row + i] : 0;
      const upLeft = i >= channels && y > 0 ? pixels[(y - 1) * row + i - channels] : 0;
      pixels[y * row + i] = (value + predict(filter, left, up, upLeft)) & 255;
    }
  }
  return { width, height, channels, pixels };
}

function predict(filter, left, up, upLeft) {
  switch (filter) {
    case 1:
      return left;
    case 2:
      return up;
    case 3:
      return (left + up) >> 1;
    case 4: {
      const guess = left + up - upLeft;
      const [dl, du, dul] = [Math.abs(guess - left), Math.abs(guess - up), Math.abs(guess - upLeft)];
      if (dl <= du && dl <= dul) {
        return left;
      }
      return du <= dul ? up : upLeft;
    }
    default:
      return 0;
  }
}

const map = readPng(readFileSync(resolve(dirname(input), gltf.images[0].uri)));
const clamp = (n, max) => Math.min(max - 1, Math.max(0, n));

function colourAt([u, v]) {
  const x = clamp(Math.floor(u * map.width), map.width);
  const y = clamp(Math.floor(v * map.height), map.height);
  const at = (y * map.width + x) * map.channels;
  return `#${[0, 1, 2].map((c) => map.pixels[at + c].toString(16).padStart(2, "0")).join("")}`;
}

const primitive = gltf.meshes[0].primitives[0];
const positions = read(primitive.attributes.POSITION);
const uvs = read(primitive.attributes.TEXCOORD_0);
const indices = read(primitive.indices);
const round = (n) => Math.round(n * 1000) / 1000;

writeFileSync(output, JSON.stringify({ positions: positions.flat().map(round), indices, colours: uvs.map(colourAt) }));
console.log(`${positions.length} vertices, ${indices.length / 3} triangles`);
