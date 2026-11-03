# World Beyond: Bäume für Unity aus Blender (headless). Äste als Röhren, Laub als Zweig-Karten mit eigenem Blatt-Atlas.
# Aufruf: blender -b -noaudio --python tree_build.py -- <oak|cherry|spruce> <texdir> <outdir> [seed]
# Ergebnis: <outdir>/WB_Tree_<Art>.fbx (Ursprung = Stammfuß, 1 Einheit = 1 m, Y oben in Unity), zwei Materialien: M_Bark<Art>, M_Leaf<Art>.
import bpy, bmesh, sys, math, random, os
from mathutils import Vector, Matrix, Quaternion, noise

a = sys.argv[sys.argv.index("--") + 1:]
KIND, TEX, OUT = a[0], a[1], a[2]; SEED = int(a[3]) if len(a) > 3 else 1
NAME = {"oak": "WB_Tree_Broadleaf", "cherry": "WB_Tree_Cherry", "spruce": "WB_Tree_Spruce"}[KIND]
BARK = {"oak": ("T_BarkOak", 1.0, 2.0), "cherry": ("T_BarkCherry", 1.6, 1.6), "spruce": ("T_BarkPine", 2.0, 2.0)}[KIND]
LEAF = {"oak": "T_LeafBroad", "cherry": "T_LeafCherry", "spruce": "T_LeafNeedle"}[KIND]
rng = random.Random(SEED * 100 + {"oak": 1, "cherry": 2, "spruce": 3}[KIND])
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

def material(name, prefix, leaf):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    def img(suf, nc):
        n = nt.nodes.new("ShaderNodeTexImage"); ext = ".png" if leaf or suf == "MS" else ".jpg"
        n.image = bpy.data.images.load(os.path.join(TEX, f"{prefix}_{suf}{ext}"))
        if nc: n.image.colorspace_settings.name = "Non-Color"
        return n
    d = img("D", False); nt.links.new(d.outputs["Color"], b.inputs["Base Color"])
    nn = img("N", True); nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = 0.6 if leaf else 0.85
    if leaf:
        nt.links.new(d.outputs["Alpha"], b.inputs["Alpha"]); m.blend_method = "CLIP"; m.shadow_method = "CLIP"
        b.inputs["Subsurface Weight"].default_value = 0.0
    return m

M_BARK = material("M_Bark" + KIND.capitalize(), BARK[0], False)
M_LEAF = material("M_Leaf" + KIND.capitalize(), LEAF, True)
bm = bmesh.new(); UV = bm.loops.layers.uv.new("UVMap"); CARD_FACES = []

def tube(pts, radii, sides):
    """Ast/Stamm als Röhre; UV zylindrisch in Weltmaß der Rindentextur."""
    tu, tv = BARK[1], BARK[2]; rings = []; dist = [0.0]
    for i in range(1, len(pts)): dist.append(dist[-1] + (pts[i] - pts[i - 1]).length)
    circ = 2 * math.pi * radii[0]; uoff = rng.random()
    prev_u = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u = (prev_u - t * prev_u.dot(t)).normalized() if prev_u is not None else t.orthogonal().normalized()
        prev_u = u; v = t.cross(u)
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            r = radii[i] * (1 + 0.08 * noise.noise(p * 2.5 + Vector((k * 0.7, 0, 0))))
            ring.append(bm.verts.new(p + (u * math.cos(ang) + v * math.sin(ang)) * r))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k])); f.material_index = 0; f.smooth = True
            for lp, uu, vv in zip(f.loops, (k, k + 1, k + 1, k), (i, i, i + 1, i + 1)):
                lp[UV].uv = (uoff + uu / sides * circ / tu, dist[vv] / tv)
    if len(rings[-1]) >= 3:
        f = bm.faces.new(rings[-1]); f.material_index = 0; f.smooth = True
        for lp in f.loops: lp[UV].uv = (0.0, dist[-1] / tv)

def grow(p0, d, length, segs, gravity=0.0, wiggle=0.06, up=0.0):
    pts = [p0.copy()]; p = p0.copy(); d = d.normalized()
    for i in range(segs):
        d = (d + Vector((rng.uniform(-wiggle, wiggle), rng.uniform(-wiggle, wiggle), rng.uniform(-wiggle, wiggle) * 0.5 - gravity + up))).normalized()
        p = p + d * (length / segs); pts.append(p.copy())
    return pts

CROSS = {"oak": 0.5, "cherry": 0.6, "spruce": 1.0}[KIND]   # Anteil gekreuzter Doppelkarten (Volumen von der Seite)
def card(p, d, size, crown_c, roll=0.6, droop=0.12, face_up=0.6, _second=False, _n=None):
    """Zweig-Karte: Ansatz unten Mitte am Ast, wächst in Richtung d. Feld im 2x2-Atlas zufällig."""
    d = d.normalized()
    out = (p - crown_c); out.z = 0
    out = out.normalized() if out.length > 1e-4 else Vector((1, 0, 0))
    want = (Vector((0, 0, 1)) * face_up + out * (1 - face_up)).normalized()
    n = (want - d * want.dot(d))
    n = n.normalized() if n.length > 1e-4 else d.orthogonal().normalized()
    if _n is not None: n = _n
    else: n = Quaternion(d, rng.uniform(-roll, roll)) @ n
    s = d.cross(n).normalized()
    cell = rng.randrange(4); u0, v0 = (cell % 2) * 0.5, (cell // 2) * 0.5
    rows = []
    for j, t in enumerate((0.0, 0.5, 1.0)):
        c = p + d * size * t - n * droop * size * t * t
        rows.append([bm.verts.new(c - s * size / 2), bm.verts.new(c + s * size / 2)])
    for j in range(2):
        f = bm.faces.new((rows[j][0], rows[j][1], rows[j + 1][1], rows[j + 1][0])); f.material_index = 1; f.smooth = True
        for lp, (uu, vv) in zip(f.loops, ((0, j), (1, j), (1, j + 1), (0, j + 1))):
            lp[UV].uv = (u0 + uu * 0.5, v0 + vv * 0.25)
        CARD_FACES.append(f)
    if not _second and rng.random() < CROSS:
        twist = 0.6 if KIND == "spruce" else math.pi / 2
        n2 = Quaternion(d, twist * rng.choice((-1, 1))) @ n
        card(p, d, size * rng.uniform(0.85, 1.0), crown_c, droop=droop, _second=True, _n=n2)

def taper(n, r0, r1, flare=0.0):
    return [r0 + (r1 - r0) * (i / (n - 1)) ** 0.9 + (flare * r0 * math.exp(-i * 1.4) if flare else 0) for i in range(n)]

def side_branches(parent, pr, count, rel_len, ang_deg, seg, sides, crown_c, card_size, cards_per, gravity=0.02, t_range=(0.3, 0.95)):
    out = []
    for k in range(count):
        t = rng.uniform(*t_range); i = max(1, min(len(parent) - 2, int(t * (len(parent) - 1))))
        p = parent[i]; dp = (parent[i + 1] - parent[i - 1]).normalized()
        axis = dp.orthogonal().normalized(); axis = Quaternion(dp, rng.uniform(0, 2 * math.pi)) @ axis
        d = Quaternion(axis, math.radians(ang_deg * rng.uniform(0.8, 1.2))) @ dp
        d.z += 0.25; d.normalize()
        L = rel_len * rng.uniform(0.75, 1.15) * (1.15 - 0.5 * t)
        r = max(0.012, pr[i] * 0.5)
        pts = grow(p - d * pr[i] * 0.5, d, L, seg, gravity, 0.1)
        tube(pts, taper(len(pts), r, 0.006), sides); out.append(pts)
        for c in range(cards_per):
            tt = 0.35 + 0.65 * (c + rng.random()) / cards_per; j = min(len(pts) - 2, int(tt * (len(pts) - 1)))
            dd = (pts[j + 1] - pts[j]).normalized() + Vector((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), rng.uniform(0.0, 0.6)))
            card(pts[j], dd, card_size * rng.uniform(0.85, 1.15), crown_c)
        card(pts[-1], (pts[-1] - pts[-2]) + Vector((0, 0, 0.3)), card_size * rng.uniform(0.9, 1.2), crown_c)
    return out

def build_broadleaf(H=8.4, base=2.6, crown_r=3.2, r0=0.28, n1=12, card_size=1.4, cards_per=4, l2=6):
    trunk = grow(Vector((0, 0, -0.25)), Vector((0, 0, 1)), H * 0.86, 16, 0.0, 0.03)
    tr = taper(len(trunk), r0, 0.05, flare=0.5); tube(trunk, tr, 9)
    crown_c = Vector((0, 0, base + (H - base) * 0.5))
    for k in range(n1):
        h = base + (H * 0.82 - base) * (k + rng.uniform(0.0, 0.6)) / n1
        i = min(len(trunk) - 2, max(1, int(round((h + 0.25) / (H * 0.86) * (len(trunk) - 1)))))
        f = (h - base) / (H - base)
        ang = k * 2.39996 + rng.uniform(-0.3, 0.3)
        elev = math.radians(28 + 40 * f + rng.uniform(-8, 8))
        d = Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev)))
        L = crown_r * (1.05 - 0.75 * f ** 1.3) * rng.uniform(0.85, 1.1)
        pts = grow(trunk[i], d, L, 7, 0.03, 0.08)
        rr = taper(len(pts), max(0.03, tr[i] * 0.55), 0.012); tube(pts, rr, 6)
        side_branches(pts, rr, l2, L * 0.5, 45, 4, 4, crown_c, card_size, cards_per)
        card(pts[-1], d + Vector((0, 0, 0.4)), card_size * 1.1, crown_c)
    for k in range(4):  # Krone oben schließen
        d = Vector((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), 1)); card(trunk[-1] - Vector((0, 0, 0.3 * k)), d, card_size * 1.1, crown_c, roll=1.5)
    return crown_c

def build_cherry(H=6.2, base=1.5, crown_r=3.0, r0=0.2):
    trunk = grow(Vector((0, 0, -0.2)), Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), 1)), base + 0.4, 5, 0.0, 0.05)
    tr = taper(len(trunk), r0, r0 * 0.75, flare=0.45); tube(trunk, tr, 9)
    crown_c = Vector((0, 0, base + (H - base) * 0.45))
    a0 = rng.uniform(0, 2 * math.pi)
    for k in range(5):  # Gerüstäste: vasenförmig, weit ausladend
        ang = a0 + k * 2 * math.pi / 5 + rng.uniform(-0.35, 0.35); elev = math.radians(rng.uniform(40, 58))
        d = Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev)))
        L = (H - base) * rng.uniform(0.7, 0.85)
        pts = grow(trunk[-1] - Vector((0, 0, 0.15)), d, L, 8, 0.05, 0.09)
        rr = taper(len(pts), tr[-1] * 0.62, 0.015); tube(pts, rr, 7)
        sub = side_branches(pts, rr, 9, L * 0.6, 50, 4, 4, crown_c, 1.25, 4, gravity=0.04, t_range=(0.15, 0.95))
        card(pts[-1], d + Vector((0, 0, 0.3)), 1.2, crown_c)
    return crown_c

def build_spruce(H=9.4, r0=0.22):
    trunk = grow(Vector((0, 0, -0.25)), Vector((0, 0, 1)), H, 18, 0.0, 0.012)
    tr = taper(len(trunk), r0, 0.02, flare=0.35); tube(trunk, tr, 8)
    crown_c = Vector((0, 0, H * 0.4))
    h = 1.3; k = 0
    while h < H - 0.35:
        f = h / H; n = 6 if f < 0.75 else 5
        i = min(len(trunk) - 2, int(round((h + 0.25) / H * (len(trunk) - 1))))
        L = (1 - f) ** 0.95 * 2.9 + 0.4
        for b in range(n):
            ang = k * 0.9 + b * 2 * math.pi / n + rng.uniform(-0.3, 0.3)
            elev = math.radians(-12 + 30 * f + rng.uniform(-6, 6))
            d = Vector((math.cos(ang) * math.cos(elev), math.sin(ang) * math.cos(elev), math.sin(elev)))
            pts = grow(trunk[i], d, L, 4, 0.06 * (1 - f), 0.04, up=0.04)
            tube(pts, taper(len(pts), max(0.018, tr[i] * 0.3), 0.008), 4)
            nc = max(1, int(L / 0.5) + 1)
            for c in range(nc):
                t = c / nc; j = min(len(pts) - 2, int(t * (len(pts) - 1)))
                card(pts[j], (pts[j + 1] - pts[j]) + Vector((0, 0, 0.05)), min(1.5, L * 0.8 + 0.5) * rng.uniform(0.9, 1.1), crown_c, roll=0.3, droop=0.2, face_up=0.9)
        h += rng.uniform(0.3, 0.38) * (1.0 if f < 0.7 else 0.85); k += 1
    for c in range(3):  # Wipfel
        card(trunk[-1] - Vector((0, 0, 0.9 - 0.25 * c)), Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), 1)), 0.9, crown_c, roll=3.0, droop=0.0, face_up=0.0)
    return crown_c

crown_c = {"oak": build_broadleaf, "cherry": build_cherry, "spruce": build_spruce}[KIND]()

# Mesh fertigstellen
for f in bm.faces: f.normal_update()
me = bpy.data.meshes.new(NAME)
bm.to_mesh(me); bm.free()
me.materials.append(M_BARK); me.materials.append(M_LEAF)
obj = bpy.data.objects.new(NAME, me); sc.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj; obj.select_set(True)
# Laub: Normalen kugelförmig von der Kronenmitte nach außen (weiches Licht wie bei echten Kronen)
try:
    me.use_auto_smooth = True
except AttributeError:
    pass
me.calc_normals_split() if hasattr(me, "calc_normals_split") else None
loop_n = []
for poly in me.polygons:
    for li in poly.loop_indices:
        if poly.material_index == 1:
            v = me.vertices[me.loops[li].vertex_index].co
            o = (v - crown_c); o.z *= 0.8
            nrm = (o.normalized() * 0.75 + Vector((0, 0, 0.45))).normalized()
            loop_n.append(nrm)
        else:
            loop_n.append(Vector(me.loops[li].normal))
me.normals_split_custom_set(loop_n)

me.calc_loop_triangles(); tris = len(me.loop_triangles)
bb = [Vector(c) for c in obj.bound_box]
mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb))); mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
ncards = sum(1 for p in me.polygons if p.material_index == 1) // 2
print(f"STATS {NAME} tris={tris} cards={ncards} size_m=({mx.x - mn.x:.2f}, {mx.y - mn.y:.2f}, {mx.z:.2f}) zmin={mn.z:.2f}")
os.makedirs(OUT, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT"); obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + ".fbx"), use_selection=True, object_types={"MESH"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="OFF", use_mesh_modifiers=True, path_mode="STRIP", add_leaf_bones=False)
