import bpy, os
C = {"MA_Bone": (0.86,0.82,0.70), "MA_Cartilage": (0.62,0.78,0.92), "MA_Ligament": (0.95,0.93,0.80), "MA_Meniscus": (0.80,0.86,0.78), "MA_Capsule": (0.85,0.80,0.85),
     "MA_Fat": (0.95,0.85,0.40), "MA_Muscle": (0.65,0.22,0.22), "MA_Tendon": (0.92,0.90,0.85), "MA_CutBone": (0.85,0.65,0.45), "MA_Implant": (0.72,0.74,0.78), "MA_Poly": (0.95,0.95,0.92)}
for m in bpy.data.materials:
    c = C.get(m.name)
    if c: m.diffuse_color = (*c, 1)
    elif m.name.startswith("MA_") is False and any(k in m.name.lower() for k in ("skin", "human", "body")): m.diffuse_color = (0.86, 0.66, 0.56, 1)
HIDE = os.environ.get("HIDE", "")
for o in bpy.data.objects:
    if HIDE and any(h == o.name for h in HIDE.split(",")): o.hide_render = True
    if o.type == "MESH" and o.name in ("KneeSkin", "RightLeg_Prepped", "Body"):
        for s in o.material_slots:
            if s.material: s.material.diffuse_color = (0.86, 0.66, 0.56, 1)
