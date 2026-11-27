# MedSim AI — Surgery (Person 2): modular RIGHT knee from Z-Anatomy (CC BY-SA 4.0, derived from BodyParts3D),
# posed to the surgical patient's flexed knee and fitted into the MPFB leg.
# Usage: blender -b --disable-autoexec Startup.blend --python build_knee.py -- <work_dir> <stage>
#   stage "pose": extract + pose + fit + clearance report + save knee_posed.blend
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
WORK = argv[0]
REP = {}
dg = bpy.context.evaluated_depsgraph_get()

# ------------------------------------------------------------------ source structures (Z-Anatomy object names)
SRC = {
    # id: (source names, group, subdiv levels)
    "Femur": (["Femur.r"], "bone", 2), "Tibia": (["Tibia.r"], "bone", 2), "Fibula": (["Fibula.r"], "bone", 1), "Patella": (["Patella.r"], "bone", 2),
    "MedialMeniscus": (["Medial meniscus.r"], "meniscus", 1), "LateralMeniscus": (["Lateral meniscus.r"], "meniscus", 1),
    "ACL": (["Anterior cruciate ligament.r"], "ligament", 1), "PCL": (["Posterior cruciate ligament.r"], "ligament", 1),
    "MCL": (["Superficial part of tibial collateral ligament.r", "Deep part of tibial collateral ligament.r"], "ligament", 0),
    "LCL": (["Fibular collateral ligament.r"], "ligament", 1),
    "TransverseLigament": (["Transverse ligament of knee.r"], "ligament", 0),
    "MeniscotibialLigaments": (["Anterior meniscotibial ligament (Lateral meniscus).r", "Anterior meniscotibial ligament (Medial meniscus).r",
                                "Posterior meniscotibial ligament (Lateral meniscus).r", "Posterior meniscotibial ligament (Medial meniscus).r"], "ligament", 0),
    "PoplitealLigaments": (["Oblique popliteal ligament.r", "Arcuate popliteal ligament.r", "Popliteofibular ligament.r"], "ligament", 0),
    "PatellarRetinacula": (["Medial patellar retinaculum.r", "Lateral patellar retinaculum.r", "Meniscopatellar ligament.r"], "ligament", 1),
    "JointCapsule": (["Articular capsule of knee joint.r"], "capsule", 0),
    "InfrapatellarFatPad": (["Infrapatellar fat pad.r"], "fat", 0),
    "SuprapatellarBursa": (["Suprapatellar bursa.r"], "capsule", 0),
    "Quadriceps": (["Rectus femoris muscle.r", "Vastus lateralis muscle.r", "Vastus medialis muscle.r", "Vastus intermedius muscle.r"], "muscle", 0),
    "Gastrocnemius": (["Medial head of gastrocnemius.r", "Lateral head of gastrocnemius.r"], "muscle", 0),
    "Popliteus": (["Popliteus muscle.r"], "muscle", 0),
    "PesAnserinus": (["Sartorius muscle.r", "Gracilis muscle.r", "Semitendinosus muscle.r"], "muscle", 0),
    "Hamstrings": (["Long head of biceps femoris.r", "Short head of biceps femoris.r", "Semimembranosus muscle.r"], "muscle", 0),
    "IliotibialTract": (["Iliotibial tract.r"], "muscle", 0),
}
TRIM = {"Quadriceps": 0.30, "Gastrocnemius": 0.30, "Popliteus": 0.30, "PesAnserinus": 0.30, "Hamstrings": 0.30, "IliotibialTract": 0.30}
OPTIONAL = {"Gastrocnemius", "Popliteus", "PesAnserinus", "Hamstrings", "IliotibialTract"}   # deep posterior / side muscles: shipped inactive

def baked(names):
    me_all = []
    for n in names:
        o = bpy.data.objects.get(n)
        if o is None: REP.setdefault("missing", []).append(n); continue
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=False, depsgraph=dg)
        me.transform(o.matrix_world)
        me_all.append((me, [s.material.name if s.material else "" for s in o.material_slots]))
    return me_all

scene = bpy.context.scene
col = scene.collection
MATS = {}
def mat(name):
    if name not in MATS:
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name); MATS[name] = m
    return MATS[name]
GROUP_MAT = {"bone": "MA_Bone", "meniscus": "MA_Meniscus", "ligament": "MA_Ligament", "capsule": "MA_Capsule", "fat": "MA_Fat", "muscle": "MA_Muscle"}

objs = {}
for key, (names, group, sub) in SRC.items():
    parts = baked(names)
    if not parts: continue
    bmx = bmesh.new()
    for me, slots in parts:
        tmp = bmesh.new(); tmp.from_mesh(me)
        # material index: 0 = group material, 1 = cartilage (from Z-Anatomy "Cartilage" slots on bones)
        for f in tmp.faces:
            sname = slots[f.material_index] if f.material_index < len(slots) else ""
            f.material_index = 1 if (group == "bone" and "Cartilage" in sname) else 0
        m2 = bpy.data.meshes.new("t"); tmp.to_mesh(m2); tmp.free(); bmx.from_mesh(m2); bpy.data.meshes.remove(m2); bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(key); bmx.to_mesh(me); bmx.free()
    o = bpy.data.objects.new(key, me); col.objects.link(o)
    me.materials.append(mat(GROUP_MAT[group]))
    if group == "bone": me.materials.append(mat("MA_Cartilage"))
    if group == "meniscus": me.materials[0] = mat("MA_Meniscus")
    o["group"] = group; o["subdiv"] = sub
    objs[key] = o
REP["structures"] = sorted(objs)
# drop the whole atlas now: every later depsgraph update only sees the knee
bpy.data.batch_remove([o for o in bpy.data.objects if o not in set(objs.values())])
for c in list(bpy.data.collections): bpy.data.collections.remove(c)
bpy.data.orphans_purge(do_recursive=True)

# ------------------------------------------------------------------ landmarks (Z-Anatomy standing frame: anterior -Y, right -X, up +Z)
def V(o): return np.array([v.co[:] for v in o.data.vertices])
F, T = V(objs["Femur"]), V(objs["Tibia"])
zf0 = F[:, 2].min(); zt1 = T[:, 2].max()
fd = F[F[:, 2] < zf0 + 0.02]; tp = T[T[:, 2] > zt1 - 0.015]
knee_za = Vector(((fd[:, 0].mean() + tp[:, 0].mean()) / 2, (fd[:, 1].mean() + tp[:, 1].mean()) / 2, (zf0 + zt1) / 2))
top = F[F[:, 2] > F[:, 2].max() - 0.05]; top = top[top[:, 0] > np.median(top[:, 0])]   # femoral head = medial part of the top
A = np.c_[2 * top, np.ones(len(top))]; b = (top ** 2).sum(1); c, *_ = np.linalg.lstsq(A, b, rcond=None)
hip_za = Vector(c[:3]); REP["femoralHeadRadius_mm"] = round(float(math.sqrt(c[3] + (c[:3] ** 2).sum())) * 1000, 1)
ank = T[T[:, 2] < T[:, 2].min() + 0.02]; ankle_za = Vector(ank.mean(0))
REP["za"] = {"knee": knee_za[:], "hip": hip_za[:], "ankle": ankle_za[:]}

# ------------------------------------------------------------------ patient joints (posed, Blender patient frame)
PJ = json.load(open(os.path.join(WORK, "patient_report.json")))["joints"]
hip_p, knee_p, ankle_p = Vector(PJ["Hip_R"]), Vector(PJ["Knee_R"]), Vector(PJ["Ankle_R"])
v1, v2 = (hip_p - knee_p).normalized(), (ankle_p - knee_p).normalized()
FLEX = 180 - math.degrees(math.acos(max(-1, min(1, v1.dot(v2)))))
REP["kneeFlexDeg_patient"] = round(FLEX, 1)

# ------------------------------------------------------------------ pose in the Z-Anatomy frame
def bvh_of(names):
    bm2 = bmesh.new()
    for n in names:
        if n in objs: bm2.from_mesh(objs[n].data)
    bm2.normal_update(); t = BVHTree.FromBMesh(bm2); return t
def rot_about(point, deg):
    return Matrix.Translation(point) @ Matrix.Rotation(math.radians(deg), 4, "X") @ Matrix.Translation(-point)
femur_bvh = bvh_of(["Femur"])
def penetration(M, names):
    worst, gap = 0.0, 9.0
    for n in names:
        for v in objs[n].data.vertices:
            p = M @ v.co; hit = femur_bvh.find_nearest(p)
            if hit[0] is None: continue
            d = (p - hit[0]).length; inside = (p - hit[0]).dot(hit[1]) < 0
            if inside: worst = max(worst, d)
            else: gap = min(gap, d)
    return worst, gap
# flexion (tibia moves posteriorly): rotation about +X by -FLEX in this frame (anterior is -Y)
best = None
for up in (0.016, 0.020, 0.024, 0.028):
    for back in (-0.004, 0.0, 0.004, 0.008, 0.012):
        C = knee_za + Vector((0, back, up)); M = rot_about(C, FLEX)
        w, g = penetration(M, ["Tibia"])
        score = w * 10 + max(0, g - 0.002)
        if best is None or score < best[0]: best = (score, C, w, g)
_, C_flex, pen, gap = best
M_tib = rot_about(C_flex, FLEX)
# align the tibial axis exactly with the patient's shank (knee -> ankle of the rig), rotating about the flexion-axis point
def frame0(lat, prox):
    prox = prox.normalized(); lat = (lat - prox * lat.dot(prox)).normalized(); return Matrix((lat, prox, lat.cross(prox))).transposed()
Rm0 = frame0(Vector((1, 0, 0)), hip_p - knee_p) @ frame0(Vector((-1, 0, 0)), hip_za - knee_za).transposed()
want_za = (Rm0.transposed() @ v2).normalized()
have_za = (M_tib.to_3x3() @ (ankle_za - knee_za)).normalized()
Q = have_za.rotation_difference(want_za).to_matrix().to_4x4()
M_tib = Matrix.Translation(C_flex) @ Q @ Matrix.Translation(-C_flex) @ M_tib
pen, gap = penetration(M_tib, ["Tibia"])
REP["flexAxisPoint_za"] = C_flex[:]; REP["tibiaFemurPenetration_mm"] = round(pen * 1000, 2); REP["tibiaFemurGap_mm"] = round(gap * 1000, 2)
REP["shankCorrectionDeg"] = round(math.degrees(have_za.angle(want_za)), 2)
# patella tracks the trochlea/condyles: rotate about the same axis by ~0.6 x flexion, then clear the femur by 1 mm
PAT_RATIO = 0.6
M_pat = rot_about(C_flex, FLEX * PAT_RATIO)
pc = Vector(V(objs["Patella"]).mean(0))
for i in range(80):   # seat the patella on the condyles: 0.8–1.5 mm clearance
    w, g = penetration(M_pat, ["Patella"])
    out = ((M_pat @ pc) - C_flex); out.x = 0; out.normalize()
    if w >= 0.0003: M_pat = Matrix.Translation(out * 0.0007) @ M_pat
    elif g > 0.0015: M_pat = Matrix.Translation(-out * min(0.002, g - 0.001)) @ M_pat
    else: break
REP["patellaClearance_mm"] = round(penetration(M_pat, ["Patella"])[1] * 1000, 2)

TIBIA_GROUP = {"Tibia", "Fibula", "MedialMeniscus", "LateralMeniscus", "TransverseLigament", "MeniscotibialLigaments", "Gastrocnemius_", "Popliteus_"}
PATELLA_GROUP = {"Patella"}
BLEND = {"ACL", "PCL", "MCL", "LCL", "PoplitealLigaments", "PatellarRetinacula", "JointCapsule", "InfrapatellarFatPad", "SuprapatellarBursa",
         "Quadriceps", "Gastrocnemius", "Popliteus", "PesAnserinus", "Hamstrings", "IliotibialTract"}
tib_bvh = bvh_of(["Tibia", "Fibula"]); pat_bvh = bvh_of(["Patella"])
# trim long muscles to the knee region before deformation
for key, r in TRIM.items():
    o = objs.get(key)
    if not o: continue
    bmx = bmesh.new(); bmx.from_mesh(o.data)
    bmesh.ops.delete(bmx, geom=[v for v in bmx.verts if (v.co - knee_za).length > r], context="VERTS"); bmx.to_mesh(o.data); bmx.free()
for key, o in objs.items():
    if key in TIBIA_GROUP: o.data.transform(M_tib); continue
    if key in PATELLA_GROUP: o.data.transform(M_pat); continue
    if key not in BLEND: continue
    for v in o.data.vertices:
        p = v.co.copy()
        d = [max(1e-4, (p - t.find_nearest(p)[0]).length) for t in (femur_bvh, tib_bvh, pat_bvh)]
        w = np.array([1 / x ** 3 for x in d]); w /= w.sum()
        v.co = (p * w[0]) + ((M_tib @ p) * w[1]) + ((M_pat @ p) * w[2])

# procedural patellar tendon: patella apex -> tibial tuberosity (posed)
P_pat = np.array([(M_pat @ v.co)[:] for v in objs["Patella"].data.vertices]) if False else V(objs["Patella"])
T_pos = V(objs["Tibia"])
dist_dir = np.array((M_tib.to_3x3() @ Vector((0, 0, -1)))[:])   # tibial distal direction after flexion
ant_tib = np.array((M_tib.to_3x3() @ Vector((0, -1, 0)))[:])
apex = Vector(P_pat[np.argmax(P_pat @ dist_dir)])
knee_tp = Vector(T_pos[np.argmin(T_pos @ dist_dir)])
reg = T_pos[((T_pos - np.array(knee_tp[:])) @ dist_dir > 0.03) & ((T_pos - np.array(knee_tp[:])) @ dist_dir < 0.07)]
tub = Vector(reg[np.argmax(reg @ ant_tib)])
def tendon(name, a, b, w0, w1, t0, lat):
    bmx = bmesh.new(); rings = 14; seg = 16; ax = (b - a); L = ax.length; ax.normalize()
    side = lat - ax * lat.dot(ax); side.normalize(); nrm = ax.cross(side)
    prev = None; first = None
    for i in range(rings + 1):
        t = i / rings; c = a + ax * (L * t)
        w = (w0 + (w1 - w0) * t) * (1 + 0.15 * math.sin(math.pi * t)); th = t0 * (1 - 0.25 * math.sin(math.pi * t))
        ring = [bmx.verts.new(c + side * (w / 2 * math.cos(2 * math.pi * k / seg)) + nrm * (th / 2 * math.sin(2 * math.pi * k / seg))) for k in range(seg)]
        if prev:
            for k in range(seg): bmx.faces.new((prev[k], prev[(k + 1) % seg], ring[(k + 1) % seg], ring[k]))
        else: first = ring
        prev = ring
    bmx.faces.new(list(reversed(first))); bmx.faces.new(prev)
    me = bpy.data.meshes.new(name); bmx.normal_update(); bmx.to_mesh(me); bmx.free()
    o = bpy.data.objects.new(name, me); col.objects.link(o); me.materials.append(mat("MA_Tendon")); o["group"] = "tendon"; o["subdiv"] = 1
    return o
objs["PatellarTendon"] = tendon("PatellarTendon", apex + Vector(ant_tib) * 0.004, tub + Vector(ant_tib) * 0.003, 0.030, 0.027, 0.006, Vector((-1, 0, 0)))
REP["patellarTendonLength_mm"] = round((tub - apex).length * 1000, 1)

# ------------------------------------------------------------------ similarity transform Z-Anatomy(posed) -> patient frame
def frame(lat, prox):
    prox = prox.normalized(); lat = (lat - prox * lat.dot(prox)).normalized(); third = lat.cross(prox)
    return Matrix((lat, prox, third)).transposed()
Fza = frame(Vector((-1, 0, 0)), hip_za - knee_za)
Fp = frame(Vector((1, 0, 0)), hip_p - knee_p)
s = (hip_p - knee_p).length / (hip_za - knee_za).length * 0.95   # 95 %: leaves soft-tissue room inside the MPFB leg
Rm = (Fp @ Fza.transposed()).to_4x4()
S = Matrix.Translation(knee_p) @ Rm @ Matrix.Scale(s, 4) @ Matrix.Translation(-knee_za)
REP["scale"] = round(s, 4)
# compare the posed tibial axis with the patient's shank
tib_dir_za = (M_tib.to_3x3() @ (ankle_za - knee_za)).normalized()
REP["shankAxisErrorDeg"] = round(math.degrees((Rm.to_3x3() @ tib_dir_za).angle(v2)), 2)
for o in objs.values(): o.data.transform(S)
LM = {"kneeCenter": knee_p, "hip": hip_p, "ankle": ankle_p, "flexAxisPoint": S @ C_flex,
      "patellarApex": S @ apex, "tibialTuberosity": S @ tub,
      "femurProximalDir": (hip_p - knee_p).normalized(), "tibiaDistalDir": (Rm.to_3x3() @ tib_dir_za).normalized(),
      "lateralDir": Vector((1, 0, 0))}
LM["femurAnteriorDir"] = (Rm.to_3x3() @ Vector((0, -1, 0))).normalized()
LM["tibiaAnteriorDir"] = (Rm.to_3x3() @ (M_tib.to_3x3() @ Vector((0, -1, 0)))).normalized()

# ------------------------------------------------------------------ clean planar sections for the muscles (capped)
def plane_trim(o, co, no):
    bmx = bmesh.new(); bmx.from_mesh(o.data)
    r = bmesh.ops.bisect_plane(bmx, geom=bmx.verts[:] + bmx.edges[:] + bmx.faces[:], plane_co=co, plane_no=no, clear_outer=True)
    edges = [e for e in r["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if edges:
        try: bmesh.ops.holes_fill(bmx, edges=edges, sides=0)
        except Exception: pass
    bmx.to_mesh(o.data); bmx.free()
_fp = LM["femurProximalDir"]; _td = LM["tibiaDistalDir"]
for key, cuts in {"Quadriceps": [("f", 0.16)], "Hamstrings": [("f", 0.16)], "IliotibialTract": [("f", 0.16)], "PesAnserinus": [("f", 0.15), ("t", 0.11)],
                  "Gastrocnemius": [("t", 0.12)]}.items():
    o = objs.get(key)
    if not o: continue
    for which, dist in cuts:
        d = _fp if which == "f" else _td
        plane_trim(o, knee_p + d * dist, d)

# ------------------------------------------------------------------ smooth / densify (subdivision)
def apply_mods(o):
    d = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(d), preserve_all_data_layers=True, depsgraph=d)
    o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old); me.name = o.name
for key, o in objs.items():
    lv = o.get("subdiv", 0)
    if lv:
        m = o.modifiers.new("sd", "SUBSURF"); m.levels = lv; m.render_levels = lv
        apply_mods(o)
    for p in o.data.polygons: p.use_smooth = True
# split femur / tibia into the joint region (detailed, swappable for resection states) and the shaft (decimated)
def split_plane(o, co, no, keep_name, rest_name):
    a = o.copy(); a.data = o.data.copy(); a.name = rest_name; col.objects.link(a)
    for ob, inner in ((o, False), (a, True)):
        bmx = bmesh.new(); bmx.from_mesh(ob.data)
        bmesh.ops.bisect_plane(bmx, geom=bmx.verts[:] + bmx.edges[:] + bmx.faces[:], plane_co=co, plane_no=no, clear_inner=inner, clear_outer=not inner)
        bmx.to_mesh(ob.data); bmx.free()
    o.name = keep_name; o.data.name = keep_name
    return o, a
fpd = Vector(LM["femurProximalDir"]); tdd = Vector(LM["tibiaDistalDir"])
dfem, fshaft = split_plane(objs.pop("Femur"), knee_p + fpd * 0.15, fpd, "DistalFemur", "FemurShaft")
ptib, tshaft = split_plane(objs.pop("Tibia"), knee_p + tdd * 0.13, tdd, "ProximalTibia", "TibiaShaft")
objs.update({"DistalFemur": dfem, "FemurShaft": fshaft, "ProximalTibia": ptib, "TibiaShaft": tshaft})
for o in [fshaft, tshaft] + [x for x in objs.values() if x.get("group") == "muscle"]:
    d = o.modifiers.new("dec", "DECIMATE"); d.ratio = 0.18 if o in (fshaft, tshaft) else 0.5; apply_mods(o)
    for p in o.data.polygons: p.use_smooth = True
# cartilage zones become their own structures (bone keeps an exact matching hole border)
CART_T = 0.002   # articular cartilage thickness (m), educational approximation
def split_material(o, idx, name):
    """Bone stays CLOSED: its articular zone is inset by CART_T (subchondral surface). Cartilage becomes a separate
    CART_T-thick layer at the original articular surface (solidified inward), with clean edges."""
    c = o.copy(); c.data = o.data.copy(); c.name = name; col.objects.link(c)
    bmx = bmesh.new(); bmx.from_mesh(c.data)
    bmesh.ops.delete(bmx, geom=[f for f in bmx.faces if f.material_index != idx], context="FACES"); bmx.to_mesh(c.data); bmx.free()
    c.data.materials.clear(); c.data.materials.append(mat("MA_Cartilage")); c["group"] = "cartilage"
    for f in c.data.polygons: f.material_index = 0
    so = c.modifiers.new("so", "SOLIDIFY"); so.thickness = CART_T; so.offset = -1; so.use_even_offset = True
    apply_mods(c)
    for p_ in c.data.polygons: p_.use_smooth = True
    bmx = bmesh.new(); bmx.from_mesh(o.data); bmx.normal_update()
    inner = [v for v in bmx.verts if v.link_faces and all(f.material_index == idx for f in v.link_faces)]
    for v in inner: v.co -= v.normal * CART_T
    for f in bmx.faces: f.material_index = 0
    bmx.to_mesh(o.data); bmx.free()
    return c
objs["FemoralCartilage"] = split_material(objs["DistalFemur"], 1, "FemoralCartilage")
objs["TibialCartilage"] = split_material(objs["ProximalTibia"], 1, "TibialCartilage")
objs["PatellarCartilage"] = split_material(objs["Patella"], 1, "PatellarCartilage")
for n in ("FemurShaft", "TibiaShaft", "Fibula"):
    for f in objs[n].data.polygons: f.material_index = 0
REP["tris"] = {k: sum(len(p.vertices) - 2 for p in o.data.polygons) for k, o in objs.items()}
REP["trisTotal"] = sum(REP["tris"].values())

# ------------------------------------------------------------------ bring in the patient and measure skin clearance
with bpy.data.libraries.load(os.path.join(WORK, "patient_posed.blend"), link=False) as (src, dst):
    dst.objects = [n for n in src.objects]
pat_objs = [o for o in dst.objects if o]
pcol = bpy.data.collections.new("Patient"); scene.collection.children.link(pcol)
for o in pat_objs: pcol.objects.link(o)
skin_names = ["KneeSkin", "RightLeg_Prepped", "Body"]
def skin_bvh():
    bm2 = bmesh.new()
    for n in skin_names:
        o = bpy.data.objects[n]; t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); m2 = bpy.data.meshes.new("x"); t.to_mesh(m2); t.free(); bm2.from_mesh(m2); bpy.data.meshes.remove(m2)
    bm2.normal_update(); return BVHTree.FromBMesh(bm2)
def clearance(names, tree):
    out, mind = 0, 9.0
    for n in names:
        for v in objs[n].data.vertices:
            h = tree.find_nearest(v.co)
            if h[0] is None: continue
            d = (v.co - h[0]).length; inside = (v.co - h[0]).dot(h[1]) < 0
            if not inside: out += 1
            else: mind = min(mind, d)
    return out, mind
HARD = ["DistalFemur", "FemoralCartilage", "FemurShaft", "ProximalTibia", "TibialCartilage", "TibiaShaft", "Fibula", "Patella", "PatellarCartilage", "PatellarTendon", "Quadriceps", "MCL", "LCL", "JointCapsule"]
MIN_T = {"DistalFemur": 0.006, "FemoralCartilage": 0.006, "FemurShaft": 0.008, "ProximalTibia": 0.005, "TibialCartilage": 0.005, "TibiaShaft": 0.004, "Fibula": 0.006,
         "Patella": 0.006, "PatellarCartilage": 0.006, "PatellarTendon": 0.004, "Quadriceps": 0.004, "MCL": 0.003, "LCL": 0.003, "JointCapsule": 0.003}
REP["clearanceBefore"] = clearance(HARD, skin_bvh())
# Constrained relaxation of the skin around the knee. Seeds: skin vertices closer than MIN_T to any visible structure.
# Push along the SKIN normal (same normal for coincident border vertices of Body / RightLeg_Prepped / KneeSkin, taken
# from the merged surface), smooth falloff field (R = 3 cm, position-only so borders move identically), then relax the
# KneeSkin interior and re-push; repeat.
from mathutils.kdtree import KDTree
VIS = [n for n in objs if n not in OPTIONAL]
for n in VIS: MIN_T.setdefault(n, 0.003)
GROUPS = {"FemurG": ["DistalFemur", "FemoralCartilage", "FemurShaft"], "TibiaG": ["ProximalTibia", "TibialCartilage", "TibiaShaft"],
          "PatellaG": ["Patella", "PatellarCartilage"], "FibulaG": ["Fibula"], "TendonG": ["PatellarTendon"]}
trees = {}
for g, names in GROUPS.items():   # closed composites (cartilage re-joined to its bone) -> reliable inside/outside sign
    b = bmesh.new()
    for n in names: b.from_mesh(objs[n].data)
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=2e-5); bmesh.ops.recalc_face_normals(b, faces=b.faces); b.normal_update()
    trees[g] = BVHTree.FromBMesh(b); b.free()
in_group = {n for v in GROUPS.values() for n in v}
for n in VIS:
    if n in in_group: continue
    b = bmesh.new(); b.from_mesh(objs[n].data); b.normal_update(); trees[n] = BVHTree.FromBMesh(b); b.free()
SIGNED = set(GROUPS)
MIN_T.update({"FemurG": 0.006, "TibiaG": 0.005, "PatellaG": 0.006, "FibulaG": 0.006, "TendonG": 0.004})
R = 0.03
def smooth_k(d): x = max(0.0, 1 - d / R); return x * x * (3 - 2 * x)
def skin_normals():
    b = bmesh.new()
    for n in skin_names: b.from_mesh(bpy.data.objects[n].data)
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=1e-5); b.normal_update()
    kd = KDTree(len(b.verts)); nrm = []
    for i, v in enumerate(b.verts): kd.insert(v.co, i); nrm.append(v.normal.copy())
    kd.balance(); b.free(); return kd, nrm
def push_once():
    kdn, nrm = skin_normals(); seeds = []
    for n in skin_names:
        for v in bpy.data.objects[n].data.vertices:
            p = v.co
            if (p - knee_p).length > 0.36: continue
            need = 0.0
            for an, tr in trees.items():
                h = tr.find_nearest(p, 0.03)
                if h[0] is None: continue
                d = (p - h[0]).length
                inside = an in SIGNED and (p - h[0]).dot(h[1]) < 0   # thin/open soft tissue: unsigned distance only
                need = max(need, (MIN_T[an] if an in SIGNED else 0.002) + (d if inside else -d))
            if need > 0.0003:
                co, i, _ = kdn.find(p); seeds.append((p.copy(), need, nrm[i]))
    if not seeds: return 0
    kd = KDTree(len(seeds))
    for i, sd in enumerate(seeds): kd.insert(sd[0], i)
    kd.balance()
    for n in skin_names:
        for v in bpy.data.objects[n].data.vertices:
            if (v.co - knee_p).length > 0.40: continue
            mag = 0.0; dirv = Vector()
            for (co, idx, dist) in kd.find_range(v.co, R):
                sp, nd, nn = seeds[idx]; k = smooth_k(dist); mag = max(mag, nd * k); dirv += nn * (nd * k)
            if mag > 0 and dirv.length > 1e-9: v.co += dirv.normalized() * mag
    return len(seeds)
def relax_patch(iters=4):
    ks = bpy.data.objects["KneeSkin"]; bmx = bmesh.new(); bmx.from_mesh(ks.data)
    inner = [v for v in bmx.verts if not v.is_boundary and all(not e.is_boundary for e in v.link_edges)]
    for _ in range(iters): bmesh.ops.smooth_vert(bmx, verts=inner, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmx.to_mesh(ks.data); bmx.free()
relax_patch(6)
for it in range(6):
    n_seeds = push_once(); REP.setdefault("pushSeeds", []).append(n_seeds)
    if it < 5: relax_patch(3)
# Tighten the anterior knee skin onto the anatomy (MPFB's flexed knee leaves ~4–5 cm of "air" over the patella):
# interior KneeSkin vertices on the anterior / side half move toward the nearest structure until the soft-tissue depth
# is ~10 mm over bone / ~6 mm over tendon and quadriceps; weight falls to 0 within 3 cm of the patch border; then relax + re-push.
TIGHT = {"FemurG": 0.010, "TibiaG": 0.010, "PatellaG": 0.009, "FibulaG": 0.010, "TendonG": 0.006}
ant_k = (LM["femurAnteriorDir"] + LM["tibiaAnteriorDir"]).normalized()
def tighten(frac=0.6):
    ks = bpy.data.objects["KneeSkin"]; bmx = bmesh.new(); bmx.from_mesh(ks.data)
    border = [v.co.copy() for v in bmx.verts if v.is_boundary]
    kb = KDTree(len(border))
    for i, c_ in enumerate(border): kb.insert(c_, i)
    kb.balance(); moved = 0
    for v in bmx.verts:
        if v.is_boundary: continue
        dB = kb.find(v.co)[2]; wB = min(1.0, dB / 0.03); wB = wB * wB * (3 - 2 * wB)
        side = (v.co - knee_p).normalized().dot(ant_k); wA = min(1.0, max(0.0, (side + 0.35) / 0.5))
        w = wB * wA
        if w <= 0: continue
        best = None
        for an, tr in trees.items():
            h = tr.find_nearest(v.co, 0.08)
            if h[0] is None: continue
            d = (v.co - h[0]).length
            if an in SIGNED and (v.co - h[0]).dot(h[1]) < 0: continue
            excess = d - (TIGHT.get(an, 0.008) if an in SIGNED else 0.003)
            if best is None or excess < best[0]: best = (excess, h[0])
        if best and best[0] > 0:
            dirv = (best[1] - v.co).normalized(); v.co += dirv * best[0] * frac * w; moved += 1
    bmx.to_mesh(ks.data); bmx.free(); return moved
def _top():
    ks = bpy.data.objects['KneeSkin']; t = max((v.co for v in ks.data.vertices), key=lambda c: (c - knee_p).dot(ant_k)); return round((t - knee_p).dot(ant_k) * 1000, 1)
for it in range(5):
    REP.setdefault("tightened", []).append(tighten())
    relax_patch(3)
    REP.setdefault("pushSeeds2", []).append(push_once())
REP["clearanceAfter"] = clearance(HARD, skin_bvh()); REP["finalSeeds"] = push_once.__name__ and 0

for k, v in list(LM.items()): LM[k] = list(v)
json.dump({"landmarks": LM, "report": REP}, open(os.path.join(WORK, "knee_pose.json"), "w"), indent=1, default=lambda x: list(x))
# lean save: drop the Z-Anatomy scene content
keep = set(objs.values()) | set(pat_objs)
bpy.data.batch_remove([o for o in bpy.data.objects if o not in keep])
for sc in list(bpy.data.scenes):
    if sc != scene: bpy.data.scenes.remove(sc)
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "knee_posed.blend"))
print("KNEE_POSE_OK", json.dumps(REP, default=lambda x: list(x))[:3000])
