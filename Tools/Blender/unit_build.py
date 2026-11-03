# World Beyond: Ochse und Ochsenkarren (Frühzeit) aus getrennten Teilen, damit der Code sie animieren kann. Blender 4.0 headless.
# Aufruf: blender -b -noaudio --python unit_build.py -- <texdir> <outdir> [preview.png] [samples]
# Ergebnis: WB_Unit_Ox.fbx (Ox_Body, Ox_Head, Ox_Leg_FL/FR/BL/BR, Ox_Tail) und WB_Unit_Cart.fbx (Cart_Body, Cart_Wheel_L/R).
# Jedes Teil hat seinen Drehpunkt als Ursprung (Beine: Gelenk oben, Kopf: Nacken, Schwanz: Ansatz, Räder: Achsmitte).
# Blickrichtung: -Y in Blender = +Z (vorwärts) in Unity. 1 Einheit = 1 m. Ochse: Ursprung zwischen den Hufen auf dem Boden.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix, noise
a = sys.argv[sys.argv.index("--") + 1:]
TEX, OUT = a[0], a[1]; PREVIEW = a[2] if len(a) > 2 else None; SAMPLES = int(a[3]) if len(a) > 3 else 32
rng = random.Random(9)
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
TILE = {"M_Fur": 0.8, "M_Hide": 0.9, "M_Wood": 0.5, "M_Bark": 1.0, "M_Horn": 0.3}
MATS = {}
def material(name):
    if name in MATS: return MATS[name]
    pre = {"M_Horn": "T_Wood"}.get(name, "T_" + name[2:])
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(os.path.join(TEX, pre + "_D.jpg")); nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(os.path.join(TEX, pre + "_N.jpg")); n.image.colorspace_settings.name = "Non-Color"
    nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(n.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = 0.85; MATS[name] = m; return m

def box_uv(bm, tile):
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        ax = max(range(3), key=lambda i: abs(f.normal[i]))
        for lp in f.loops:
            c = lp.vert.co; u, v = {0: (c.y, c.z), 1: (c.x, c.z), 2: (c.x, c.y)}[ax]; lp[uv].uv = (u / tile, v / tile)

def skin_part(name, verts, edges, radii, mat, pivot, sub=2, extra=None):
    """Organische Form über den Skin-Modifier (Graph aus Punkten mit Radien), danach geglättet."""
    me = bpy.data.meshes.new(name); me.from_pydata([Vector(v) - Vector(pivot) for v in verts], edges, [])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); o.location = pivot
    m = o.modifiers.new("skin", "SKIN"); m.use_smooth_shade = True
    for i, r in enumerate(radii):
        o.data.skin_vertices[0].data[i].radius = (r[0], r[1]) if isinstance(r, tuple) else (r, r)
    o.data.skin_vertices[0].data[0].use_root = True
    s = o.modifiers.new("sub", "SUBSURF"); s.levels = sub; s.render_levels = sub
    bpy.context.view_layer.objects.active = o
    for mod in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new(); bm.from_mesh(o.data)
    for v in bm.verts:  # leichte Unregelmäßigkeit (Muskeln, Fell)
        w = o.location + v.co; v.co += v.normal * 0.012 * noise.noise(w * 4.0)
    bm.normal_update(); box_uv(bm, TILE[mat]); bm.to_mesh(o.data); bm.free()
    o.data.materials.append(material(mat))
    for p in o.data.polygons: p.use_smooth = True
    return o

def tube(name_or_bm, pts, radii, sides, mat):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap"); rings = []; dist = [0.0]
    pts = [Vector(p) for p in pts]
    for i in range(1, len(pts)): dist.append(dist[-1] + (pts[i] - pts[i - 1]).length)
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized(); u = t.orthogonal().normalized(); v = t.cross(u)
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * radii[i]) for k in range(sides)])
    T = TILE[mat]
    for i in range(len(rings) - 1):
        for k in range(sides):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k])); f.smooth = True
            for lp, (kk, ii) in zip(f.loops, ((k, i), (k + 1, i), (k + 1, i + 1), (k, i + 1))): lp[uv].uv = (kk / sides * 2 * math.pi * radii[0] / T, dist[ii] / T)
    for ring in (rings[0], rings[-1]):
        f = bm.faces.new(ring if ring is rings[-1] else ring[::-1])
        for lp in f.loops: lp[uv].uv = (lp.vert.co.x / T, lp.vert.co.y / T)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return bm

def obj_from(name, bms_mats, pivot):
    """Mehrere bmesh-Teile (je Material) zu einem Objekt mit Ursprung am Drehpunkt."""
    objs = []
    for bm, mat in bms_mats:
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
        o.data.materials.append(material(mat)); objs.append(o)
    join(objs, name)
    o = bpy.data.objects[name]
    o.data.transform(Matrix.Translation(-Vector(pivot))); o.location = pivot; return o

def join(objs, name):
    allm = []
    for o in objs:
        for m in o.data.materials:
            if m.name not in [x.name for x in allm]: allm.append(m)
    for o in objs:
        old = [p.material_index for p in o.data.polygons]; own = [m.name for m in o.data.materials]
        o.data.materials.clear()
        for m in allm: o.data.materials.append(m)
        for p, oi in zip(o.data.polygons, old): p.material_index = [x.name for x in allm].index(own[oi])
        for lay in list(o.data.uv_layers):
            if lay.name != "UVMap": o.data.uv_layers.remove(lay)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name; return o

# ================= Ochse =================
OX = []
body_v = [(0, 0.98, 1.28), (0, 0.72, 1.30), (0, 0.25, 1.22), (0, -0.25, 1.20), (0, -0.62, 1.30), (0, -0.82, 1.36)]
body_r = [(0.16, 0.14), (0.40, 0.38), (0.46, 0.46), (0.46, 0.46), (0.40, 0.44), (0.30, 0.32)]
OX.append(skin_part("Ox_Body", body_v, [(i, i + 1) for i in range(len(body_v) - 1)], body_r, "M_Fur", (0, 0, 0)))
# Wamme (Hautfalte unter dem Hals) gehört zum Körper
dew = skin_part("Ox_Dewlap", [(0, -0.72, 1.05), (0, -0.95, 1.0)], [(0, 1)], [(0.08, 0.2), (0.06, 0.12)], "M_Fur", (0, 0, 0), sub=1)
OX[0] = join([OX[0], dew], "Ox_Body")
# Kopf mit Hals: Drehpunkt im Nacken
head_v = [(0, -0.80, 1.36), (0, -1.04, 1.40), (0, -1.16, 1.30), (0, -1.27, 1.08), (0, -1.31, 0.99)]
head_r = [(0.27, 0.30), (0.22, 0.2), (0.18, 0.17), (0.15, 0.13), (0.14, 0.11)]
head = skin_part("Ox_Head", head_v, [(i, i + 1) for i in range(4)], head_r, "M_Fur", (0, -0.85, 1.36))
parts = [head]
for sg in (-1, 1):  # Hörner, nach außen und oben geschwungen
    bm = tube(None, [(sg * 0.1, -1.1, 1.5), (sg * 0.28, -1.08, 1.53), (sg * 0.4, -1.12, 1.64), (sg * 0.42, -1.2, 1.76)], [0.05, 0.04, 0.028, 0.008], 8, "M_Horn")
    h = obj_from("horn", [(bm, "M_Horn")], (0, 0, 0)); h.location = (0, 0, 0); h.data.transform(Matrix.Translation(Vector((0, 0, 0)))); parts.append(h)
    e = skin_part("ear", [(sg * 0.13, -1.06, 1.42), (sg * 0.29, -1.03, 1.36)], [(0, 1)], [(0.05, 0.02), (0.03, 0.012)], "M_Fur", (0, 0, 0), sub=1); parts.append(e)
for o in parts[1:]:  # in Kopf-Koordinaten bringen
    o.data.transform(Matrix.Translation(o.location - head.location)); o.location = head.location
head = join(parts, "Ox_Head")
OX.append(head)
# Beine: Drehpunkt oben (Schulter/Hüfte)
LEGS = {"FL": (-0.22, -0.55, False), "FR": (0.22, -0.55, False), "BL": (-0.22, 0.62, True), "BR": (0.22, 0.62, True)}
for k, (x, y, back) in LEGS.items():
    if back:
        v = [(x, y, 1.12), (x, y + 0.1, 0.78), (x, y + 0.14, 0.52), (x, y + 0.06, 0.12), (x, y + 0.04, 0.0)]
        r = [0.16, 0.12, 0.06, 0.055, 0.065]
    else:
        v = [(x, y, 1.08), (x, y - 0.02, 0.72), (x, y + 0.02, 0.48), (x, y, 0.12), (x, y - 0.02, 0.0)]
        r = [0.14, 0.095, 0.062, 0.055, 0.065]
    leg = skin_part("Ox_Leg_" + k, v, [(i, i + 1) for i in range(4)], r, "M_Fur", v[0])
    bm = tube(None, [(x, v[-1][1], -0.01), (x, v[-1][1], 0.1)], [0.085, 0.07], 10, "M_Bark")  # Huf
    hoof = obj_from("hoof", [(bm, "M_Bark")], (0, 0, 0))
    hoof.data.transform(Matrix.Translation(-Vector(v[0]))); hoof.location = v[0]
    OX.append(join([leg, hoof], "Ox_Leg_" + k))
tail = skin_part("Ox_Tail", [(0, 1.0, 1.32), (0, 1.1, 1.0), (0, 1.12, 0.62), (0, 1.12, 0.48)], [(0, 1), (1, 2), (2, 3)], [0.05, 0.03, 0.025, 0.06], "M_Fur", (0, 1.0, 1.32), sub=1)
OX.append(tail)

# ================= Karren =================
CART = []
bms = []
axle_z = 0.46
# Plattform aus Brettern auf zwei Längsbalken
for sg in (-1, 1): bms.append((tube(None, [(sg * 0.42, 1.0, 0.72), (sg * 0.42, -0.95, 0.72)], [0.06, 0.06], 8, "M_Bark"), "M_Bark"))
for k in range(9):
    y = -0.88 + k * 0.22
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=Vector((1.1, 0.2, 0.04)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector((rng.uniform(-0.02, 0.02), y, 0.8)), verts=bm.verts); bm.normal_update(); box_uv(bm, TILE["M_Wood"]); bms.append((bm, "M_Wood"))
# Seitenwände aus Flechtwerk zwischen Rungen
for sg in (-1, 1):
    for k in range(6):
        y = -0.85 + k * 0.36; bms.append((tube(None, [(sg * 0.52, y, 0.76), (sg * 0.54, y, 1.25)], [0.03, 0.025], 6, "M_Bark"), "M_Bark"))
    for j in range(5):
        z = 0.86 + j * 0.085; pts = []
        for k in range(13):
            y = -0.85 + k * 0.15; pts.append((sg * (0.53 + 0.025 * (1 if (k + j) % 2 else -1)), y, z))
        bms.append((tube(None, pts, [0.014] * len(pts), 5, "M_Bark"), "M_Bark"))
# Deichseln (zwei Holme) und Joch über dem Ochsennacken
YOKE_Y = -3.25
for sg in (-1, 1):
    bms.append((tube(None, [(sg * 0.42, 0.9, 0.7), (sg * 0.45, -0.9, 0.76), (sg * 0.58, -2.4, 1.2), (sg * 0.5, YOKE_Y - 0.1, 1.42)], [0.055, 0.055, 0.05, 0.045], 8, "M_Bark"), "M_Bark"))
bms.append((tube(None, [(-0.62, YOKE_Y, 1.45), (-0.3, YOKE_Y, 1.68), (0, YOKE_Y, 1.74), (0.3, YOKE_Y, 1.68), (0.62, YOKE_Y, 1.45)], [0.06, 0.07, 0.07, 0.07, 0.06], 8, "M_Wood"), "M_Wood"))
# Achse
bms.append((tube(None, [(-0.78, 0, axle_z), (0.78, 0, axle_z)], [0.05, 0.05], 8, "M_Wood"), "M_Wood"))
for sg in (-1, 1): bms.append((tube(None, [(sg * 0.42, -0.08, axle_z), (sg * 0.42, -0.08, 0.72)], [0.045, 0.045], 6, "M_Wood"), "M_Wood"))
# Ladung: Fellbündel und zwei Säcke
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.3)
for v in bm.verts: v.co.z *= 0.55; v.co.y *= 1.4; v.co += Vector((0.1, 0.35, 0.98))
bm.normal_update(); box_uv(bm, TILE["M_Hide"]); bms.append((bm, "M_Hide"))
CART.append(obj_from("Cart_Body", bms, (0, 0, 0)))
for sg, k in ((-1, "L"), (1, "R")):  # Scheibenräder aus drei Bohlen, Ursprung = Achsmitte
    bms = []; R = axle_z - 0.0; x = sg * 0.68
    for j, (w0, w1) in enumerate(((-R, -R / 3), (-R / 3, R / 3), (R / 3, R))):
        bm = bmesh.new(); n = 20
        vs = []
        for side in (-1, 1):
            ring = []
            for i in range(n + 1):
                ang = math.pi * i / n - math.pi / 2
                yy = math.cos(ang) * R; zz = math.sin(ang) * R
                ring.append((yy, zz))
            vs.append(ring)
        # Bohle j: Teil der Scheibe zwischen y = w0 und w1
        pts = []
        steps = 10
        for i in range(steps + 1):
            yy = w0 + (w1 - w0) * i / steps; zz = math.sqrt(max(R * R - yy * yy, 0.0)); pts.append((yy, zz))
        top = [bm.verts.new((x - 0.05, yy, axle_z + zz * 0.995)) for yy, zz in pts]; bot = [bm.verts.new((x - 0.05, yy, axle_z - zz * 0.995)) for yy, zz in pts]
        top2 = [bm.verts.new((x + 0.05, yy, axle_z + zz * 0.995)) for yy, zz in pts]; bot2 = [bm.verts.new((x + 0.05, yy, axle_z - zz * 0.995)) for yy, zz in pts]
        for i in range(steps):
            bm.faces.new((top[i], top[i + 1], bot[i + 1], bot[i])); bm.faces.new((top2[i + 1], top2[i], bot2[i], bot2[i + 1]))
            bm.faces.new((top[i + 1], top[i], top2[i], top2[i + 1])); bm.faces.new((bot[i], bot[i + 1], bot2[i + 1], bot2[i]))
        bm.faces.new((top[0], bot[0], bot2[0], top2[0])); bm.faces.new((top[-1], top2[-1], bot2[-1], bot[-1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.normal_update(); box_uv(bm, TILE["M_Wood"]); bms.append((bm, "M_Wood"))
    for z0 in (0.16, -0.16):  # Querleisten, halten die Bohlen zusammen (zeigen die Drehung deutlich)
        bms.append((tube(None, [(x + sg * 0.07, -R * 0.8, axle_z + z0), (x + sg * 0.07, R * 0.8, axle_z + z0)], [0.03, 0.03], 6, "M_Bark"), "M_Bark"))
    bms.append((tube(None, [(x + sg * 0.05, 0, axle_z), (x + sg * 0.16, 0, axle_z)], [0.09, 0.08], 10, "M_Bark"), "M_Bark"))  # Nabe
    CART.append(obj_from("Cart_Wheel_" + k, bms, (x, 0, axle_z)))

def export(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    tris = 0
    for o in objs: o.data.calc_loop_triangles(); tris += len(o.data.loop_triangles)
    print(f"STATS {name} tris={tris} parts={[o.name for o in objs]}")
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", path_mode="STRIP", add_leaf_bones=False)
os.makedirs(OUT, exist_ok=True)
export(OX, "WB_Unit_Ox"); export(CART, "WB_Unit_Cart")

if PREVIEW:  # Ochse vor den Karren gespannt (Ochse-Ursprung = Karren-Ursprung + (0, OX_OFFSET, 0))
    OX_OFFSET = YOKE_Y + 0.72
    for o in OX: o.location.y += OX_OFFSET
    bpy.ops.mesh.primitive_plane_add(size=40); g = bpy.context.object
    gm = bpy.data.materials.new("G"); gm.use_nodes = True; gt = gm.node_tree.nodes.new("ShaderNodeTexImage")
    gt.image = bpy.data.images.load("/home/user/World-Beyond/Assets/_Project/Art/Terrain/Resources/WorldBeyondTerrain/T_Ground_Grass_D.jpg")
    gm.node_tree.links.new(gt.outputs["Color"], gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"]); g.data.materials.append(gm)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.uv.cube_project(cube_size=5); bpy.ops.object.mode_set(mode="OBJECT")
    sun = bpy.data.lights.new("S", "SUN"); sun.energy = 3.2; so = bpy.data.objects.new("S", sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-40))
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.72, 0.88, 1)
    cam = bpy.data.cameras.new("C"); cam.lens = 35; co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (4.6, -4.2, 2.2); tgt = Vector((0, -1.6, 0.9)); co.rotation_euler = (tgt - co.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"; sc.cycles.samples = SAMPLES; sc.cycles.device = "CPU"; sc.cycles.use_denoising = False
    sc.render.resolution_x = 1200; sc.render.resolution_y = 800; sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
    sc.render.filepath = PREVIEW; bpy.ops.render.render(write_still=True)
