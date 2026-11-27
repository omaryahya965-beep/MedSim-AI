# MedSim AI — Surgery (Person 2): knee stage 2 — resection placeholders, generic implants, skin state geometry, export.
# Usage: blender -b work/knee_posed.blend --python build_knee2.py -- <work> <anatomy_models_dir> <implant_models_dir> <patient_models_dir>
# All cut/implant dimensions below are ILLUSTRATIVE EDUCATIONAL PLACEHOLDERS (not surgical parameters).
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from mathutils.geometry import delaunay_2d_cdt

argv = sys.argv[sys.argv.index("--") + 1:]
WORK, OUT_K, OUT_I, OUT_P = argv[:4]
for d in (OUT_K, OUT_I, OUT_P): os.makedirs(d, exist_ok=True)
J = json.load(open(os.path.join(WORK, "knee_pose.json"))); LM = {k: Vector(v) for k, v in J["landmarks"].items()}
REP = {"pose": J["report"]}
O = bpy.data.objects
col = bpy.context.scene.collection
knee = LM["kneeCenter"]
PH = dict(distal_resection_mm=9, posterior_resection_mm=9, chamfer_mm=10, component_height_mm=62, tibial_resection_mm=9,
          tray_thickness_mm=4, insert_thickness_mm=10, insert_dish_depth_mm=3)   # placeholders, see IMPLANT_CATALOG.md
REP["placeholders"] = PH

def mat(name, color=(0.8, 0.8, 0.8)):
    m = bpy.data.materials.get(name)
    if not m: m = bpy.data.materials.new(name); m.diffuse_color = (*color, 1)
    return m
def ortho(prox, ant):
    prox = prox.normalized(); ant = (ant - prox * ant.dot(prox)).normalized()
    lat = Vector((1, 0, 0)); lat = (lat - prox * lat.dot(prox) - ant * lat.dot(ant)).normalized()
    if lat.cross(prox).dot(ant) > 0: lat = -lat   # Blender: lat x prox = -ant  (non-mirrored Unity frame: +X lat, +Y prox, +Z ant)
    return lat, prox, ant
F_lat, F_pro, F_ant = ortho(LM["femurProximalDir"], LM["femurAnteriorDir"])
T_lat, T_pro, T_ant = ortho(-LM["tibiaDistalDir"], LM["tibiaAnteriorDir"])
REP["frameCheck"] = {"femurLatDotX": round(F_lat.x, 3), "tibiaLatDotX": round(T_lat.x, 3)}

def new_obj(name, bmx, material, smooth=True):
    me = bpy.data.meshes.new(name); bmx.normal_update(); bmx.to_mesh(me); bmx.free()
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    if isinstance(material, list):
        for m in material: me.materials.append(m)
    else: me.materials.append(material)
    for p in me.polygons: p.use_smooth = smooth
    return o
def joined_closed(names, newname):
    bmx = bmesh.new()
    for k, n in enumerate(names):
        before = len(bmx.faces); bmx.from_mesh(O[n].data); bmx.faces.ensure_lookup_table()
        for f in bmx.faces[before:]: f.material_index = 0 if k == 0 else 1
    bmesh.ops.remove_doubles(bmx, verts=bmx.verts, dist=2e-5)
    bnd = [e for e in bmx.edges if e.is_boundary]
    if bnd: bmesh.ops.holes_fill(bmx, edges=bnd, sides=0)
    bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces)
    nm = sum(1 for e in bmx.edges if not e.is_manifold)
    me = bpy.data.meshes.new(newname); bmx.to_mesh(me); bmx.free()
    o = bpy.data.objects.new(newname, me); col.objects.link(o)
    for m in O[names[0]].data.materials: me.materials.append(m)
    # cartilage pieces come in with material index 0 -> map them to the bone's cartilage slot by face proximity later; keep index from the source
    return o, nm
def convex_cutter(name, planes, center, size=0.4):
    """planes: list of (point, outward_normal); returns the convex keep-polytope as a closed mesh"""
    bmx = bmesh.new(); bmesh.ops.create_cube(bmx, size=size); bmx.transform(Matrix.Translation(center))
    for co, no in planes:
        r = bmesh.ops.bisect_plane(bmx, geom=bmx.verts[:] + bmx.edges[:] + bmx.faces[:], plane_co=co, plane_no=no, clear_outer=True)
        e = [x for x in r["geom_cut"] if isinstance(x, bmesh.types.BMEdge)]
        if e: bmesh.ops.contextual_create(bmx, geom=e)
    bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces)
    return new_obj(name, bmx, mat("MA_CutBone", (0.85, 0.65, 0.45)), smooth=False)
def boolean(target, cutter, op):
    m = target.modifiers.new("b", "BOOLEAN"); m.operation = op; m.object = cutter; m.solver = "EXACT"
    try: m.material_mode = "TRANSFER"
    except Exception: pass
    d = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(target.evaluated_get(d), depsgraph=d)
    target.modifiers.clear(); old = target.data; target.data = me; bpy.data.meshes.remove(old)
def dup(o, name):
    c = o.copy(); c.data = o.data.copy(); c.name = name; c.data.name = name; col.objects.link(c); return c
def coords(o): return np.array([v.co[:] for v in o.data.vertices])

# ---------------------------------------------------------------- femur: placeholder 5-cut resection and femoral component
fem, nm_f = joined_closed(["DistalFemur"], "DistalFemur_Closed")   # bone is closed (cartilage is a separate layer)
REP["distalFemurNonManifoldEdges"] = nm_f
P = coords(fem) - np.array(knee[:]); u = P @ np.array(F_pro[:]); a = P @ np.array(F_ant[:]); l = P @ np.array(F_lat[:])
dmin = u.min(); u0 = dmin + PH["distal_resection_mm"] / 1000
cond = u < dmin + 0.05
a_p = a[cond].min() + PH["posterior_resection_mm"] / 1000
cond_ant = u < dmin + 0.045   # anterior condyles / trochlea: the flange takes a resection-thickness slab here (joins both condyles)
a_a = a[cond_ant].max() - PH["distal_resection_mm"] / 1000
c = PH["chamfer_mm"] / 1000; top = u0 + PH["component_height_mm"] / 1000 - PH["distal_resection_mm"] / 1000
def W(uu, aa, ll=0.0): return knee + F_pro * uu + F_ant * aa + F_lat * ll
d2 = math.sqrt(2)
planes = [(W(u0, 0), -F_pro), (W(0, a_p), -F_ant), (W(0, a_a), F_ant),
          (W(u0, a_a - c), (F_ant - F_pro) / d2), (W(u0, a_p + c), (-F_ant - F_pro) / d2)]
keep = convex_cutter("FemurCutter", planes, knee + F_pro * 0.12)
# above the component height everything is kept: union a box over the polytope (overlapping by 0.5 mm)
bmx = bmesh.new(); bmesh.ops.create_cube(bmx, size=0.4); bmx.transform(Matrix.Translation(knee + F_pro * (top - 0.0005 + 0.2)))
upper = new_obj("FemurUpper", bmx, mat("MA_CutBone"), smooth=False)
# make the upper box axis-aligned with the femur frame
R = Matrix((F_lat, F_pro, F_ant)).transposed().to_4x4()
upper.data.transform(Matrix.Translation(knee + F_pro * (top - 0.0005 + 0.2)) @ R @ Matrix.Translation(-(knee + F_pro * (top - 0.0005 + 0.2))))
boolean(keep, upper, "UNION"); bpy.data.objects.remove(upper)
# fixation pegs (7 mm x 12 mm) on the distal inner face, medial and lateral condyle centres
notch_l = float(np.median(l[cond]))
med_l = float(np.percentile(l[cond], 22)); lat_l = float(np.percentile(l[cond], 78))
a_mid = (a_p + a_a) / 2 - 0.006
def cyl(name, base, axis, r, h, segs=24):
    bmx = bmesh.new(); bmesh.ops.create_cone(bmx, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=h)
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    bmx.transform(Matrix.Translation(base + axis.normalized() * (h / 2)) @ q); return new_obj(name, bmx, mat("MA_Implant", (0.72, 0.74, 0.78)))
pegs = [cyl("pegM", W(u0 - 0.001, a_mid, med_l), F_pro, 0.0035, 0.013), cyl("pegL", W(u0 - 0.001, a_mid, lat_l), F_pro, 0.0035, 0.013)]
fem_res = dup(fem, "DistalFemur_Resected"); boolean(fem_res, keep, "INTERSECT")
fem_comp = dup(fem, "FemoralComponent"); boolean(fem_comp, keep, "DIFFERENCE")
# keep only the largest connected shell(s) of the component (drop slivers)
for pg in pegs:
    boolean(fem_comp, pg, "UNION")
    hole = dup(pg, pg.name + "_hole"); hole.scale = (1.0, 1.0, 1.0); boolean(fem_res, hole, "DIFFERENCE"); bpy.data.objects.remove(hole)
for pg in pegs: bpy.data.objects.remove(pg)
bpy.data.objects.remove(keep)
def assign_component_materials(o, outer, inner):
    """inner (bone-facing) faces lie ON a cut plane with the normal pointing into the kept bone; everything else is articular"""
    o.data.materials.clear(); o.data.materials.append(outer); o.data.materials.append(inner)
    for p in o.data.polygons:
        c = p.center; n = p.normal; inner_face = False
        for co, no in planes:
            if abs((c - co).dot(no)) < 0.0004 and n.dot(-no) > 0.9: inner_face = True; break
        p.material_index = 1 if inner_face else 0
    # polish the articular surface: smooth only vertices that belong exclusively to articular faces
    bm_ = bmesh.new(); bm_.from_mesh(o.data)
    art = [v for v in bm_.verts if all(f.material_index == 0 for f in v.link_faces)]
    for _ in range(6): bmesh.ops.smooth_vert(bm_, verts=art, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm_.to_mesh(o.data); bm_.free()
assign_component_materials(fem_comp, mat("MA_Implant"), mat("MA_ImplantBack", (0.55, 0.56, 0.58)))
fem_trial = dup(fem_comp, "FemoralTrial"); fem_trial.data.materials[0] = mat("MA_Trial", (0.55, 0.58, 0.62)); fem_trial.data.materials[1] = mat("MA_Trial")
# resected femur: bone slots + cut-bone slot
for p_ in fem_res.data.polygons:
    if p_.material_index > 2: p_.material_index = 2
fem_res.data.materials.clear()
for m in ("MA_Bone", "MA_Cartilage", "MA_CutBone"): fem_res.data.materials.append(mat(m))
REP["femur"] = {"u0_mm": round(u0 * 1000, 1), "a_p_mm": round(a_p * 1000, 1), "a_a_mm": round(a_a * 1000, 1), "componentTris": len(fem_comp.data.polygons)}

# ---------------------------------------------------------------- tibia: placeholder plateau resection, tray, insert
tib, nm_t = joined_closed(["ProximalTibia"], "ProximalTibia_Closed")
REP["proximalTibiaNonManifoldEdges"] = nm_t
Q = coords(tib) - np.array(knee[:]); h = Q @ np.array(T_pro[:]); tl = Q @ np.array(T_lat[:]); ta = Q @ np.array(T_ant[:])
plateau = h > h.max() - 0.03
latmask = plateau & (tl > np.median(tl[plateau]) + 0.008)
h0 = h[latmask].max() - PH["tibial_resection_mm"] / 1000
cut_co = knee + T_pro * h0
tib_res = dup(tib, "ProximalTibia_Resected")
bmx = bmesh.new(); bmx.from_mesh(tib_res.data)
r = bmesh.ops.bisect_plane(bmx, geom=bmx.verts[:] + bmx.edges[:] + bmx.faces[:], plane_co=cut_co, plane_no=T_pro, clear_outer=True)
cut_edges = [x for x in r["geom_cut"] if isinstance(x, bmesh.types.BMEdge)]
# outline of the cut (largest loop) for the tray / insert
def loops_from_edges(edges):
    adj = {}
    for e in edges:
        a_, b_ = e.verts; adj.setdefault(a_, []).append(b_); adj.setdefault(b_, []).append(a_)
    seen = set(); loops = []
    for start in adj:
        if start in seen: continue
        loop = [start]; seen.add(start); prev = None; cur = start
        while True:
            nxt = [x for x in adj[cur] if x is not prev and x not in seen]
            if not nxt: break
            prev, cur = cur, nxt[0]; loop.append(cur); seen.add(cur)
        loops.append([v.co.copy() for v in loop])
    return loops
loops = loops_from_edges(cut_edges); outline = max(loops, key=len)
fill = bmesh.ops.contextual_create(bmx, geom=cut_edges)
cutmat_idx = len(tib_res.data.materials)
for f in fill.get("faces", []): f.material_index = 2
bmx.to_mesh(tib_res.data); bmx.free()
tib_res.data.materials.clear()
for m in ("MA_Bone", "MA_Cartilage", "MA_CutBone"): tib_res.data.materials.append(mat(m))
# 2D outline in the tibial frame (x = lateral, y = anterior)
pts2 = np.array([((p - cut_co).dot(T_lat), (p - cut_co).dot(T_ant)) for p in outline])
ang = np.arctan2(pts2[:, 1] - pts2[:, 1].mean(), pts2[:, 0] - pts2[:, 0].mean()); pts2 = pts2[np.argsort(ang)]
def resample(poly, n=96, smooth=3, inset=0.0):
    poly = np.vstack([poly, poly[:1]]); seg = np.linalg.norm(np.diff(poly, axis=0), axis=1); s = np.r_[0, np.cumsum(seg)]
    t = np.linspace(0, s[-1], n, endpoint=False); x = np.interp(t, s, poly[:, 0]); y = np.interp(t, s, poly[:, 1]); q = np.c_[x, y]
    for _ in range(smooth): q = (np.roll(q, 1, 0) + q * 2 + np.roll(q, -1, 0)) / 4
    if inset:
        cen = q.mean(0); d = q - cen; q = cen + d * (1 - inset / np.linalg.norm(d, axis=1, keepdims=True))
    return q
tray_outline = resample(pts2, inset=0.0015); insert_outline = resample(pts2, inset=0.0025)
cen2 = tray_outline.mean(0)
pivot_tray = cut_co + T_lat * cen2[0] + T_ant * cen2[1]
def to3(xy, hh, origin): return origin + T_lat * xy[0] + T_ant * xy[1] + T_pro * hh
def point_in_poly(x, y, poly):
    n = len(poly); inside = False; j = n - 1
    for i in range(n):
        xi, yi = poly[i]; xj, yj = poly[j]
        if ((yi > y) != (yj > y)) and (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi): inside = not inside
        j = i
    return inside
def slab(name, outline2, h_bot, h_top, materials, origin, height_fn=None, grid=0.0022, bevel=0.0006):
    n = len(outline2); pts = [tuple(p) for p in outline2]
    for x in np.arange(outline2[:, 0].min() + grid, outline2[:, 0].max(), grid):
        for y in np.arange(outline2[:, 1].min() + grid, outline2[:, 1].max(), grid):
            if point_in_poly(x, y, outline2):
                dmin_ = np.min(np.linalg.norm(outline2 - np.array([x, y]), axis=1))
                if dmin_ > grid * 0.6: pts.append((x, y))
    vco, eout, fout, orig_v, _, _ = delaunay_2d_cdt([Vector(p) for p in pts], [], [list(range(n))], 1, 1e-7)
    bmx = bmesh.new(); top_v = []; bot_v = []
    for p in vco:
        ht = h_top + (height_fn(p.x, p.y) if height_fn else 0.0)
        top_v.append(bmx.verts.new(to3(p, ht, origin))); bot_v.append(bmx.verts.new(to3(p, h_bot, origin)))
    for f in fout:
        bmx.faces.new([top_v[i] for i in f]); bmx.faces.new([bot_v[i] for i in reversed(f)])
    # side walls along the outline (first n points of vco keep the outline order for CDT input)
    o2n = {}
    for j, ids in enumerate(orig_v):
        for i in ids: o2n[i] = j
    ring = [o2n.get(i) for i in range(n)]
    for i in range(n):
        a_, b_ = ring[i], ring[(i + 1) % n]
        if a_ is None or b_ is None: continue
        try: f = bmx.faces.new((top_v[a_], top_v[b_], bot_v[b_], bot_v[a_])); f.material_index = 1 if len(materials) > 1 else 0
        except ValueError: pass
    bmesh.ops.remove_doubles(bmx, verts=bmx.verts, dist=1e-7)
    bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces)
    o = new_obj(name, bmx, materials)
    if bevel:
        bv = o.modifiers.new("bv", "BEVEL"); bv.width = bevel; bv.segments = 2; bv.limit_method = "ANGLE"; bv.angle_limit = math.radians(50)
        wn = o.modifiers.new("wn", "WEIGHTED_NORMAL"); wn.keep_sharp = True
        d = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(o.evaluated_get(d), depsgraph=d)
        o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old)
    return o
tt = PH["tray_thickness_mm"] / 1000; it_ = PH["insert_thickness_mm"] / 1000; dd = PH["insert_dish_depth_mm"] / 1000
tray = slab("TibialTray", tray_outline, 0.0, tt, [mat("MA_ImplantTi", (0.62, 0.63, 0.66))], cut_co)
# keel: stem (Ø12 x 38 mm) + two fins, unioned
stem = cyl("stem", pivot_tray + T_pro * 0.0005, -T_pro, 0.006, 0.038, 28)
for side in (-1, 1):
    bmx = bmesh.new(); bmesh.ops.create_cube(bmx, size=1.0)
    finc = pivot_tray - T_pro * 0.012 + T_lat * side * 0.014
    S_ = Matrix.Diagonal((0.022, 0.024, 0.0022, 1.0))
    Rf = Matrix((T_lat, -T_pro, T_ant)).transposed().to_4x4()
    bmx.transform(Matrix.Translation(finc) @ Rf @ Matrix.Rotation(math.radians(-side * 18), 4, "Z") @ S_)
    fin = new_obj("fin", bmx, mat("MA_ImplantTi")); boolean(stem, fin, "UNION"); bpy.data.objects.remove(fin)
boolean(tray, stem, "UNION"); bpy.data.objects.remove(stem)
tray.data.materials.clear(); tray.data.materials.append(mat("MA_ImplantTi"))
tib_trial = slab("TibialTrial", tray_outline, 0.0, tt, [mat("MA_Trial")], cut_co)
cl = np.percentile(tray_outline[:, 0], [28, 72]); cy = cen2[1] - 0.004
def dish(x, y):
    g = lambda cx: math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * 0.011 ** 2))
    return -dd * max(g(cl[0]), g(cl[1])) + 0.0012 * math.exp(-((x - cen2[0]) ** 2) / (2 * 0.004 ** 2))
insert = slab("PolyInsert", insert_outline, tt, tt + it_, [mat("MA_Poly", (0.93, 0.93, 0.90))], cut_co, height_fn=dish)
REP["tibia"] = {"h0_mm": round(h0 * 1000, 1), "outlinePts": len(outline), "trayTris": len(tray.data.polygons), "insertTris": len(insert.data.polygons)}

# ---------------------------------------------------------------- guide pins (placeholder) for guide_placed
def pins(name, base_pts, axis, length=0.065, out=0.035):
    bmx = bmesh.new()
    for bp in base_pts:
        t = bmesh.new(); bmesh.ops.create_cone(t, cap_ends=True, segments=16, radius1=0.0016, radius2=0.0016, depth=length)
        q = Vector((0, 0, 1)).rotation_difference(axis).to_matrix().to_4x4()
        t.transform(Matrix.Translation(bp + axis * (out - length / 2)) @ q); m2 = bpy.data.meshes.new("t"); t.to_mesh(m2); t.free(); bmx.from_mesh(m2); bpy.data.meshes.remove(m2)
    return new_obj(name, bmx, mat("MA_Steel", (0.7, 0.72, 0.75)))
def surface_point(tree, origin, direction, back=0.1):
    hit = tree.ray_cast(origin + direction * back, -direction)
    return hit[0] if hit[0] is not None else origin
def tree_of(names):
    b = bmesh.new()
    for n in names: b.from_mesh(O[n].data)
    return BVHTree.FromBMesh(b)
ftree = tree_of(["DistalFemur", "FemoralCartilage", "FemurShaft"]); ttree = tree_of(["ProximalTibia", "TibialCartilage", "TibiaShaft"])
fp = [surface_point(ftree, W(u0 + 0.035, 0, s_ * 0.012), F_ant) for s_ in (-1, 1)]
tp_ = [surface_point(ttree, knee + T_pro * (h0 - 0.022) + T_lat * s_ * 0.012, T_ant) for s_ in (-1, 1)]
pins_f = pins("GuidePins_Femur", fp, F_ant); pins_t = pins("GuidePins_Tibia", tp_, T_ant)
guide_f = (surface_point(ftree, W(u0, 0, notch_l), F_ant), F_pro, F_ant)
guide_t = (surface_point(ttree, cut_co, T_ant), T_pro, T_ant)

# ---------------------------------------------------------------- patella eversion pose (placeholder) for "exposed"
pat_c = Vector(coords(O["Patella"]).mean(0)); tub = LM["tibialTuberosity"]; axis = (pat_c - tub).normalized()
best = None
_sk = bmesh.new(); _sk.from_mesh(O["KneeSkin"].data); _sk.normal_update(); skin_only = BVHTree.FromBMesh(_sk); _sk.free()
ant_up = (F_ant + T_ant).normalized()
pat_samples = list(O["Patella"].data.vertices)[::5] + list(O["PatellarTendon"].data.vertices)[::5]
for sign in (1, -1):
    for angle in (20, 35, 50, 65):
        for shift in (0.02, 0.028, 0.036, 0.044):
            for lift in (-0.012, -0.006, 0.0, 0.004):
                Mv = Matrix.Translation(F_lat * shift + F_ant * lift) @ Matrix.Translation(tub) @ Matrix.Rotation(math.radians(sign * angle), 4, axis) @ Matrix.Translation(-tub)
                pen = out = 0
                for v in pat_samples:
                    p = Mv @ v.co; hh = ftree.find_nearest(p)
                    if hh[0] is not None and (p - hh[0]).dot(hh[1]) < 0: pen += 1
                    hs = skin_only.ray_cast(p, ant_up, 0.25)   # under the (intact) skin envelope?
                    if hs[0] is None or (hs[0] - p).length < 0.002: out += 1
                car2 = Mv @ Vector(coords(O["PatellarCartilage"]).mean(0)); facing = (car2 - Mv @ pat_c).normalized().dot(F_ant)
                score = pen * 4 + out * 6 - facing * 2
                if best is None or score < best[0]: best = (score, sign * angle, shift + 1j * lift, pen, facing, out)
_, ev_sign, ev_shift, ev_pen, ev_face, ev_out = best
ev_lift = ev_shift.imag; ev_shift = ev_shift.real

REP["patellaEversion"] = {"angleDeg": ev_sign, "lateralShift_m": ev_shift, "lift_m": ev_lift, "femurPenetratingSamples": ev_pen, "samplesNearOrAboveSkin": ev_out, "articularFacingAnterior": round(ev_face, 2)}

# ---------------------------------------------------------------- incision path, skin state geometry
skin_names = ["KneeSkin", "RightLeg_Prepped", "Body"]
stree = tree_of(skin_names)
pat_top = Vector(coords(O["Patella"])[np.argmax(coords(O["Patella"]) @ np.array(F_pro[:]))])
ctrl = [pat_top + F_pro * 0.065, pat_top + F_pro * 0.02, pat_c, LM["patellarApex"], tub - LM["tibiaDistalDir"] * -0.012]
path = []
for i in range(len(ctrl) - 1):
    a0, a1 = ctrl[i], ctrl[i + 1]; n_ = max(2, int((a1 - a0).length / 0.004))
    for k in range(n_):
        q = a0.lerp(a1, k / n_); t = (q - knee).dot(F_pro)
        d = (F_ant if t > 0.01 else T_ant if t < -0.03 else (F_ant + T_ant).normalized())
        hit = stree.ray_cast(q + d * 0.12, -d)
        if hit[0] is not None: path.append(hit[0].copy())
path.append(path[-1])
P_ = np.array([p[:] for p in path])
for _ in range(4): P_[1:-1] = (P_[:-2] + 2 * P_[1:-1] + P_[2:]) / 4
path = [Vector(p) for p in P_]
path = [stree.find_nearest(p)[0].copy() if stree.find_nearest(p)[0] is not None else p for p in path]   # back onto the skin after smoothing
L_cum = np.r_[0, np.cumsum(np.linalg.norm(np.diff(P_, axis=0), axis=1))]; REP["incisionLength_mm"] = round(float(L_cum[-1]) * 1000, 1)
def surf_n(p):
    hh = stree.find_nearest(p); return hh[1].normalized() if hh[1] is not None else Vector((0, 0, 1))
def ribbon(name, width, lift, material, every=None, tick=0.0):
    bmx = bmesh.new(); prev = None
    for i, p in enumerate(path):
        tdir = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized(); n = surf_n(p); side = tdir.cross(n).normalized()
        c = p + n * lift; v = (bmx.verts.new(c - side * width / 2), bmx.verts.new(c + side * width / 2))
        if prev: bmx.faces.new((prev[0], prev[1], v[1], v[0]))
        prev = v
    if every:
        for k in np.arange(every / 2, L_cum[-1], every):
            i = int(np.searchsorted(L_cum, k)); i = min(max(i, 1), len(path) - 2); p = path[i]
            tdir = (path[i + 1] - path[i - 1]).normalized(); n = surf_n(p); side = tdir.cross(n).normalized(); c = p + n * lift
            q = [c - side * tick / 2 - tdir * width / 2, c + side * tick / 2 - tdir * width / 2, c + side * tick / 2 + tdir * width / 2, c - side * tick / 2 + tdir * width / 2]
            bmx.faces.new([bmx.verts.new(x) for x in q])
    return new_obj(name, bmx, mat(material))
site_mark = ribbon("SiteMarking", 0.0018, 0.00035, "MA_SkinMarker", every=0.02, tick=0.009)
incision = ribbon("IncisionLine", 0.0016, 0.00025, "MA_Incision")
closure_line = ribbon("ClosureLine", 0.0011, 0.00025, "MA_SutureLine")
# staples every 11 mm
bmx = bmesh.new()
for k in np.arange(0.008, L_cum[-1] - 0.004, 0.011):
    i = int(np.searchsorted(L_cum, k)); i = min(max(i, 1), len(path) - 2); p = path[i]
    tdir = (path[i + 1] - path[i - 1]).normalized(); n = surf_n(p); side = tdir.cross(n).normalized()
    w = 0.0035; crown = 0.0007; r = 0.00035
    ptsU = [p + side * -w - n * 0.0012, p + side * -w + n * crown, p + side * w + n * crown, p + side * w - n * 0.0012]
    for j in range(3):
        a0, a1 = ptsU[j], ptsU[j + 1]; ax = (a1 - a0); L = ax.length
        t = bmesh.new(); bmesh.ops.create_cone(t, cap_ends=True, segments=8, radius1=r, radius2=r, depth=L)
        q = Vector((0, 0, 1)).rotation_difference(ax.normalized()).to_matrix().to_4x4(); t.transform(Matrix.Translation((a0 + a1) / 2) @ q)
        m2 = bpy.data.meshes.new("t"); t.to_mesh(m2); t.free(); bmx.from_mesh(m2); bpy.data.meshes.remove(m2)
staples = new_obj("SkinStaples", bmx, mat("MA_Steel"))
# opening: lens-shaped window around the incision path + wound walls
ks = O["KneeSkin"]; ko = dup(ks, "KneeSkin_Open")
kd = KDTree(len(path))
for i, p in enumerate(path): kd.insert(p, i)
kd.balance()
def halfwidth(i): t = L_cum[i] / L_cum[-1]; return 0.034 * max(0.0, math.sin(math.pi * t)) ** 0.65
bmx = bmesh.new(); bmx.from_mesh(ko.data); bmx.faces.ensure_lookup_table()
_ob = [v.co.copy() for v in bmx.verts if v.is_boundary]
border_kd = KDTree(len(_ob))
for i_, c_ in enumerate(_ob): border_kd.insert(c_, i_)
border_kd.balance()
kill = []
for f in bmx.faces:
    cpos = f.calc_center_median(); co, i, d = kd.find(cpos)
    if d < halfwidth(i): kill.append(f)
bmesh.ops.delete(bmx, geom=kill, context="FACES")
# smooth the opening edge and project it back on the skin
orig_tree = BVHTree.FromObject(ks, bpy.context.evaluated_depsgraph_get())
bmx.verts.ensure_lookup_table()
ring = [v for v in bmx.verts if v.is_boundary and border_kd.find(v.co)[2] > 1e-6]
ring_set = set(ring)
for _ in range(8):
    newp = {}
    for v in ring:
        nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
        if len(nb) == 2: newp[v] = (v.co * 2 + nb[0].co + nb[1].co) / 4
    for v, p in newp.items():
        hh = orig_tree.find_nearest(p); v.co = hh[0] if hh[0] is not None else p
# wound walls: extrude the window rim inward (subcutaneous fat, then deeper tissue)
rim_edges = [e for e in bmx.edges if e.is_boundary and all(v in ring_set for v in e.verts)]
bmx.normal_update()
wall_mat_fat = len(ko.data.materials);
ko.data.materials.append(mat("MA_WoundFat", (0.92, 0.80, 0.45))); ko.data.materials.append(mat("MA_WoundTissue", (0.70, 0.30, 0.28)))
def extrude_in(edges, depth, mat_index, shrink):
    ret = bmesh.ops.extrude_edge_only(bmx, edges=edges)
    nv = [x for x in ret["geom"] if isinstance(x, bmesh.types.BMVert)]
    ne = [x for x in ret["geom"] if isinstance(x, bmesh.types.BMEdge) and x.is_boundary]
    for v in nv:
        co_, i_, d_ = kd.find(v.co); n = surf_n(v.co)
        to_path = (path[i_] - v.co); to_path -= n * to_path.dot(n)
        v.co += -n * depth + (to_path.normalized() * shrink if to_path.length > 1e-6 else Vector())
    for f in ret["geom"]:
        if isinstance(f, bmesh.types.BMFace): f.material_index = mat_index; f.smooth = True
    return ne
e1 = extrude_in(rim_edges, 0.006, wall_mat_fat, 0.002)
e2 = extrude_in(e1, 0.005, wall_mat_fat + 1, 0.003)
bmesh.ops.recalc_face_normals(bmx, faces=bmx.faces)
bmx.to_mesh(ko.data); bmx.free()
for p in ko.data.polygons: p.use_smooth = True
REP["opening"] = {"removedFaces": len(kill), "rimEdges": len(rim_edges)}

# ---------------------------------------------------------------- unify skin normals across Body / RightLeg_Prepped / KneeSkin(*)
def unify_normals(names):
    b = bmesh.new()
    for n in names: b.from_mesh(O[n].data)
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=1e-5); b.normal_update()
    kd2 = KDTree(len(b.verts)); nr = []
    for i, v in enumerate(b.verts): kd2.insert(v.co, i); nr.append(v.normal.copy())
    kd2.balance(); b.free()
    for n in names + ["KneeSkin_Open"]:
        me = O[n].data; vn = []
        for v in me.vertices:
            co, i, d = kd2.find(v.co); vn.append(nr[i] if d < 1e-4 else v.normal.copy())
        me.normals_split_custom_set_from_vertices(vn)
unify_normals(["Body", "RightLeg_Prepped", "KneeSkin"])

# ---------------------------------------------------------------- pivots, export
def B2U(v): return [-v[0], v[2], -v[1]]   # Blender -> Unity (FBX export with axis_forward=-Z, axis_up=Y)
def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p
def center(o): return Vector(coords(o).mean(0))
def bot(o, d):  # extreme point of a mesh along direction d
    c = coords(o); return Vector(c[np.argmax(c @ np.array(d[:]))])
pivot_fcomp = W(u0, a_mid, notch_l)
pivot_insert = pivot_tray + T_pro * tt
origins = {
    "FemurShaft": knee + F_pro * 0.15, "DistalFemur": pivot_fcomp, "DistalFemur_Resected": pivot_fcomp, "FemoralCartilage": pivot_fcomp,
    "TibiaShaft": knee - T_pro * 0.13, "ProximalTibia": pivot_tray, "ProximalTibia_Resected": pivot_tray, "TibialCartilage": pivot_tray,
    "Patella": pat_c, "PatellarCartilage": pat_c, "PatellarTendon": tub,
    "GuidePins_Femur": guide_f[0], "GuidePins_Tibia": guide_t[0],
    "FemoralComponent": pivot_fcomp, "FemoralTrial": pivot_fcomp, "TibialTray": pivot_tray, "TibialTrial": pivot_tray, "PolyInsert": pivot_insert,
}
for n in ["Fibula", "MedialMeniscus", "LateralMeniscus", "ACL", "PCL", "MCL", "LCL", "TransverseLigament", "MeniscotibialLigaments", "PoplitealLigaments",
          "PatellarRetinacula", "JointCapsule", "InfrapatellarFatPad", "SuprapatellarBursa", "Quadriceps", "Gastrocnemius", "Popliteus", "PesAnserinus", "Hamstrings",
          "IliotibialTract", "SiteMarking", "IncisionLine", "ClosureLine", "SkinStaples", "KneeSkin", "KneeSkin_Open"]:
    if n in O: origins[n] = center(O[n])
if "Fibula" in O: origins["Fibula"] = bot(O["Fibula"], T_pro)   # fibular head apex
exec(open(r"D:/Medical-Test/scratch_surgery/uvbox.py").read())
for _o in [o for o in bpy.data.objects if o.type == "MESH"]: box_uv(_o, 0.04)
# implants: export standalone in their own frames (+X lateral, +Y proximal, +Z anterior in Unity)
def export_implant(o, pivot, lat, pro, ant, fname):
    orig_name = o.name; o.name = orig_name + "_InKnee"
    c = dup(o, orig_name + "_X")
    Mloc = Matrix(((-lat).to_tuple(), (-ant).to_tuple(), pro.to_tuple())).to_4x4()   # rows: Blender-local x=-lx, y=-lz, z=ly
    c.data.transform(Mloc @ Matrix.Translation(-pivot)); c.location = (0, 0, 0); c.name = orig_name
    bpy.ops.object.select_all(action="DESELECT"); c.select_set(True); bpy.context.view_layer.objects.active = c
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_I, fname), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH"}, use_tspace=True)
    bpy.data.objects.remove(c); o.name = orig_name
    bb = coords(o) - np.array(pivot[:])
    return {"size_mm": [round(float(x) * 1000, 1) for x in (np.ptp(bb @ np.array(lat[:])), np.ptp(bb @ np.array(pro[:])), np.ptp(bb @ np.array(ant[:])))], "tris": len(o.data.polygons)}
IMPL = {}
IMPL["implant.femoral_component"] = export_implant(O["FemoralComponent"], pivot_fcomp, F_lat, F_pro, F_ant, "implant.femoral_component.fbx")
IMPL["implant.femoral_trial"] = export_implant(O["FemoralTrial"], pivot_fcomp, F_lat, F_pro, F_ant, "implant.femoral_trial.fbx")
IMPL["implant.tibial_tray"] = export_implant(O["TibialTray"], pivot_tray, T_lat, T_pro, T_ant, "implant.tibial_tray.fbx")
IMPL["implant.tibial_trial"] = export_implant(O["TibialTrial"], pivot_tray, T_lat, T_pro, T_ant, "implant.tibial_trial.fbx")
IMPL["implant.poly_insert"] = export_implant(O["PolyInsert"], pivot_insert, T_lat, T_pro, T_ant, "implant.poly_insert.fbx")
REP["implants"] = IMPL
# knee objects: origins relative to the knee centre (= ANCHOR_SurgicalSite_Knee_R)
knee_objs = [o for o in O if o.type == "MESH" and o.name not in ("Body", "RightLeg_Prepped", "Brief", "Gown", "Hair", "Eyes", "Eyebrows", "Eyelashes", "Teeth", "Tongue",
             "DistalFemur_Closed", "ProximalTibia_Closed")]
for o in knee_objs:
    o.data.transform(Matrix.Translation(-knee)); p = origins.get(o.name, center(o) + knee) - knee
    set_origin(o, p)
bpy.ops.object.select_all(action="DESELECT")
for o in knee_objs: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_K, "KneeAnatomy_R.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH"}, use_tspace=True)
REP["kneeObjects"] = {o.name: len(o.data.polygons) for o in knee_objs}
# patient (body without the knee patch; skin pushed + unified normals)
bpy.ops.object.select_all(action="DESELECT")
pat = [O[n] for n in ("Body", "RightLeg_Prepped", "Brief", "Gown", "Hair", "Eyes", "Eyebrows", "Eyelashes", "Teeth", "Tongue") if n in O]
for o in pat: o.select_set(True)
for e in [o for o in O if o.type == "EMPTY"]: e.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_P, "SurgicalPatient_Body.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False, object_types={"MESH", "EMPTY"}, use_tspace=True)

# ---------------------------------------------------------------- landmarks/anchors for Unity (knee-root relative, Unity coordinates)
def U(p): return B2U(p - knee)
def Ud(d): return B2U(d)
anchors = {
    "kneeCenter_patient": B2U(knee),
    "femur": {"pivot": U(pivot_fcomp), "lateral": Ud(F_lat), "proximal": Ud(F_pro), "anterior": Ud(F_ant)},
    "tibia": {"pivot": U(pivot_tray), "lateral": Ud(T_lat), "proximal": Ud(T_pro), "anterior": Ud(T_ant)},
    "PlacementTarget_implant.femoral_component": {"pos": U(pivot_fcomp), "up": Ud(F_pro), "forward": Ud(F_ant)},
    "PlacementTarget_implant.femoral_trial": {"pos": U(pivot_fcomp), "up": Ud(F_pro), "forward": Ud(F_ant)},
    "PlacementTarget_implant.tibial_tray": {"pos": U(pivot_tray), "up": Ud(T_pro), "forward": Ud(T_ant)},
    "PlacementTarget_implant.tibial_trial": {"pos": U(pivot_tray), "up": Ud(T_pro), "forward": Ud(T_ant)},
    "PlacementTarget_implant.poly_insert": {"pos": U(pivot_insert), "up": Ud(T_pro), "forward": Ud(T_ant)},
    "GuideTarget_inst.cutting_guide_femoral": {"pos": U(guide_f[0]), "up": Ud(F_pro), "forward": Ud(-F_ant)},
    "GuideTarget_inst.cutting_guide_tibial": {"pos": U(guide_t[0]), "up": Ud(T_pro), "forward": Ud(-T_ant)},
    "PatellaEversion": {"pivot": U(tub), "axis": Ud(axis), "angleDeg": ev_sign, "offset": Ud(F_lat * ev_shift + F_ant * ev_lift)},
    "incisionPath": [U(p) for p in path[::3]],
    "patellaCenter": U(pat_c), "tibialTuberosity": U(tub), "distalCutCenter": U(W(u0, a_mid, notch_l)), "tibialCutCenter": U(pivot_tray),
}
json.dump({"anchors": anchors, "report": REP}, open(os.path.join(OUT_K, "KneeAnatomy_R.landmarks.json"), "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "knee_final.blend"))
print("KNEE2_OK", json.dumps(REP)[:4000])
