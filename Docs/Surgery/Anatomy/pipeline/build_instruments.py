# MedSim AI — Surgery (Person 2): original procedural surgical instruments (generic designs, no third-party geometry).
# Usage: blender -b --python build_instruments.py -- <out_dir>
# Frame per instrument (Blender): grip point at the origin, working tip toward -Y, up +Z  ==>  Unity: +Z to tip, +Y up.
import bpy, bmesh, sys, os, math, json
from mathutils import Matrix, Vector

OUT = sys.argv[sys.argv.index("--") + 1]
os.makedirs(OUT, exist_ok=True)
MM = 0.001
REPORT = {}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
MATCOL = {"MA_SteelPolished": (0.80, 0.81, 0.83), "MA_SteelSatin": (0.70, 0.71, 0.73), "MA_SteelDark": (0.45, 0.46, 0.48), "MA_Blade": (0.85, 0.86, 0.88),
          "MA_PolymerGrey": (0.35, 0.37, 0.40), "MA_PolymerDark": (0.12, 0.13, 0.14), "MA_PolymerWhite": (0.92, 0.92, 0.90), "MA_PolymerBlue": (0.20, 0.42, 0.70),
          "MA_PolymerPurple": (0.42, 0.22, 0.58), "MA_Rubber": (0.08, 0.08, 0.09), "MA_GoldInsert": (0.80, 0.65, 0.30), "MA_PolymerAmber": (0.85, 0.55, 0.15),
          "MA_Tungsten": (0.30, 0.30, 0.32), "MA_PolymerClear": (0.85, 0.90, 0.95), "MA_Towel": (0.18, 0.42, 0.55)}
def mat(n):
    m = bpy.data.materials.get(n)
    if not m: m = bpy.data.materials.new(n); m.diffuse_color = (*MATCOL.get(n, (0.7, 0.7, 0.7)), 1)
    return m
def obj_from(bmx, name, material, parent=None, bevel=0.0, subd=0, smooth=True, wn=True):
    me = bpy.data.meshes.new(name); bmx.normal_update(); bmx.to_mesh(me); bmx.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat(material))
    for p in me.polygons: p.use_smooth = smooth
    if bevel:
        b = o.modifiers.new("bv", "BEVEL"); b.width = bevel; b.segments = 3; b.limit_method = "ANGLE"; b.angle_limit = math.radians(35)
    if subd:
        s = o.modifiers.new("sd", "SUBSURF"); s.levels = subd; s.render_levels = subd
    if wn and (bevel or subd):
        w = o.modifiers.new("wn", "WEIGHTED_NORMAL"); w.keep_sharp = True
    apply(o)
    if parent: o.parent = parent
    return o
def apply(o):
    if not o.modifiers: return
    d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d)
    o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old)
def box(sx, sy, sz, c=(0, 0, 0), rot=None):
    b = bmesh.new(); bmesh.ops.create_cube(b, size=1.0); b.transform(Matrix.Diagonal((sx, sy, sz, 1)))
    if rot: b.transform(rot)
    b.transform(Matrix.Translation(Vector(c))); return b
def cyl(r, h, c=(0, 0, 0), axis="Y", seg=32, r2=None):
    b = bmesh.new(); bmesh.ops.create_cone(b, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    R = {"Z": Matrix.Identity(4), "Y": Matrix.Rotation(math.pi / 2, 4, "X"), "X": Matrix.Rotation(math.pi / 2, 4, "Y")}[axis]
    b.transform(Matrix.Translation(Vector(c)) @ R); return b
def torus(R_, r, c=(0, 0, 0), axis_rot=None, seg=40, mseg=12):
    b = bmesh.new()
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; ring = []
        for j in range(mseg):
            t = 2 * math.pi * j / mseg
            p = Vector(((R_ + r * math.cos(t)) * math.cos(a), (R_ + r * math.cos(t)) * math.sin(a), r * math.sin(t)))
            ring.append(b.verts.new(p))
        rings.append(ring)
    for i in range(seg):
        for j in range(mseg):
            a_, b_ = rings[i], rings[(i + 1) % seg]
            b.faces.new((a_[j], b_[j], b_[(j + 1) % mseg], a_[(j + 1) % mseg]))
    if axis_rot: b.transform(axis_rot)
    b.transform(Matrix.Translation(Vector(c))); return b
def prism(poly2d, thickness, plane="XY", z0=0.0):
    """extrude a 2D outline (list of (u,v) in metres) by thickness; plane XY: u=x, v=y, thickness along z"""
    b = bmesh.new(); bot = [b.verts.new((u, v, z0 - thickness / 2)) for u, v in poly2d]; top = [b.verts.new((u, v, z0 + thickness / 2)) for u, v in poly2d]
    b.faces.new(list(reversed(bot))); b.faces.new(top)
    n = len(poly2d)
    for i in range(n): b.faces.new((bot[i], bot[(i + 1) % n], top[(i + 1) % n], top[i]))
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    if plane == "XZ": b.transform(Matrix.Rotation(math.pi / 2, 4, "X"))
    return b
def merge(*bms):
    out = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("t"); b.to_mesh(me); b.free(); out.from_mesh(me); bpy.data.meshes.remove(me)
    return out
def empty(name, loc, parent, rot=(0, 0, 0)):
    e = bpy.data.objects.new(name, None); e.location = loc; e.rotation_euler = rot; e.empty_display_size = 0.01
    bpy.context.scene.collection.objects.link(e); e.parent = parent; return e
def root(name):
    r = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(r); return r
def anchors(r, tip, interaction=None, attach=None, extra=None):
    empty("GripPoint", (0, 0, 0), r); empty("Tip", tip, r)
    empty("InteractionPoint", interaction if interaction else tip, r)
    empty("AttachmentPoint", attach if attach else (0, 0, 0), r); empty("ResetAnchor", (0, 0, 0), r)
    for k, v in (extra or {}).items(): empty(k, v, r)
def flatten(r):
    """FBX 'apply transform' mis-converts meshes parented to empties -> export flat; Unity rebuilds the hierarchy from 'parents'."""
    bpy.context.view_layer.update(); parents = {}
    for o in list(r.children_recursive):
        parents[o.name] = o.parent.name if o.parent and o.parent is not r else ""
    for o in list(r.children_recursive):
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    return parents
exec(open(r"D:/Medical-Test/scratch_surgery/uvbox.py").read())
def export(r, iid, info):
    kids = list(r.children_recursive)
    for _o in kids:
        if _o.type == "MESH": box_uv(_o, 0.03)
    vs = [o.matrix_world @ v.co for o in kids if o.type == "MESH" for v in o.data.vertices]
    parents = flatten(r)
    bpy.ops.object.select_all(action="DESELECT")
    for o in kids: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, iid + ".fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH", "EMPTY"}, use_tspace=True)
    mn = [min(v[i] for v in vs) for i in range(3)]; mx = [max(v[i] for v in vs) for i in range(3)]
    info.update({"tris": sum(len(p.vertices) - 2 for o in kids if o.type == "MESH" for p in o.data.polygons),
                 "size_mm": [round((mx[i] - mn[i]) * 1000, 1) for i in (0, 2, 1)],   # Unity x, y, z(length)
                 "parts": [o.name for o in kids if o.type == "MESH"], "parents": parents})
    REPORT[iid] = info

# ---------------------------------------------------------------- 1 skin marker (Ø11 x 140 mm, cap over the tip)
reset(); r = root("inst.skin_marker")
body = obj_from(cyl(5.5 * MM, 96 * MM, (0, 18 * MM, 0)), "Body", "MA_PolymerWhite", r, bevel=1.2 * MM)
cap = obj_from(cyl(6.2 * MM, 42 * MM, (0, -46 * MM, 0)), "Cap", "MA_PolymerPurple", r, bevel=1.5 * MM)
clip = obj_from(box(2.4 * MM, 30 * MM, 2.5 * MM, (0, -44 * MM, 6.6 * MM)), "CapClip", "MA_PolymerPurple", r, bevel=0.8 * MM)
nib = obj_from(cyl(1.8 * MM, 8 * MM, (0, -33 * MM, 0), r2=0.6 * MM), "Nib", "MA_PolymerPurple", r)
end = obj_from(cyl(5.6 * MM, 6 * MM, (0, 68 * MM, 0)), "EndPlug", "MA_PolymerPurple", r, bevel=1.2 * MM)
anchors(r, (0, -37 * MM, 0))
export(r, "inst.skin_marker", {"name": "Skin marker", "category": "marking", "movable": ["Cap"]})

# ---------------------------------------------------------------- 2 scalpel: No.3 handle + No.10 blade
reset(); r = root("inst.scalpel")
handle_outline = [(-4.0 * MM, 70 * MM), (4.0 * MM, 70 * MM), (4.6 * MM, 20 * MM), (4.0 * MM, -38 * MM), (2.5 * MM, -50 * MM), (-2.5 * MM, -50 * MM), (-4.0 * MM, -38 * MM), (-4.6 * MM, 20 * MM)]
h = obj_from(prism(handle_outline, 2.6 * MM), "Handle", "MA_SteelSatin", r, bevel=0.6 * MM)
for i in range(12):   # knurl ribs on the grip
    obj_from(box(9.4 * MM, 0.8 * MM, 0.5 * MM, (0, (2 + i * 3.2) * MM, 1.45 * MM)), f"Rib{i}", "MA_SteelDark", h)
blade_pts = []
for t in range(0, 21):   # blade: straight back, convex belly to the point
    a = t / 20; x = -3.8 * MM + 7.6 * MM * 0
blade_poly = [(-1.6 * MM, -48 * MM), (2.0 * MM, -48 * MM), (2.4 * MM, -60 * MM), (1.5 * MM, -78 * MM), (0.0 * MM, -88 * MM)]
belly = [(-1.6 * MM - 6.5 * MM * math.sin(math.pi * t / 14), -50 * MM - 36 * MM * t / 14) for t in range(1, 14)]
blade_poly = blade_poly + list(reversed(belly))
blade = obj_from(prism(blade_poly, 0.4 * MM, z0=0.0), "Blade", "MA_Blade", r, bevel=0.15 * MM)
anchors(r, (-5.5 * MM, -78 * MM, 0), attach=(0, -48 * MM, 0))
export(r, "inst.scalpel", {"name": "Scalpel (No.3 handle, No.10 blade)", "category": "cutting", "movable": ["Blade (replaceable)"]})

# ---------------------------------------------------------------- 3 Hohmann retractor
reset(); r = root("inst.retractor")
hd = obj_from(merge(box(16 * MM, 120 * MM, 3 * MM, (0, 40 * MM, 0)), cyl(8 * MM, 3 * MM, (0, 100 * MM, 0), axis="Z")), "Handle", "MA_SteelSatin", r, bevel=1.0 * MM)
# neck bends down 35 deg, blade curves and ends in a narrow point
b = bmesh.new(); prev = None
for i in range(31):
    t = i / 30; y = -20 * MM - 95 * MM * t; z = -18 * MM * (t ** 1.6) - 10 * MM * math.sin(min(1, t * 1.2) * math.pi / 2)
    w = (14 * MM if t < 0.55 else 14 * MM * (1 - (t - 0.55) / 0.45 * 0.82)); curl = 5 * MM * math.sin(math.pi * min(1, t * 1.15))
    ring = [b.verts.new((-w / 2, y, z + curl * 0.0)), b.verts.new((0, y, z - curl * 0.35)), b.verts.new((w / 2, y, z)),
            b.verts.new((w / 2, y, z + 2 * MM)), b.verts.new((0, y, z - curl * 0.35 + 2 * MM)), b.verts.new((-w / 2, y, z + 2 * MM))]
    if prev:
        for k in range(6): b.faces.new((prev[k], prev[(k + 1) % 6], ring[(k + 1) % 6], ring[k]))
    else: b.faces.new(ring)
    prev = ring
b.faces.new(list(reversed(prev)))
neck = obj_from(merge(b, box(10 * MM, 22 * MM, 3 * MM, (0, -10 * MM, 0))), "Blade", "MA_SteelPolished", r, bevel=0.5 * MM, subd=1)
anchors(r, (0, -115 * MM, -26 * MM))
export(r, "inst.retractor", {"name": "Hohmann retractor", "category": "retraction", "movable": []})

# ---------------------------------------------------------------- 4/5 cutting guides (generic blocks with a saw slot)
def guide(iid, name, w, hgt, depth, slot_off):
    reset(); r = root(iid)
    blk = box(w, depth, hgt, (0, -120 * MM, 0))
    blk_o = obj_from(blk, "Block", "MA_SteelSatin", r, bevel=1.0 * MM)
    # saw slot (1.3 mm) and pin holes are cut with booleans
    cutter = obj_from(box(w * 0.86, depth + 4 * MM, 1.3 * MM, (0, -120 * MM, slot_off)), "slot", "MA_SteelDark", None, smooth=False, wn=False)
    m = blk_o.modifiers.new("b", "BOOLEAN"); m.operation = "DIFFERENCE"; m.object = cutter; m.solver = "EXACT"; apply(blk_o); bpy.data.objects.remove(cutter)
    for sx in (-0.32, 0.32):
        for sz in (-0.28,):
            hole = obj_from(cyl(1.8 * MM, depth + 4 * MM, (w * sx, -120 * MM, hgt * sz)), "hole", "MA_SteelDark", None, smooth=False, wn=False)
            m = blk_o.modifiers.new("b", "BOOLEAN"); m.operation = "DIFFERENCE"; m.object = hole; m.solver = "EXACT"; apply(blk_o); bpy.data.objects.remove(hole)
    obj_from(merge(cyl(5 * MM, 95 * MM, (0, -55 * MM, 0)), cyl(12 * MM, 70 * MM, (0, 0, 0))), "Handle", "MA_PolymerDark", r, bevel=1.0 * MM)
    obj_from(cyl(6 * MM, 10 * MM, (0, -101 * MM, 0)), "Collar", "MA_SteelSatin", r, bevel=0.6 * MM)
    for i in range(6):
        obj_from(torus(12.2 * MM, 0.9 * MM, (0, (-25 + i * 10) * MM, 0), Matrix.Rotation(math.pi / 2, 4, "X")), f"Grip{i}", "MA_Rubber", r)
    anchors(r, (0, -120 * MM, slot_off), extra={"SeatPoint": (0, -120 * MM - depth / 2, 0), "SlotPlane": (0, -120 * MM, slot_off)})
    export(r, iid, {"name": name, "category": "guide", "movable": []})
guide("inst.cutting_guide_femoral", "Femoral cutting guide (generic distal block)", 62 * MM, 26 * MM, 16 * MM, 3 * MM)
guide("inst.cutting_guide_tibial", "Tibial cutting guide (generic block)", 56 * MM, 24 * MM, 16 * MM, 4 * MM)

# ---------------------------------------------------------------- 6 oscillating saw (pistol grip, removable blade)
def power_body(r, color="MA_PolymerGrey"):
    obj_from(merge(cyl(21 * MM, 120 * MM, (0, -10 * MM, 30 * MM)), cyl(17 * MM, 26 * MM, (0, -80 * MM, 30 * MM), r2=13 * MM)), "Housing", color, r, bevel=2 * MM)
    obj_from(box(28 * MM, 36 * MM, 96 * MM, (0, 0, 0), rot=Matrix.Rotation(math.radians(-14), 4, "X")), "Grip", "MA_PolymerDark", r, bevel=6 * MM).data.transform(Matrix.Translation((0, 12 * MM, -30 * MM)))
    obj_from(box(34 * MM, 62 * MM, 34 * MM, (0, 4 * MM, -86 * MM)), "Battery", "MA_PolymerAmber", r, bevel=3 * MM)
    trig = obj_from(box(9 * MM, 10 * MM, 30 * MM, (0, -22 * MM, 2 * MM)), "Trigger", "MA_PolymerDark", r, bevel=2 * MM)
    return trig
reset(); r = root("inst.oscillating_saw")
trig = power_body(r)
obj_from(cyl(9 * MM, 18 * MM, (0, -100 * MM, 30 * MM)), "Head", "MA_SteelSatin", r, bevel=1 * MM)
bl = [(-4 * MM, 0), (4 * MM, 0), (9.5 * MM, -80 * MM), (-9.5 * MM, -80 * MM)]
teeth = [(-9.5 * MM + 19 * MM * k / 14, -80 * MM - (1.6 * MM if k % 2 else 0)) for k in range(15)]
blade = obj_from(prism(bl[:2] + [bl[2]] + list(reversed(teeth)) + [bl[3]], 1.27 * MM), "Blade", "MA_Blade", r, bevel=0.2 * MM)
blade.data.transform(Matrix.Translation((0, -104 * MM, 30 * MM)))
pv = empty("Pivot_Blade", (0, -104 * MM, 30 * MM), r); blade.parent = pv; blade.data.transform(Matrix.Translation((0, 104 * MM, -30 * MM)))
anchors(r, (0, -185 * MM, 30 * MM), attach=(0, -104 * MM, 30 * MM))
export(r, "inst.oscillating_saw", {"name": "Oscillating saw (powered)", "category": "powered_cutting", "movable": ["Pivot_Blade/Blade (oscillates about local Y)", "Trigger"]})

# ---------------------------------------------------------------- 7 drill (pistol grip, chuck + 3.2 mm bit)
reset(); r = root("inst.drill")
trig = power_body(r, "MA_PolymerGrey")
pvc = empty("Pivot_Chuck", (0, -92 * MM, 30 * MM), r)
ch = obj_from(cyl(11 * MM, 24 * MM, (0, 0, 0), r2=7 * MM), "Chuck", "MA_SteelSatin", pvc, bevel=1 * MM); ch.data.transform(Matrix.Translation((0, -12 * MM, 0)))
bit = obj_from(merge(cyl(1.6 * MM, 96 * MM, (0, -72 * MM, 0)), cyl(1.6 * MM, 4 * MM, (0, -122 * MM, 0), r2=0.1 * MM)), "Bit", "MA_SteelPolished", pvc)
anchors(r, (0, -216 * MM, 30 * MM), attach=(0, -92 * MM, 30 * MM))
export(r, "inst.drill", {"name": "Surgical drill (powered, 3.2 mm bit)", "category": "powered_drilling", "movable": ["Pivot_Chuck (Chuck + Bit spin about local Z)", "Trigger"]})

# ---------------------------------------------------------------- 8 mallet
reset(); r = root("inst.mallet")
obj_from(merge(cyl(9 * MM, 200 * MM, (0, -60 * MM, 0)), cyl(13 * MM, 26 * MM, (0, 30 * MM, 0))), "Handle", "MA_PolymerDark", r, bevel=1.5 * MM)
head = obj_from(cyl(20 * MM, 70 * MM, (0, -170 * MM, 0), axis="X"), "Head", "MA_SteelSatin", r, bevel=1.5 * MM)
for sx in (-1, 1): obj_from(cyl(20.5 * MM, 12 * MM, (sx * 41 * MM, -170 * MM, 0), axis="X"), f"Face{'L' if sx < 0 else 'R'}", "MA_PolymerWhite", r, bevel=2 * MM)
anchors(r, (47 * MM, -170 * MM, 0))
export(r, "inst.mallet", {"name": "Surgical mallet (polymer faces)", "category": "impaction", "movable": []})

# ---------------------------------------------------------------- 9 impactor
reset(); r = root("inst.impactor")
obj_from(cyl(6 * MM, 220 * MM, (0, -20 * MM, 0)), "Shaft", "MA_SteelSatin", r, bevel=0.8 * MM)
obj_from(cyl(14 * MM, 70 * MM, (0, 20 * MM, 0)), "Grip", "MA_PolymerDark", r, bevel=2 * MM)
obj_from(cyl(20 * MM, 10 * MM, (0, 96 * MM, 0)), "StrikePlate", "MA_SteelSatin", r, bevel=1.5 * MM)
obj_from(merge(cyl(14 * MM, 16 * MM, (0, -136 * MM, 0)), box(34 * MM, 10 * MM, 18 * MM, (0, -148 * MM, 0))), "ImpactionHead", "MA_PolymerWhite", r, bevel=2.5 * MM)
anchors(r, (0, -153 * MM, 0), extra={"StrikePoint": (0, 101 * MM, 0)})
export(r, "inst.impactor", {"name": "Impactor (polymer impaction head)", "category": "impaction", "movable": []})

# ---------------------------------------------------------------- 10 needle driver (Mayo-Hegar), two jaws on the box-lock pivot
reset(); r = root("inst.needle_driver")
box_y = -95 * MM
def half(name, side):
    pv = empty(f"Pivot_{name}", (0, box_y, 0), r)
    pts = []
    for i in range(21):   # shank from ring to box-lock, slight curve
        t = i / 20; y = 10 * MM - (105 * MM) * t; x = side * (16 * MM * (1 - t) ** 1.2 + 1.5 * MM); pts.append((x, y))
    b = bmesh.new(); prev = None
    for (x, y) in pts:
        ring = [b.verts.new((x - 1.6 * MM, y, -1.6 * MM)), b.verts.new((x + 1.6 * MM, y, -1.6 * MM)), b.verts.new((x + 1.6 * MM, y, 1.6 * MM)), b.verts.new((x - 1.6 * MM, y, 1.6 * MM))]
        if prev:
            for k in range(4): b.faces.new((prev[k], prev[(k + 1) % 4], ring[(k + 1) % 4], ring[k]))
        else: b.faces.new(list(reversed(ring)))
        prev = ring
    b.faces.new(prev)
    jaw = box(3.2 * MM, 22 * MM, 3.0 * MM, (side * 0.9 * MM, box_y - 12 * MM, side * 0.9 * MM))
    ringm = torus(9 * MM, 1.7 * MM, (side * 22 * MM, 18 * MM, 0))
    o = obj_from(merge(b, jaw, ringm, box(6 * MM, 8 * MM, 3.2 * MM, (0, box_y, side * 0.8 * MM))), name, "MA_SteelPolished", None, bevel=0.5 * MM)
    o.data.transform(Matrix.Translation((0, -box_y, 0))); o.parent = pv
    ins = obj_from(box(2.6 * MM, 14 * MM, 0.6 * MM, (side * 0.9 * MM, -16 * MM, side * (0.9 * MM - 1.5 * MM))), name + "_Insert", "MA_Tungsten", pv)
    rat = obj_from(box(2.2 * MM, 7.0 * MM, 2.4 * MM, (side * 6.5 * MM, 2 * MM, -side * 1.0 * MM)), name + "_Ratchet", "MA_SteelSatin", pv, bevel=0.3 * MM)
    return pv
half("JawUpper", 1); half("JawLower", -1)
obj_from(cyl(2.4 * MM, 5.6 * MM, (0, box_y, 0), axis="Z"), "BoxLockScrew", "MA_SteelSatin", r, bevel=0.4 * MM)
anchors(r, (0, box_y - 22 * MM, 0), extra={"Pivot_BoxLock": (0, box_y, 0)})
export(r, "inst.needle_driver", {"name": "Needle driver (Mayo-Hegar pattern)", "category": "suturing", "movable": ["Pivot_JawUpper (rotates about local Z)", "Pivot_JawLower (rotates about local Z)"]})

# ---------------------------------------------------------------- 11 skin stapler (disposable)
reset(); r = root("inst.skin_stapler")
obj_from(merge(box(22 * MM, 110 * MM, 28 * MM, (0, -10 * MM, 20 * MM)), box(18 * MM, 26 * MM, 18 * MM, (0, -66 * MM, 8 * MM))), "Body", "MA_PolymerClear", r, bevel=5 * MM)
obj_from(box(18.5 * MM, 60 * MM, 6 * MM, (0, -14 * MM, 36 * MM)), "TopCover", "MA_PolymerBlue", r, bevel=2.5 * MM)
pvt = empty("Pivot_Trigger", (0, 30 * MM, 6 * MM), r)
tr = obj_from(box(16 * MM, 70 * MM, 10 * MM, (0, -10 * MM, -6 * MM), rot=Matrix.Rotation(math.radians(-14), 4, "X")), "Trigger", "MA_PolymerBlue", pvt, bevel=3 * MM)
tr.data.transform(Matrix.Translation((0, -30 * MM, -6 * MM)))
obj_from(box(10 * MM, 6 * MM, 4 * MM, (0, -80 * MM, 2 * MM)), "Nose", "MA_SteelSatin", r, bevel=0.8 * MM)
anchors(r, (0, -83 * MM, 0))
export(r, "inst.skin_stapler", {"name": "Skin stapler (disposable)", "category": "closure", "movable": ["Pivot_Trigger (rotates about local X)"]})

# ---------------------------------------------------------------- extras (proposed ids, not yet in SurgeryIds): Adson forceps, Yankauer suction
reset(); r = root("inst.forceps")
for side in (-1, 1):
    b = bmesh.new(); prev = None
    for i in range(25):
        t = i / 24; y = 30 * MM - 120 * MM * t; spread = side * (5 * MM * (1 - t) + 0.4 * MM); w = 8 * MM - 5.8 * MM * t ** 1.5
        ring = [b.verts.new((spread - 0.5 * MM, y, -w / 2)), b.verts.new((spread + 0.5 * MM, y, -w / 2)), b.verts.new((spread + 0.5 * MM, y, w / 2)), b.verts.new((spread - 0.5 * MM, y, w / 2))]
        if prev:
            for k in range(4): b.faces.new((prev[k], prev[(k + 1) % 4], ring[(k + 1) % 4], ring[k]))
        else: b.faces.new(list(reversed(ring)))
        prev = ring
    b.faces.new(prev)
    pv = empty(f"Pivot_Arm{'L' if side < 0 else 'R'}", (0, 30 * MM, 0), r)
    o = obj_from(b, f"Arm{'L' if side < 0 else 'R'}", "MA_SteelSatin", None, bevel=0.3 * MM); o.data.transform(Matrix.Translation((0, -30 * MM, 0))); o.parent = pv
anchors(r, (0, -90 * MM, 0))
export(r, "inst.forceps", {"name": "Adson forceps (toothed) — EXTRA, id not in contract", "category": "grasping", "movable": ["Pivot_ArmL", "Pivot_ArmR (close about local Y)"]})
reset(); r = root("inst.suction")
obj_from(merge(cyl(7 * MM, 120 * MM, (0, 40 * MM, 0)), cyl(4.5 * MM, 40 * MM, (0, 115 * MM, 0), r2=3.5 * MM)), "Handle", "MA_PolymerClear", r, bevel=1.5 * MM)
b = bmesh.new(); prev = None
for i in range(25):
    t = i / 24; y = -20 * MM - 110 * MM * t; z = -28 * MM * t ** 2; rr = 3.0 * MM - 0.8 * MM * t
    ring = [b.verts.new((rr * math.cos(2 * math.pi * k / 12), y, z + rr * math.sin(2 * math.pi * k / 12))) for k in range(12)]
    if prev:
        for k in range(12): b.faces.new((prev[k], prev[(k + 1) % 12], ring[(k + 1) % 12], ring[k]))
    else: b.faces.new(list(reversed(ring)))
    prev = ring
b.faces.new(prev)
obj_from(b, "Tube", "MA_PolymerClear", r, subd=1)
obj_from(cyl(3.6 * MM, 6 * MM, (0, -132 * MM, -28 * MM), r2=2.0 * MM), "BulbTip", "MA_PolymerClear", r, bevel=1 * MM)
anchors(r, (0, -135 * MM, -28 * MM))
export(r, "inst.suction", {"name": "Yankauer suction — EXTRA, id not in contract", "category": "suction", "movable": []})

# ---------------------------------------------------------------- instrument tray (stainless, sterile towel)
reset(); r = root("InstrumentTray")
tray = obj_from(box(0.60, 0.44, 0.012, (0, 0, 0.006)), "TrayBase", "MA_SteelSatin", r, bevel=4 * MM)
for (sx, sy, cx, cy) in ((0.60, 0.006, 0, 0.217), (0.60, 0.006, 0, -0.217), (0.006, 0.44, 0.297, 0), (0.006, 0.44, -0.297, 0)):
    obj_from(box(sx, sy, 0.03, (cx, cy, 0.021)), "TrayWall", "MA_SteelSatin", r, bevel=1.5 * MM)
obj_from(box(0.58, 0.42, 0.002, (0, 0, 0.0135)), "Towel", "MA_Towel", r, bevel=0.8 * MM)
empty("TrayRoot", (0, 0, 0), r)
kids = list(r.children_recursive)
for _o in kids:
    if _o.type == "MESH": box_uv(_o, 0.08)
flatten(r)
bpy.ops.object.select_all(action="DESELECT")
for o in kids: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "InstrumentTray.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH", "EMPTY"})
json.dump(REPORT, open(os.path.join(OUT, "instruments_report.json"), "w"), indent=1)
print("INSTRUMENTS_OK", json.dumps({k: (v["size_mm"], v["tris"]) for k, v in REPORT.items()}))
