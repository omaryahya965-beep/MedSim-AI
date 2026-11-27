# blender -b file.blend [--python colors.py] --python preview.py -- out_prefix "name:cx,cy,cz:tx,ty,tz:lens" ...
# Renders from a fresh "Preview" scene (independent of whatever display settings the source file carries).
import bpy, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]; out = argv[0]
src = bpy.context.scene
sc = bpy.data.scenes.new("Preview")
for o in src.objects:
    if o.type in ("MESH", "EMPTY") and not o.hide_render: sc.collection.objects.link(o)
sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x = 960; sc.render.resolution_y = 600; sc.render.resolution_percentage = 100
sc.render.use_freestyle = False
sh = sc.display.shading; sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_cavity = True; sh.cavity_type = "WORLD"; sh.show_shadows = False
import os
sh.show_xray = bool(os.environ.get("XRAY")); sh.xray_alpha = 0.45; sh.show_object_outline = False; sh.background_type = "VIEWPORT"
sc.display.render_aa = "8"
sc.view_settings.view_transform = "Standard"
w = bpy.data.worlds.new("pw"); w.color = (0.32, 0.34, 0.36); sc.world = w
cam_data = bpy.data.cameras.new("pc"); cam_data.clip_start = 0.01; cam = bpy.data.objects.new("pc", cam_data); sc.collection.objects.link(cam); sc.camera = cam
for spec in argv[1:]:
    n, c, t, l = spec.split(":"); c = Vector(map(float, c.split(","))); t = Vector(map(float, t.split(",")))
    cam.location = c; cam.rotation_euler = (t - c).to_track_quat("-Z", "Y").to_euler(); cam_data.lens = float(l)
    sc.render.filepath = f"{out}_{n}.png"; bpy.ops.render.render(write_still=True, scene=sc.name)
