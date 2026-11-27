# MedSim AI — Surgery (Person 2): independent surgical patient for TKA, built with MPFB2 / MakeHuman CC0 assets.
# Usage: blender -b --python build_patient.py -- <out_fbx_dir> <work_dir>
# Output frame (Blender): supine on a table plane z=0, head toward -Y, face +Z, patient's RIGHT = +X,
# origin under the mid-point of the hip joints.  (Unity after FBX export: +Z head, +Y up, right = -X.)
import importlib, sys, os, json, math, bpy, bmesh
from mathutils import Matrix, Vector

def dyn(pkg, key):
    for m in list(sys.modules):
        if m.endswith(pkg):
            mod = importlib.import_module(m)
            if hasattr(mod, key): return getattr(mod, key)
    raise ValueError(pkg)
HumanService = dyn("mpfb.services.humanservice", "HumanService")
AssetService = dyn("mpfb.services.assetservice", "AssetService")
ExportService = dyn("mpfb.services.exportservice", "ExportService")
TargetService = dyn("mpfb.services.targetservice", "TargetService")

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, WORK = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True); os.makedirs(WORK, exist_ok=True)
KNEE_FLEX = 90.0          # degrees (TKA exposure position; configurable)
report = {"kneeFlexDeg": KNEE_FLEX, "missing": []}

bpy.ops.wm.read_factory_settings(use_empty=True)
macro = {"gender": 1.0, "age": 0.9, "muscle": 0.42, "weight": 0.72, "proportions": 0.55, "height": 0.52, "cupsize": 0.5, "firmness": 0.35,
         "race": {"caucasian": 0.45, "asian": 0.15, "african": 0.40}}
bm = HumanService.create_human(macro_detail_dict=macro)
skin = AssetService.find_asset_absolute_path("old_caucasian_male.mhmat", asset_subdir="skins")
HumanService.set_character_skin(skin, bm, skin_type="GAMEENGINE", material_instances=False)
HumanService.add_builtin_rig(bm, "game_engine")
for sub, fname, atype in [("eyes", "low-poly.mhclo", "Eyes"), ("eyebrows", "eyebrow001.mhclo", "Eyebrows"), ("eyelashes", "eyelashes01.mhclo", "Eyelashes"),
                          ("teeth", "teeth_base.mhclo", "Teeth"), ("tongue", "tongue01.mhclo", "Tongue"), ("hair", "short04.mhclo", "Hair")]:
    p = AssetService.find_asset_absolute_path(fname, asset_subdir=sub)
    if p is None: report["missing"].append(fname); continue
    HumanService.add_mhclo_asset(p, bm, asset_type=atype, material_type="GAMEENGINE")
TargetService.bake_targets(bm)
arm = bm.parent
ExportService.bake_modifiers_remove_helpers(bm, bake_masks=True, bake_subdiv=False, remove_helpers=True, also_proxy=True)
meshes = [o for o in arm.children_recursive if o.type == "MESH"]
body = bm
report["meshes"] = [o.name for o in meshes]

# ---- garments for dignity (derived from body faces, follow the skinning) -------------------------------------------
def clone_region(name, pred, push, thick, mat_name, color):
    me = body.data
    bpy.ops.object.select_all(action="DESELECT")
    dup = body.copy(); dup.data = me.copy(); dup.name = name; bpy.context.collection.objects.link(dup)
    bmx = bmesh.new(); bmx.from_mesh(dup.data)
    kill = [f for f in bmx.faces if not pred(dup.matrix_world @ f.calc_center_median())]
    bmesh.ops.delete(bmx, geom=kill, context="FACES")
    bmx.normal_update()
    for v in bmx.verts: v.co += v.normal * push
    bmx.to_mesh(dup.data); bmx.free()
    dup.data.materials.clear()
    m = bpy.data.materials.new(mat_name); m.diffuse_color = color; dup.data.materials.append(m)
    s = dup.modifiers.new("thick", "SOLIDIFY"); s.thickness = thick; s.offset = 1   # evaluated after the armature (posed shell)
    return dup

hipz = (arm.matrix_world @ arm.data.bones["thigh_r"].head_local).z
def brief_pred(c):  # pelvis band, ends just below the groin
    return hipz - 0.13 < c.z < hipz + 0.07 and abs(c.x) < 0.24
def gown_pred(c):   # torso + short sleeves (gown pulled up for the procedure)
    if hipz + 0.05 < c.z < 1.50 and abs(c.x) < 0.22: return True
    return 1.30 < c.z < 1.50 and 0.18 <= abs(c.x) < 0.30
brief = clone_region("Brief", brief_pred, 0.004, 0.0015, "M_Brief", (0.55, 0.70, 0.82, 1))
gown = clone_region("Gown", gown_pred, 0.008, 0.002, "M_Gown", (0.62, 0.74, 0.80, 1))
meshes += [brief, gown]

# ---- pose -------------------------------------------------------------------------------------------------------
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode="POSE")
P = arm.pose.bones
def rot_world(pb, axis, deg):
    bpy.context.view_layer.update()
    head = pb.head.copy()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()
def reset(names):
    for n in names: P[n].matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()

def eval_points(obj):
    dg = bpy.context.evaluated_depsgraph_get(); ev = obj.evaluated_get(dg); m = ev.to_mesh()
    pts = [obj.matrix_world @ v.co for v in m.vertices]; ev.to_mesh_clear(); return pts

# arms down to the sides (sign chosen automatically), elbows nearly straight
for side, sgn in (("r", -1), ("l", 1)):
    best = None
    for ang in (-40, 40):
        reset([f"upperarm_{side}", f"lowerarm_{side}"])
        rot_world(P[f"upperarm_{side}"], "Y", ang)
        hx = abs((arm.matrix_world @ P[f"hand_{side}"].head).x)
        if best is None or hx < best[1]: best = (ang, hx)
    reset([f"upperarm_{side}", f"lowerarm_{side}"]); rot_world(P[f"upperarm_{side}"], "Y", best[0])
    # upper arm slightly forward, forearm folded across the abdomen (keeps the hands clear of the flexed right thigh)
    rot_world(P[f"upperarm_{side}"], "X", -4)
    la = P[f"lowerarm_{side}"]
    d_la = (la.tail - la.head).normalized()
    want = Vector((-sgn * 0.80, -0.36, 0.30 if side == "r" else 0.50)).normalized()   # medial, forward (= up when supine), cranial; left forearm above the right
    q = d_la.rotation_difference(want); axis, angle = q.to_axis_angle()
    rot_world(la, axis, math.degrees(angle))

# torso back plane (standing frame): the patient lies on +Y side
torso = [p for p in eval_points(body) if hipz - 0.05 < p.z < 1.45 and abs(p.x) < 0.18]
y_table = max(p.y for p in torso)

def pose_leg(A):
    reset(["thigh_r", "calf_r", "foot_r"])
    rot_world(P["thigh_r"], "X", -A)            # hip flexion: knee forward (-Y)
    rot_world(P["calf_r"], "X", KNEE_FLEX)      # knee flexion: ankle backward
    rot_world(P["foot_r"], "X", 90 - KNEE_FLEX + A)  # sole flat on the table plane
right_foot_idx = None
def foot_contact():
    pts = eval_points(body)
    global right_foot_idx
    ank = arm.matrix_world @ P["foot_r"].head
    if right_foot_idx is None:
        right_foot_idx = [i for i, p in enumerate(pts) if (p - ank).length < 0.16 and p.x < -0.08]
    return max(pts[i].y for i in right_foot_idx)
best = None
for A in [30 + 1.5 * k for k in range(21)]:
    pose_leg(A); right_foot_idx = None
    err = abs(foot_contact() - (y_table - 0.005))
    if best is None or err < best[1]: best = (A, err)
pose_leg(best[0]); report["hipFlexDeg"] = best[0]; report["footContactErr_m"] = best[1]
bpy.ops.object.mode_set(mode="OBJECT")

def joint(name): return arm.matrix_world @ P[name].head
J = {"Hip_R": joint("thigh_r"), "Knee_R": joint("calf_r"), "Ankle_R": joint("foot_r"), "Hip_L": joint("thigh_l"), "Knee_L": joint("calf_l"),
     "Ankle_L": joint("foot_l"), "Pelvis": joint("pelvis"), "Head": joint("head"), "Neck": joint("neck_01"), "Shoulder_R": joint("upperarm_r"), "Shoulder_L": joint("upperarm_l")}
J["HeadTop"] = arm.matrix_world @ P["head"].tail

# ---- bake the pose into static meshes ----------------------------------------------------------------------------
for o in meshes:
    bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = o; o.select_set(True)
    if o.data.shape_keys: o.shape_key_clear()
    for md in list(o.modifiers):
        try: bpy.ops.object.modifier_apply(modifier=md.name)
        except Exception as e: print("modifier apply failed", o.name, md.name, e); o.modifiers.remove(md)
    o.parent = None
    o.matrix_world = o.matrix_world  # keep
bpy.data.objects.remove(arm)

# ---- supine transform: standing (face -Y, up +Z)  ->  supine (head -Y, face +Z, right +X) --------------------------
R = Matrix.Rotation(math.pi, 4, "Y") @ Matrix.Rotation(math.pi / 2, 4, "X")
for o in meshes:
    o.data.transform(R @ o.matrix_world); o.matrix_world = Matrix.Identity(4)
for k in J: J[k] = R @ J[k]
hipmid = (J["Hip_R"] + J["Hip_L"]) / 2
backz = min((v.co.z for v in body.data.vertices if abs(v.co.y - hipmid.y) < 0.35 and abs(v.co.x) < 0.17), default=0)
T = Matrix.Translation(Vector((-hipmid.x, -hipmid.y, -backz - 0.012)))   # 12 mm into the mattress
for o in meshes: o.data.transform(T)
for k in J: J[k] = T @ J[k]

# ---- split skin: knee patch (state variants) and prepped right leg -------------------------------------------------
hip, knee, ank = J["Hip_R"], J["Knee_R"], J["Ankle_R"]
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (p - (a + ab * t)).length, t
def split(src, name, pred):
    bpy.ops.object.select_all(action="DESELECT")
    bmx = bmesh.new(); bmx.from_mesh(src.data)
    take = [f for f in bmx.faces if pred(f.calc_center_median())]
    me2 = src.data.copy(); new = bpy.data.objects.new(name, me2); bpy.context.collection.objects.link(new)
    b2 = bmesh.new(); b2.from_mesh(me2)
    keep_ids = set(f.index for f in take)
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if f.index not in keep_ids], context="FACES"); b2.to_mesh(me2); b2.free()
    bmesh.ops.delete(bmx, geom=take, context="FACES"); bmx.to_mesh(src.data); bmx.free()
    return new
def in_knee_patch(c): return (c - knee).length < 0.135 and c.x > 0.04
def in_right_leg(c):
    d1, t1 = seg_dist(c, hip, knee); d2, t2 = seg_dist(c, knee, ank)
    if c.x < 0.04: return False
    return (d1 < 0.13 and t1 > 0.42) or (d2 < 0.11 and t2 < 0.93)
knee_skin = split(body, "KneeSkin", in_knee_patch)
leg_skin = split(body, "RightLeg_Prepped", in_right_leg)
# subdivide the knee patch (simple: borders stay crack-free) so incisions/openings can be cut at ~5 mm resolution
bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = knee_skin; knee_skin.select_set(True)
sd = knee_skin.modifiers.new("sd", "SUBSURF"); sd.subdivision_type = "SIMPLE"; sd.levels = 2; sd.render_levels = 2
bpy.ops.object.modifier_apply(modifier="sd")
body.name = "Body"
for o in meshes:
    if o.name.startswith("Human") and o is not body: pass
names = {}
for o in list(bpy.data.objects):
    if o.type != "MESH": continue
    n = o.name.lower()
    for key, new in (("eyebrow", "Eyebrows"), ("eyelash", "Eyelashes"), ("teeth", "Teeth"), ("tongue", "Tongue"), ("short04", "Hair"), ("low-poly", "Eyes"), ("eyes", "Eyes")):
        if key in n and o not in (body, knee_skin, leg_skin, brief, gown): o.name = new; break
for o in bpy.data.objects:
    if o.type == "MESH":
        for p in o.data.polygons: p.use_smooth = True

# ---- anchors (empties) ----------------------------------------------------------------------------------------------
anchors = bpy.data.objects.new("Anchors", None); bpy.context.collection.objects.link(anchors)
for k, v in J.items():
    e = bpy.data.objects.new("Joint_" + k, None); e.location = v; bpy.context.collection.objects.link(e); e.parent = anchors
report["joints"] = {k: [round(c, 4) for c in v] for k, v in J.items()}
allz = [(o.matrix_world @ v.co) for o in bpy.data.objects if o.type == "MESH" for v in o.data.vertices]
report["bounds"] = {"min": [round(min(p[i] for p in allz), 3) for i in range(3)], "max": [round(max(p[i] for p in allz), 3) for i in range(3)]}

# textures used by the materials (copied for the Unity material builder)
tex = {}
for o in bpy.data.objects:
    if o.type != "MESH": continue
    for m in o.data.materials:
        if not m or not m.use_nodes: continue
        for nd in m.node_tree.nodes:
            if nd.type == "TEX_IMAGE" and nd.image:
                tex.setdefault(o.name, []).append({"material": m.name, "node": nd.label or nd.name, "file": bpy.path.abspath(nd.image.filepath)})
report["textures"] = tex
report["objects"] = {o.name: len(o.data.vertices) for o in bpy.data.objects if o.type == "MESH"}

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "patient_posed.blend"))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "SurgicalPatient_Body.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                         path_mode="STRIP", object_types={"MESH", "EMPTY"}, use_tspace=True)
with open(os.path.join(WORK, "patient_report.json"), "w") as f: json.dump(report, f, indent=1)
print("PATIENT_OK", json.dumps({k: report[k] for k in ("hipFlexDeg", "footContactErr_m", "joints", "bounds", "objects", "missing")}))
