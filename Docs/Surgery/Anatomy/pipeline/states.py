import bpy, os
st = os.environ.get("STATE", "anat")
OPT = {"Gastrocnemius","Popliteus","PesAnserinus","Hamstrings","IliotibialTract"}
PAT = {"Body","RightLeg_Prepped","Brief","Gown","Hair","Eyes","Eyebrows","Eyelashes","Teeth","Tongue","DistalFemur_Closed","ProximalTibia_Closed"}
IMPL = {"FemoralComponent","FemoralTrial","TibialTray","TibialTrial","PolyInsert"}
OVER = {"SiteMarking","IncisionLine","ClosureLine","SkinStaples","GuidePins_Femur","GuidePins_Tibia"}
SOFT_ANT = {"JointCapsule","InfrapatellarFatPad","PatellarRetinacula","SuprapatellarBursa"}
RES = {"DistalFemur_Resected","ProximalTibia_Resected"}
SKIN = {"KneeSkin","KneeSkin_Open"}
vis = {
 "anat": lambda n: n not in OPT|PAT|IMPL|OVER|RES|SKIN,
 "marked": lambda n: n in {"KneeSkin","SiteMarking"},
 "incised": lambda n: n in {"KneeSkin","IncisionLine"},
 "exposed": lambda n: n not in OPT|PAT|IMPL|OVER|RES|SOFT_ANT|{"KneeSkin"},
 "guides": lambda n: n not in OPT|PAT|IMPL|RES|SOFT_ANT|{"KneeSkin","SiteMarking","IncisionLine","ClosureLine","SkinStaples"},
 "resected": lambda n: n not in OPT|PAT|IMPL|OVER|SOFT_ANT|{"KneeSkin","DistalFemur","ProximalTibia","FemoralCartilage","TibialCartilage","MedialMeniscus","LateralMeniscus","ACL","TransverseLigament","MeniscotibialLigaments"},
 "implanted": lambda n: n not in OPT|PAT|OVER|SOFT_ANT|{"KneeSkin","DistalFemur","ProximalTibia","FemoralCartilage","TibialCartilage","MedialMeniscus","LateralMeniscus","ACL","TransverseLigament","MeniscotibialLigaments","FemoralTrial","TibialTrial"},
 "closed": lambda n: n in {"KneeSkin","ClosureLine","SkinStaples"},
 "implants_only": lambda n: n in {"FemoralComponent","TibialTray","PolyInsert"},
}[st]
for o in bpy.data.objects:
    if o.type == "MESH": o.hide_render = not vis(o.name)
# preview the exposed-state rules used in Unity: hide quadriceps + anterior soft tissue, evert the patella group
import json, math
from mathutils import Matrix, Vector
if st in ("exposed", "guides", "resected", "implanted"):
    A = json.load(open(r"D:/Medical-Test/My project/Assets/MedSimAI/Surgery/Anatomy/Models/Knee/KneeAnatomy_R.landmarks.json"))["anchors"]["PatellaEversion"]
    b = lambda u: Vector((-u[0], -u[2], u[1]))
    piv, ax, off = b(A["pivot"]), b(A["axis"]).normalized(), b(A["offset"])
    # Unity angle about a Unity axis -> mirrored handedness flips the sign in Blender
    M = Matrix.Translation(off) @ Matrix.Translation(piv) @ Matrix.Rotation(math.radians(-A["angleDeg"]), 4, ax) @ Matrix.Translation(-piv)
    for n in ("Patella", "PatellarCartilage", "PatellarTendon"):
        o = bpy.data.objects.get(n)
        if o: o.matrix_world = M @ o.matrix_world
    for n in ("Quadriceps",):
        if n in bpy.data.objects: bpy.data.objects[n].hide_render = True
