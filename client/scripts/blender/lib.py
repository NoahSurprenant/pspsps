import bpy, bmesh, json, math, sys
from mathutils import Vector

_boxes = [0]
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _boxes[0] = 0

_mats = {}
def mat(hexcol):
    if hexcol not in _mats:
        m = bpy.data.materials.new(hexcol)
        r, g, b = (int(hexcol[i:i+2], 16) / 255 for i in (1, 3, 5))
        m.diffuse_color = (r, g, b, 1)
        _mats[hexcol] = m
    return _mats[hexcol]

def box(w, d, h, x, y, z, col, name="box"):
    """A box of width w (x), depth d (y; the front is -y), height h (z), its bottom-middle at (x, y, z)."""
    # Each box grows a hair more than the one before, so faces that would share a plane sit a few millimetres apart
    # and never flicker (z-fighting); the client zFight test checks the result.
    grow = 0.0025 * (1 + _boxes[0] % 23)
    _boxes[0] += 1
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + h / 2))
    o = bpy.context.active_object
    o.scale = (w + 2 * grow, d + 2 * grow, h + 2 * grow)
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(mat(col))
    return o

def prism(w, d, h, x, y, z, col):
    """A gable roof: ridge along x, w long, d wide at the bottom, h high."""
    bm = bmesh.new()
    pts = [(-w/2, -d/2, 0), (w/2, -d/2, 0), (w/2, d/2, 0), (-w/2, d/2, 0), (-w/2, 0, h), (w/2, 0, h)]
    v = [bm.verts.new(p) for p in pts]
    for f in ([0,1,2,3], [0,4,5,1], [3,2,5,4], [0,3,4], [1,5,2]):
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("prism"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("prism", me); bpy.context.collection.objects.link(o)
    o.location = (x, y, z); o.data.materials.append(mat(col))
    return o

def wedge(w, d, h, x, y, z, col):
    """A sloping awning: w wide, d deep, high at the wall (+y) and low at the front (-y)."""
    bm = bmesh.new()
    pts = [(-w/2, -d, 0), (w/2, -d, 0), (w/2, 0, 0), (-w/2, 0, 0), (-w/2, 0, h), (w/2, 0, h)]
    v = [bm.verts.new(p) for p in pts]
    for f in ([0,1,2,3], [0,3,4], [1,5,2], [0,4,5,1], [3,2,5,4]):
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("wedge"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("wedge", me); bpy.context.collection.objects.link(o)
    o.location = (x, y, z); o.data.materials.append(mat(col))
    return o

def text(s, size, x, y, z, col, extrude=0.04, font=None):
    bpy.ops.object.text_add(location=(0, 0, 0))
    o = bpy.context.active_object
    o.data.body = s; o.data.size = size; o.data.extrude = extrude
    o.data.align_x = "CENTER"; o.data.align_y = "CENTER"
    o.data.materials.append(mat(col))
    o.rotation_euler = (math.pi / 2, 0, 0)
    o.location = (x, y, z)
    bpy.ops.object.convert(target="MESH")
    return bpy.context.active_object

def export(path):
    dg = bpy.context.evaluated_depsgraph_get()
    pos, idx, cols, seen = [], [], [], {}
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        eo = o.evaluated_get(dg); me = eo.to_mesh(); me.calc_loop_triangles()
        mw = eo.matrix_world
        for t in me.loop_triangles:
            col = me.materials[t.material_index].diffuse_color if t.material_index < len(me.materials) and me.materials[t.material_index] else (1, 1, 1, 1)
            hexcol = "#%02x%02x%02x" % tuple(round(c * 255) for c in col[:3])
            for li in t.loops:
                v = mw @ me.vertices[me.loops[li].vertex_index].co
                key = (round(v.x, 3), round(v.z, 3), round(-v.y, 3), hexcol)
                if key not in seen:
                    seen[key] = len(pos) // 3
                    pos.extend(key[:3])
                    cols.append(hexcol)
                idx.append(seen[key])
        eo.to_mesh_clear()
    json.dump({"positions": pos, "indices": idx, "colours": cols}, open(path, "w"), separators=(",", ":"))
    print("exported", path, len(idx) // 3, "triangles")

def cyl(r, d, x, y, z, col, verts=24):
    """A disc or drum with its axis along y (a round window, a speaker cone, an arch): radius r, thick d, centre (x, y, z)."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d, location=(x, y, z), rotation=(math.pi / 2, 0, 0))
    o = bpy.context.active_object
    o.data.materials.append(mat(col))
    return o

def star(r, d, x, y, z, col):
    """A five-pointed star standing in the x/z plane, outer radius r, thick d, centre (x, y, z)."""
    bm = bmesh.new()
    pts = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        rr = r if k % 2 == 0 else r * 0.42
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    front = [bm.verts.new((px, -d / 2, pz)) for px, pz in pts]
    back = [bm.verts.new((px, d / 2, pz)) for px, pz in pts]
    bm.faces.new(front[::-1]); bm.faces.new(back)
    for k in range(10):
        bm.faces.new([front[k], front[(k + 1) % 10], back[(k + 1) % 10], back[k]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("star"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("star", me); bpy.context.collection.objects.link(o)
    o.location = (x, y, z); o.data.materials.append(mat(col))
    return o
