import bpy, bmesh, json, math, sys
from mathutils import Vector

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

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
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + h / 2))
    o = bpy.context.active_object
    o.scale = (w, d, h)
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
