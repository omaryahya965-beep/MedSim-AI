# MedSim AI — Surgery (Person 2): surgical drapes, baked Blender cloth simulation (static meshes at runtime, no real-time cloth).
# Usage: blender -b work/knee_posed.blend --python build_drapes.py -- <work> <out_dir>
# Patient frame (Blender): table/mattress plane z = 0, head -Y, face +Z, patient right +X.
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index("--") + 1:]; WORK, OUT = argv[0], argv[1]; os.makedirs(OUT, exist_ok=True)
sc = bpy.context.scene; col = sc.collection
sc.use_gravity = True; sc.gravity = (0, 0, -9.81); sc.render.fps = 24   # the inherited Z-Anatomy scene has gravity OFF
J = json.load(open(os.path.join(WORK, "patient_report.json")))["joints"]
hip, knee, ank = Vector(J["Hip_R"]), Vector(J["Knee_R"]), Vector(J["Ankle_R"])
REP = {}
TABLE = dict(width=0.51, length=2.09, y_min=-1.05, thickness=0.08)   # provisional, Person 1's deck (0.51 x 2.09 m, mattress top = patient plane)

def mat(name, color):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name); m.diffuse_color = (*color, 1); return m
BLUE = mat("MA_DrapeBlue", (0.16, 0.36, 0.52)); KNIT = mat("MA_Stockinette", (0.88, 0.89, 0.90)); CUFF = mat("MA_CuffBlue", (0.20, 0.30, 0.55)); STEEL = mat("MA_SteelSatin", (0.7, 0.71, 0.73))
def link_copy(o, name):
    c = o.copy(); c.data = o.data.copy(); c.name = name; col.objects.link(c); return c
def decimated_collider(src_names, name, ratio=0.35, drop=None):
    global_thick = 0.010
    bmx = bmesh.new()
    for n in src_names:
        o = bpy.data.objects[n]; t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new("t"); t.to_mesh(me); t.free(); bmx.from_mesh(me); bpy.data.meshes.remove(me)
    if drop: bmesh.ops.delete(bmx, geom=[v for v in bmx.verts if drop(v.co)], context="VERTS")
    me = bpy.data.meshes.new(name); bmx.to_mesh(me); bmx.free(); o = bpy.data.objects.new(name, me); col.objects.link(o)
    d = o.modifiers.new("d", "DECIMATE"); d.ratio = ratio
    c = o.modifiers.new("collision", "COLLISION"); o.collision.thickness_outer = 0.010; o.collision.cloth_friction = 12; o.collision.damping = 0.6
    return o
def table_proxy():
    bmx = bmesh.new(); bmesh.ops.create_cube(bmx, size=1.0)
    bmx.transform(Matrix.Translation((0, TABLE["y_min"] + TABLE["length"] / 2, -TABLE["thickness"] / 2)) @ Matrix.Diagonal((TABLE["width"], TABLE["length"], TABLE["thickness"], 1)))
    me = bpy.data.meshes.new("TableProxy"); bmx.to_mesh(me); bmx.free(); o = bpy.data.objects.new("TableProxy", me); col.objects.link(o)
    o.modifiers.new("collision", "COLLISION"); o.collision.thickness_outer = 0.004; o.collision.cloth_friction = 10; return o
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (p - (a + ab * t)).length, t
def right_leg(p, margin=0.0):   # region of the operative leg from mid-thigh down (incl. foot)
    d1, t1 = seg_dist(p, hip, knee); d2, t2 = seg_dist(p, knee, ank)
    foot = (p - ank).length < 0.20 and p.x > 0.05
    return (d1 < 0.12 + margin and t1 > 0.25) or d2 < 0.11 + margin or foot
def grid(name, corner, du, dv, nu, nv):
    bmx = bmesh.new(); vs = [[bmx.verts.new(corner + du * (i / nu) + dv * (j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
    for i in range(nu):
        for j in range(nv): bmx.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    me = bpy.data.meshes.new(name); bmx.to_mesh(me); bmx.free(); o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(BLUE); return o
def sheet(name, x0, x1, y0, y1, z, nx, ny):
    return grid(name, Vector((x0, y0, z)), Vector((x1 - x0, 0, 0)), Vector((0, y1 - y0, 0)), nx, ny)
def simulate(o, frames=70, pin=None, quality=7, mass=0.25):
    if pin:
        vg = o.vertex_groups.new(name="pin"); vg.add([v.index for v in o.data.vertices if pin(v.co)], 1.0, "REPLACE")
    m = o.modifiers.new("cloth", "CLOTH"); s = m.settings
    s.quality = quality; s.mass = mass; s.tension_stiffness = 12; s.compression_stiffness = 12; s.shear_stiffness = 6; s.bending_stiffness = 0.6
    s.air_damping = 1.5; m.collision_settings.distance_min = 0.006; m.collision_settings.use_self_collision = False; m.collision_settings.collision_quality = 5
    if pin: s.vertex_group_mass = "pin"
    m.point_cache.frame_start = 1; m.point_cache.frame_end = frames
    sc.frame_start = 1; sc.frame_end = frames
    for f in range(1, frames + 1): sc.frame_set(f)
    d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d)
    o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old); me.materials.clear(); me.materials.append(BLUE)
    sc.frame_set(1)
def finish(o, thick=0.0012, smooth=10, dec=1.0):
    sm = o.modifiers.new("sm", "SMOOTH"); sm.factor = 0.5; sm.iterations = smooth
    if dec < 1.0: dm = o.modifiers.new("dec", "DECIMATE"); dm.ratio = dec
    so = o.modifiers.new("so", "SOLIDIFY"); so.thickness = thick; so.offset = 1
    d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d)
    o.modifiers.clear(); mats = list(o.data.materials); old = o.data; o.data = me; bpy.data.meshes.remove(old)
    for p in me.polygons: p.use_smooth = True

table = table_proxy()
HALF_W = TABLE["width"] / 2
def projected(name, x0, x1, y0, y1, nx, ny, src_names, drop, offset=0.010, y_hang_end=None):
    """sheet laid onto body + table from above; parts beyond the table edge pre-hung vertically (length preserved)"""
    bmx = bmesh.new()
    for n in src_names:
        o = bpy.data.objects[n]; t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new("t"); t.to_mesh(me); t.free(); bmx.from_mesh(me); bpy.data.meshes.remove(me)
    if drop: bmesh.ops.delete(bmx, geom=[v for v in bmx.verts if drop(v.co)], context="VERTS")
    tree = BVHTree.FromBMesh(bmx); bmx.free()
    o = sheet(name, x0, x1, y0, y1, 0.0, nx, ny)
    for v in o.data.vertices:
        x, y = v.co.x, v.co.y; xe = max(-HALF_W - 0.004, min(HALF_W + 0.004, x)); over_x = abs(x) - (HALF_W + 0.004)
        ye = y; over_y = 0.0
        if y_hang_end is not None and y > y_hang_end: over_y = y - y_hang_end; ye = y_hang_end
        h = tree.ray_cast(Vector((xe, ye, 1.5)), Vector((0, 0, -1)))
        top = max(0.0, h[0].z if h[0] is not None else 0.0) + offset
        if over_x > 0: v.co = Vector((math.copysign(HALF_W + 0.006 + 0.02 * (1 - math.exp(-over_x * 8)), x), ye, min(top, offset) - over_x))
        elif over_y > 0: v.co = Vector((x, y_hang_end + 0.006, top - over_y))
        else: v.co = Vector((x, y, top))
    return o
def safe_sim(o, rebuild, frames=30):
    name = o.name; simulate(o, frames=frames)
    big = max(abs(c) for v in o.data.vertices for c in v.co)
    REP.setdefault("sim", {})[name] = "simulated" if big < 3 else "diverged -> projected only"
    if big < 3: return o
    bpy.data.objects.remove(o); o = rebuild(); o.name = name
    sm = o.modifiers.new("sm", "SMOOTH"); sm.factor = 0.6; sm.iterations = 6
    d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d); o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old); me.materials.clear(); me.materials.append(BLUE)
    return o
import numpy as np
def heightfield_sheet(name, x0, x1, y0, y1, res, src_names, drop, clear_body=0.014, clear_table=0.003, dilate=0.03, y_end=None, seed=1, taper=()):
    bmx = bmesh.new()
    for n in src_names:
        o = bpy.data.objects[n]; t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new("t"); t.to_mesh(me); t.free(); bmx.from_mesh(me); bpy.data.meshes.remove(me)
    if drop: bmesh.ops.delete(bmx, geom=[v for v in bmx.verts if drop(v.co)], context="VERTS")
    tree = BVHTree.FromBMesh(bmx); bmx.free()
    xs = np.arange(x0, x1 + 1e-9, res); ys = np.arange(y0, y1 + 1e-9, res); nx, ny = len(xs), len(ys)
    H = np.zeros((nx, ny)); body = np.zeros((nx, ny), bool)
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            xe = max(-HALF_W, min(HALF_W, x)); ye = y if y_end is None else min(y, y_end)
            h = tree.ray_cast(Vector((xe, ye, 1.5)), Vector((0, 0, -1)))
            if h[0] is not None and h[0].z > 0.002: H[i, j] = h[0].z; body[i, j] = True
    floor = H + np.where(body, clear_body, clear_table)
    # tent over limbs: dilate, then blur while never dropping below the clearance floor
    r = max(1, int(round(dilate / res))); D = floor.copy()
    for dx in range(-r, r + 1):
        for dy in range(-r, r + 1):
            if dx * dx + dy * dy > r * r: continue
            D = np.maximum(D, np.roll(np.roll(floor, dx, 0), dy, 1) - 0.25 * math.hypot(dx, dy) * res)
    Z = D
    for _ in range(70):   # long relaxation: no terracing from the dilation step
        P = np.pad(Z, 1, mode="edge"); Z = (P[:-2, 1:-1] + P[2:, 1:-1] + P[1:-1, :-2] + P[1:-1, 2:] + 4 * Z) / 8; Z = np.maximum(Z, floor)
    # inner edges lie down on the body (no "cliff" where two sheets meet)
    for edge, width in taper:
        n_ = max(1, int(width / res))
        for k in range(n_):
            w = (k / n_) ** 1.5; j = k if edge == "y0" else ny - 1 - k
            Z[:, j] = floor[:, j] + (Z[:, j] - floor[:, j]) * w
    rng = np.random.default_rng(seed)
    ph = rng.uniform(0, 6.28, 4)
    bm_ = bmesh.new(); vg = [[None] * ny for _ in range(nx)]
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            z = Z[i, j] + 0.0025 * math.sin(x * 23 + ph[0]) * math.sin(y * 17 + ph[1]) + 0.0015 * math.sin((x + y) * 41 + ph[2])
            px, py = x, y
            over_x = abs(x) - HALF_W
            if over_x > 0:   # roll over the table edge, then hang with curtain folds
                roll = 0.02; zc = min(Z[i, j], clear_table)
                if over_x < roll * math.pi / 2:
                    ang = over_x / roll; px = math.copysign(HALF_W + roll * math.sin(ang), x); z = zc - roll * (1 - math.cos(ang))
                else:
                    drop_ = over_x - roll * math.pi / 2; px = math.copysign(HALF_W + roll + 0.012 * math.sin(y * 28 + ph[3]) * min(1, drop_ * 6), x); z = zc - roll - drop_
            if y_end is not None and y > y_end:
                over_y = y - y_end; roll = 0.02; zc = Z[i, min(j, int((y_end - y0) / res))]
                if over_y < roll * math.pi / 2:
                    ang = over_y / roll; py = y_end + roll * math.sin(ang); z = min(z, zc - roll * (1 - math.cos(ang)))
                else:
                    d_ = over_y - roll * math.pi / 2; py = y_end + roll + 0.010 * math.sin(x * 30 + ph[3]) * min(1, d_ * 6); z = min(z, zc - roll - d_)
            vg[i][j] = bm_.verts.new((px, py, z))
    for i in range(nx - 1):
        for j in range(ny - 1): bm_.faces.new((vg[i][j], vg[i + 1][j], vg[i + 1][j + 1], vg[i][j + 1]))
    me = bpy.data.meshes.new(name); bm_.to_mesh(me); bm_.free(); o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(BLUE)
    REP.setdefault("heightfield", {})[name] = [nx, ny]
    return o
upper_src = ["Body", "Gown", "Brief", "RightLeg_Prepped", "KneeSkin"]
up = heightfield_sheet("Drape_Upper", -0.80, 0.80, -0.50, 0.10, 0.01, upper_src, lambda p: p.x > 0.03 and p.y > 0.03 and right_leg(p, 0.02), seed=3, taper=(("y1", 0.05),))
lo = heightfield_sheet("Drape_Lower", -0.80, 0.80, 0.03, 1.42, 0.01, ["Body", "Brief"], lambda p: right_leg(p, 0.03) or p.y < 0.0, taper=(("y0", 0.05),),
                       y_end=TABLE["y_min"] + TABLE["length"], seed=7)
# ---- anaesthesia screen drape hanging from a bar at the upper chest (bar = optional frame; Person 1 may supply their own)
bar_y, bar_z = -0.52, 0.56
bm2 = bmesh.new(); bmesh.ops.create_cone(bm2, cap_ends=True, segments=16, radius1=0.009, radius2=0.009, depth=0.80)
bm2.transform(Matrix.Translation((0, bar_y, bar_z)) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
for sx in (-0.40, 0.40):
    t = bmesh.new(); bmesh.ops.create_cone(t, cap_ends=True, segments=12, radius1=0.008, radius2=0.008, depth=bar_z + 0.08)
    t.transform(Matrix.Translation((sx, bar_y, (bar_z - 0.08) / 2 - 0.04))); me = bpy.data.meshes.new("t"); t.to_mesh(me); t.free(); bm2.from_mesh(me); bpy.data.meshes.remove(me)
me = bpy.data.meshes.new("ScreenFrame"); bm2.to_mesh(me); bm2.free(); frame = bpy.data.objects.new("ScreenFrame", me); col.objects.link(frame); me.materials.append(STEEL)
screen_col = decimated_collider(["Body", "Gown"], "ColScreen", drop=lambda p: p.y > -0.25)
scr = grid("Drape_Screen", Vector((-0.55, bar_y - 0.012, bar_z)), Vector((1.10, 0, 0)), Vector((0, -0.05, -0.62)), 44, 30)
# fold the lower part toward the head so it falls over the bar side
simulate(scr, frames=60, pin=lambda p: p.z > bar_z - 0.02)
bpy.data.objects.remove(screen_col)
bpy.data.objects.remove(table)
# ---- thigh cuff (covers the junction where the operative thigh leaves the drapes)
def band(name, a, b, t0, t1, r_add, width_mat):
    ax = (b - a).normalized(); bmx = bmesh.new(); seg = 36; rings = []
    trees = [BVHTree.FromObject(bpy.data.objects[n], bpy.context.evaluated_depsgraph_get()) for n in ("Body", "RightLeg_Prepped")]
    side = ax.cross(Vector((1, 0, 0))).normalized(); up_ = ax.cross(side).normalized()
    for k in range(9):
        t = t0 + (t1 - t0) * k / 8; c = a + (b - a) * t; ring = []
        for i in range(seg):
            ang = 2 * math.pi * i / seg; d = (side * math.cos(ang) + up_ * math.sin(ang))
            hit = None
            for tree in trees:   # outermost skin within 10 cm of the thigh axis (never the other thigh)
                h = tree.ray_cast(c + d * 0.10, -d, 0.10)
                if h[0] is not None and (hit is None or (h[0] - c).length > (hit - c).length): hit = h[0]
            rr = ((hit - c).length if hit is not None else 0.08) + r_add
            ring.append(bmx.verts.new(c + d * rr))
        rings.append(ring)
    for k in range(8):
        for i in range(seg): bmx.faces.new((rings[k][i], rings[k][(i + 1) % seg], rings[k + 1][(i + 1) % seg], rings[k + 1][i]))
    me = bpy.data.meshes.new(name); bmx.to_mesh(me); bmx.free(); o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(width_mat)
    so = o.modifiers.new("so", "SOLIDIFY"); so.thickness = 0.004; so.offset = 1; return o
cuff = band("ThighCuff", hip, knee, 0.30, 0.52, 0.006, CUFF)
# ---- stockinette over the foot and distal shin (offset copy of the skin)
def offset_region(name, srcs, pred, push, material):
    bmx = bmesh.new()
    for n in srcs:
        o = bpy.data.objects[n]; t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new("t"); t.to_mesh(me); t.free(); bmx.from_mesh(me); bpy.data.meshes.remove(me)
    bmesh.ops.delete(bmx, geom=[f for f in bmx.faces if not pred(f.calc_center_median())], context="FACES")
    bmesh.ops.remove_doubles(bmx, verts=bmx.verts, dist=1e-5); bmx.normal_update()
    for v in bmx.verts: v.co += v.normal * push
    me = bpy.data.meshes.new(name); bmx.to_mesh(me); bmx.free(); o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(material)
    sm = o.modifiers.new("sm", "SMOOTH"); sm.factor = 0.6; sm.iterations = 4
    so = o.modifiers.new("so", "SOLIDIFY"); so.thickness = 0.0025; so.offset = 1; return o
def stock_pred(p):
    d2, t2 = seg_dist(p, knee, ank)
    return p.x > 0.03 and ((d2 < 0.12 and t2 > 0.55) or (p - ank).length < 0.22 and p.y > ank.y - 0.06)
stock = offset_region("Stockinette", ["Body", "RightLeg_Prepped"], stock_pred, 0.004, KNIT)
for o, dec in ((up, 0.22), (lo, 0.18), (scr, 0.6)): finish(o, dec=dec)
for o in (cuff, stock):
    d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d); o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old)
    for p in me.polygons: p.use_smooth = True
exec(open(r"D:/Medical-Test/scratch_surgery/uvbox.py").read())
for _o in [o for o in bpy.data.objects if o.type == "MESH"]: box_uv(_o, 0.25)
drapes = [up, lo, scr, frame, cuff, stock]
REP["tris"] = {o.name: len(o.data.polygons) for o in drapes}
REP["table"] = TABLE
REP["bounds"] = {o.name: [[round(min((o.matrix_world @ v.co)[i] for v in o.data.vertices), 3) for i in range(3)], [round(max((o.matrix_world @ v.co)[i] for v in o.data.vertices), 3) for i in range(3)]] for o in drapes}
bpy.ops.object.select_all(action="DESELECT")
for o in drapes: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SurgicalDrape.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH"})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "drapes.blend"))
json.dump(REP, open(os.path.join(OUT, "SurgicalDrape_report.json"), "w"), indent=1)
print("DRAPES_OK", json.dumps(REP)[:2000])
