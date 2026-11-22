// Makes a built-in mesh (src/meshes.ts) from a Kenney glTF model that uses a colour-map texture (CC0; see src/models/README.md):
//   node scripts/mesh-model.mjs <model.glb> <out.json>   (the texture is read from where the model says, next to it)
// Every vertex gets the colour of the texel it points at; the game draws the mesh flat, outlined, like its other parts.
import fs from 'fs';import path from 'path';import z from 'zlib';
const [,, input, output] = process.argv;
const b=fs.readFileSync(input);const jl=b.readUInt32LE(12);const j=JSON.parse(b.subarray(20,20+jl));
const bin=b.subarray(20+jl+8);
const acc=(i)=>{const a=j.accessors[i],v=j.bufferViews[a.bufferView];const n={SCALAR:1,VEC2:2,VEC3:3,VEC4:4}[a.type];
 const T={5126:Float32Array,5123:Uint16Array,5125:Uint32Array}[a.componentType];
 const off=bin.byteOffset+(v.byteOffset||0)+(a.byteOffset||0);const stride=v.byteStride||n*T.BYTES_PER_ELEMENT;
 const out=new Array(a.count);for(let k=0;k<a.count;k++){const r=[];for(let c=0;c<n;c++)r.push(new T(bin.buffer.slice(off+k*stride+c*T.BYTES_PER_ELEMENT,off+k*stride+(c+1)*T.BYTES_PER_ELEMENT))[0]);out[k]=n==1?r[0]:r;}return out;};
// decode PNG colormap
const png=fs.readFileSync(path.resolve(path.dirname(input),j.images[0].uri));
let p=8,W,H,bpp,idat=[];while(p<png.length){const l=png.readUInt32BE(p),t=png.toString('latin1',p+4,p+8);const d=png.subarray(p+8,p+8+l);if(t==='IHDR'){W=d.readUInt32BE(0);H=d.readUInt32BE(4);bpp=({2:3,6:4})[d[9]];if(!bpp)throw new Error('colour type '+d[9]);}if(t==='IDAT')idat.push(d);p+=12+l;}
const raw=z.inflateSync(Buffer.concat(idat));const st=W*bpp;const px=Buffer.alloc(st*H);
for(let y=0;y<H;y++){const f=raw[y*(st+1)];for(let i=0;i<st;i++){const v=raw[y*(st+1)+1+i];const a=i>=bpp?px[y*st+i-bpp]:0,u=y?px[(y-1)*st+i]:0,c=i>=bpp&&y?px[(y-1)*st+i-bpp]:0;let r;if(f==0)r=v;else if(f==1)r=v+a;else if(f==2)r=v+u;else if(f==3)r=v+((a+u)>>1);else{const pp=a+u-c,pa=Math.abs(pp-a),pb=Math.abs(pp-u),pc=Math.abs(pp-c);r=v+(pa<=pb&&pa<=pc?a:pb<=pc?u:c);}px[y*st+i]=r&255;}}
const hex=(u,v)=>{const x=Math.min(W-1,Math.max(0,Math.floor(u*W))),y=Math.min(H-1,Math.max(0,Math.floor(v*H)));const o=y*st+x*bpp;return '#'+[0,1,2].map(k=>px[o+k].toString(16).padStart(2,'0')).join('');};
const prim=j.meshes[0].primitives[0];
const pos=acc(prim.attributes.POSITION),uv=acc(prim.attributes.TEXCOORD_0),idx=acc(prim.indices);
const palette=[];const cols=pos.map((_,i)=>{const h=hex(uv[i][0],uv[i][1]);let k=palette.indexOf(h);if(k<0)k=palette.push(h)-1;return k;});
const r=(n)=>Math.round(n*1000)/1000;
fs.writeFileSync(output,JSON.stringify({positions:pos.flat().map(r),indices:idx,colours:cols.map(k=>palette[k])}));
console.log(pos.length,'verts',idx.length/3,'tris',palette.length,'colours',palette.join(' '),W,H);
