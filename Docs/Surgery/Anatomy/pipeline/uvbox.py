# Box-projected UVs (dominant-axis planar per face) for meshes without UVs; 1 UV unit = `size` metres.
import bmesh
def box_uv(obj, size=0.04, force=False):
    me = obj.data
    if len(me.uv_layers) and not force: return False
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            if ax == 0: u, v = c.y, c.z
            elif ax == 1: u, v = c.x, c.z
            else: u, v = c.x, c.y
            l[uv].uv = (u / size, v / size)
    bm.to_mesh(me); bm.free(); return True
