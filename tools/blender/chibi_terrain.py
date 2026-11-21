"""卡通地形装饰（chibi-world 段 ①）：在 Blender 里用代码生成 18 类（19 种 Kind）装饰的 glb，风格同 chibi_pieces.py。

运行（无界面，全部）：
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup \
      --python tools/blender/chibi_terrain.py -- src/godot/parts/terrain_chibi
只生成其中几种：在输出目录后面追加 Kind 名，如 `... -- src/godot/parts/terrain_chibi rock chest`。
生成后必须在 Godot 里重新导入：Godot --headless --path src/godot --import

约定（与 TerrainParts.LoadChibi 一致）：
- 文件名 = TerrainParts.Kind.FileName 换成 .glb（档数 1 时不带后缀，否则 <名>_<档>.glb）。档数从 TerrainParts.cs / LowPoly.cs 读出来核对，
  与脚本里的 VARIANTS 不一致即退出码 1、什么都不导出。
- 物体名 = "<材质槽>_<部件名>"，槽见 SLOTS；运行时按槽合并成一个网格、按 (种类, 档, 槽) 上卡通分阶材质（TerrainParts.ChibiColor），
  glb 里的材质不使用。Line = 描边外壳（运行时节点名 Outline，cull front、无光照、墨色，不投影）。
- Line_Hull 由脚本把本体零件沿顶点法线外推 OUTLINE[种类] 得到；贴地的薄片（水纹、裂纹、水洼、沙纹）标 flat，不进外壳——
  它们几乎没有厚度，外壳只会在远侧画出一道黑边。所以描边面数 ≤ 本体面数（"描边同面数"按上限读）。
- 不建父子层级，导出时应用修改器与变换（变体的整体朝向直接烘进网格）。
- Blender 坐标：x 右、-y 朝前（导出后为 Godot +Z）、z 上；原点 = 所在格地砖上表面（Kind.Anchor）。单位与棋盘格一致（格 1.0、地砖 0.9）。
- 断言（任一不过即不导出该件，最后退出码 1）：本体 ≤ MAX_BODY 三角形、描边 ≤ 本体、材质槽（含 Line）≤ MAX_SLOTS、
  本体包围盒不超出 terrain_full_art 现有同名部件（OLD_BOUNDS，Godot 坐标，容差 BOUNDS_TOL）、
  可落子格上的装饰（PLAYABLE）在格心半径 CENTER_CLEAR 内不高于 CENTER_MAX_Z（不挡格心与单位）、零件法线朝外（inward_parts）。
"""
import math
import os
import random
import re
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

SLOTS = {"Stone", "Shade", "Wood", "Leaf", "Moss", "Water", "Crystal", "Gold", "Flame", "Glow", "Bloom", "Bone"}
MAX_BODY = 600
MAX_SLOTS = 7  # 用户裁决 6（2026-11-21）：火盆加黄色内焰（Glow 槽），上限与单位一致
BOUNDS_TOL = 0.006
CENTER_CLEAR = 0.36   # 棋子脚下圆环外半径
CENTER_MAX_Z = 0.0072  # 格心圆内只许贴地薄片（同旧水洼的 0.007；领地着色在 +0.008）
CENTER_MAX_Z_OF = {"stele": 0.045}  # 石碑座的内角在格心 0.315 处（与旧石碑同位），只有 0.04 高
FLAT = "chibi_flat"   # 物体自定义属性：贴地薄片，不进描边外壳

# 描边厚度：起点 0.02（大件障碍），小件按尺寸压细，免得成黑团。
OUTLINE = {
    "rock": 0.022, "ruins": 0.02, "pines": 0.02, "crystal": 0.02, "brazier": 0.018, "arch": 0.02, "chest": 0.018,
    "trees": 0.015, "desert": 0.014, "marsh": 0.012, "crag": 0.014, "shallows": 0.010,
    "fence_x": 0.014, "fence_z": 0.014, "bridge": 0.013, "stele": 0.014,
    "grass": 0.010, "pebble": 0.010, "flower": 0.007,
}

# 档数：与 TerrainParts.Kind 一一对应（脚本启动时从 C# 读出来核对）。
VARIANTS = {
    "rock": 6, "ruins": 4, "pines": 7, "trees": 4, "desert": 6, "marsh": 6, "crag": 4, "shallows": 6,
    "fence_x": 1, "fence_z": 1, "bridge": 1, "grass": 4, "crystal": 3, "brazier": 2, "arch": 2, "chest": 2,
    "pebble": 3, "flower": 3, "stele": 2,
}

# 放在可落子格上的装饰：格心圆内只许贴地薄片。
PLAYABLE = {"trees", "desert", "marsh", "crag", "shallows", "bridge", "grass", "pebble", "flower", "stele"}

# terrain_full_art 现有部件的包围盒（Godot 坐标 min x,y,z / max x,y,z；2026-11-21 从 .tscn 实测）。
# 格角类（grass / pebble / flower / stele）逐档核对；其余按同类各档的并集核对。
OLD_BOUNDS = {
    "arch_0": ((-0.370, -0.015, -0.100), (0.370, 0.700, 0.338)),
    "arch_1": ((-0.100, -0.020, -0.370), (0.339, 0.700, 0.370)),
    "brazier_0": ((-0.240, -0.020, -0.240), (0.300, 0.600, 0.250)),
    "brazier_1": ((-0.237, -0.011, -0.237), (0.311, 0.600, 0.246)),
    "bridge": ((-0.450, -0.060, -0.450), (0.450, 0.130, 0.450)),
    "chest": ((-0.286, -0.020, -0.336), (0.335, 0.247, 0.282)),   # 两档实测 (x -0.286..0.335, z -0.162..0.192) / (x -0.166..0.178, z -0.336..0.282) 的并集
    "crag": ((-0.450, -0.030, -0.450), (0.450, 0.056, 0.450)),
    "crystal": ((-0.357, -0.052, -0.327), (0.331, 0.570, 0.318)),
    "desert": ((-0.430, -0.001, -0.430), (0.428, 0.230, 0.428)),
    "fence_x": ((-0.480, 0.000, -0.025), (0.480, 0.303, 0.025)),
    "fence_z": ((-0.025, 0.000, -0.480), (0.025, 0.303, 0.480)),
    "flower_0": ((0.325, 0.000, -0.450), (0.457, 0.054, -0.331)),
    "flower_1": ((0.319, 0.000, 0.327), (0.449, 0.062, 0.415)),
    "flower_2": ((-0.437, 0.000, 0.317), (-0.316, 0.057, 0.435)),
    "grass_0": ((-0.436, -0.004, -0.428), (-0.272, 0.167, -0.277)),
    "grass_1": ((0.280, -0.004, -0.410), (0.423, 0.169, -0.283)),
    "grass_2": ((0.294, -0.004, 0.288), (0.409, 0.195, 0.438)),
    "grass_3": ((-0.433, -0.004, 0.280), (-0.291, 0.189, 0.409)),
    "marsh": ((-0.395, 0.000, -0.396), (0.402, 0.215, 0.400)),
    "pebble_0": ((-0.449, -0.017, -0.458), (-0.334, 0.047, -0.306)),
    "pebble_1": ((0.302, -0.014, -0.449), (0.417, 0.041, -0.341)),
    "pebble_2": ((0.313, -0.013, 0.300), (0.440, 0.045, 0.418)),
    "pines": ((-0.366, 0.000, -0.339), (0.366, 0.720, 0.339)),
    "rock": ((-0.395, -0.130, -0.388), (0.407, 0.448, 0.397)),
    "ruins": ((-0.320, 0.000, -0.320), (0.320, 0.545, 0.320)),
    "shallows": ((-0.434, -0.013, -0.431), (0.429, 0.031, 0.433)),
    "stele_0": ((-0.390, 0.000, -0.365), (-0.210, 0.340, -0.235)),
    "stele_1": ((-0.390, 0.000, -0.365), (-0.210, 0.370, -0.235)),
    "trees": ((-0.426, 0.000, -0.427), (0.430, 0.254, 0.428)),
}

# 格角序（同 LowPoly.Trees / Grass）：Godot (x, z) → Blender (x, -z)。
CORNERS = [(-1.0, 1.0), (1.0, 1.0), (1.0, -1.0), (-1.0, -1.0)]


def old_center(stem):
    """旧部件包围盒在地面上的中心（Blender 坐标）：格角点缀按它对位，免得旧件偏向格边一侧而新件居中出界。"""
    (lo, hi) = OLD_BOUNDS[stem]
    return Vector(((lo[0] + hi[0]) / 2, -(lo[2] + hi[2]) / 2, 0.0))


def corner(k, reach):
    cx, cy = CORNERS[k % 4]
    return Vector((cx * reach, cy * reach, 0.0))


# ---------- 场景与零件 ----------

def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


_names = {}


def unique(name):
    n = _names.get(name, 0)
    _names[name] = n + 1
    return name if n == 0 else f"{name}{n}"


def finish(name, smooth=True, bevel=0.0, flat=False):
    slot = name.split("_", 1)[0]
    assert slot in SLOTS, name
    name = unique(name)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        mod.limit_method = "NONE"
    if smooth:
        obj.data.shade_smooth()
    else:
        obj.data.shade_flat()
    if flat:
        obj[FLAT] = True
    return obj


def sphere(name, loc, radius, scale=(1, 1, 1), seg=8, rings=5, rot=(0, 0, 0), smooth=True):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=radius, location=loc, rotation=rot)
    bpy.context.active_object.scale = scale
    return finish(name, smooth)


def blob(name, loc, radius, scale=(1, 1, 1), rng=None, jitter=0.12, subdiv=2, rot=(0, 0, 0), smooth=True):
    """圆润的卡通石块：二十面体（subdiv 1 = 20 面、2 = 80 面）按散列径向抖动，再缩放。"""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=radius, location=(0, 0, 0))
    obj = bpy.context.active_object
    if rng is not None and jitter > 0:
        for v in obj.data.vertices:
            v.co *= 1.0 + jitter * (rng.random() * 2 - 1)
    obj.location = loc
    obj.rotation_euler = rot
    obj.scale = scale
    return finish(name, smooth)


def cyl(name, loc, radius, depth, rot=(0, 0, 0), verts=8, smooth=True, bevel=0.0, caps=True, scale=(1, 1, 1), flat=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot,
                                        end_fill_type="NGON" if caps else "NOTHING")
    bpy.context.active_object.scale = scale
    return finish(name, smooth, bevel, flat)


def cone(name, loc, r1, r2, depth, rot=(0, 0, 0), verts=8, smooth=True, caps=True, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=rot,
                                    end_fill_type="NGON" if caps else "NOTHING")
    bpy.context.active_object.scale = scale
    return finish(name, smooth)


def box(name, loc, size, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    bpy.context.active_object.scale = size
    return finish(name, smooth=False, bevel=bevel)


def rod(name, p0, p1, radius, verts=6, caps=True, smooth=True):
    d = Vector(p1) - Vector(p0)
    mid = (Vector(p0) + Vector(p1)) / 2
    tilt = math.acos(max(-1.0, min(1.0, d.z / d.length)))
    heading = math.atan2(d.y, d.x)
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=d.length, location=mid,
                                        rotation=(0, tilt, heading), end_fill_type="NGON" if caps else "NOTHING")
    return finish(name, smooth)


def spike(name, base, direction, length, radius, verts=6, smooth=False):
    """从 base 沿 direction 长出的尖锥（草叶、晶尖、火舌、芦苇）。"""
    d = Vector(direction).normalized()
    center = Vector(base) + d * (length / 2)
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius, radius2=0, depth=length, location=center,
                                    rotation=(0, math.acos(max(-1.0, min(1.0, d.z))), math.atan2(d.y, d.x)))
    return finish(name, smooth)


def mesh_object(name, verts, faces, smooth=False, flat=False):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("tmp", mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    return finish(name, smooth, flat=flat)


def prism(name, profile, thickness, loc, rot=(0, 0, 0), smooth=False):
    """xz 平面上的凸轮廓沿 y 挤出成厚板（碑、门板）。轮廓坐标相对 loc，正面朝 -y。"""
    n = len(profile)
    verts = [(x, -thickness / 2, z) for x, z in profile] + [(x, thickness / 2, z) for x, z in profile]
    faces = [list(range(n)), list(reversed(range(n, 2 * n)))]
    for i in range(n):
        j = (i + 1) % n
        faces.append([j, i, n + i, n + j])
    obj = mesh_object(name, verts, faces, smooth)
    obj.location = loc
    obj.rotation_euler = rot
    return obj


def arch_profile(half_w, rect_h, arch_h, steps=8):
    pts = [(-half_w, 0.0), (half_w, 0.0), (half_w, rect_h)]
    for i in range(1, steps):
        t = math.pi * i / steps
        pts.append((half_w * math.cos(t), rect_h + arch_h * math.sin(t)))
    pts.append((-half_w, rect_h))
    return pts


def ribbon(name, points, width, z=0.004, thick=0.006):
    """贴地的折线带（水纹、沙纹、裂纹）：薄薄一层，不进描边。"""
    left, right = [], []
    for i, p in enumerate(points):
        a = points[max(i - 1, 0)]
        b = points[min(i + 1, len(points) - 1)]
        t = Vector((b[0] - a[0], b[1] - a[1], 0)).normalized()
        nrm = Vector((-t.y, t.x, 0)) * (width / 2)
        left.append(Vector((p[0], p[1], 0)) + nrm)
        right.append(Vector((p[0], p[1], 0)) - nrm)
    n = len(points)
    verts = []
    for p in left + right:
        verts.append((p.x, p.y, z - thick))
    for p in left + right:
        verts.append((p.x, p.y, z))
    faces = []
    top = 2 * n
    for i in range(n - 1):
        faces.append([top + i, top + i + 1, top + n + i + 1, top + n + i])          # 顶
        faces.append([i, n + i, n + i + 1, i + 1])                                    # 底
        faces.append([i, i + 1, top + i + 1, top + i])                                # 左
        faces.append([n + i, top + n + i, top + n + i + 1, n + i + 1])                # 右
    faces.append([0, top, top + n, n])
    faces.append([n - 1, 2 * n - 1, top + 2 * n - 1, top + n - 1])
    return mesh_object(name, verts, faces, smooth=False, flat=True)


def disc(name, loc, radius, height, rng, verts=9, wobble=0.18, scale=(1, 1), flat=True, rot=0.0):
    """贴地的扁圆斑（水洼、睡莲叶）：边缘按散列起伏。"""
    pts = []
    for i in range(verts):
        a = rot + 2 * math.pi * i / verts
        r = radius * (1 + wobble * (rng.random() * 2 - 1))
        pts.append((loc[0] + math.cos(a) * r * scale[0], loc[1] + math.sin(a) * r * scale[1]))
    z0, z1 = loc[2] - height, loc[2]
    vs = [(x, y, z0) for x, y in pts] + [(x, y, z1) for x, y in pts]
    faces = [list(reversed(range(verts))), list(range(verts, 2 * verts))]
    for i in range(verts):
        j = (i + 1) % verts
        faces.append([i, j, verts + j, verts + i])
    return mesh_object(name, vs, faces, smooth=False, flat=flat)


def rotate_all(angle_deg, mirror_x=False):
    """把当前全部零件绕原点（格心）整体转 angle_deg（Blender 绕 +Z，等于 Godot 绕 +Y 同角），可先沿 x 镜像。"""
    bpy.context.view_layer.update()   # 刚设过 location / scale 的物体，matrix_world 要刷新后才是新值
    m = Matrix.Rotation(math.radians(angle_deg), 4, "Z")
    if mirror_x:
        m = m @ Matrix.Scale(-1, 4, (1, 0, 0))
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            obj.matrix_world = m @ obj.matrix_world
    if mirror_x:
        # 镜像让每个面的绕序反过来：应用变换后把全部面翻回来（确定性的，不靠开口网格上不可靠的法线重算）
        for obj in bpy.data.objects:
            if obj.type == "MESH":
                apply_transform(obj)
                bm = bmesh.new()
                bm.from_mesh(obj.data)
                bmesh.ops.reverse_faces(bm, faces=bm.faces)
                bm.to_mesh(obj.data)
                bm.free()


def apply_transform(obj):
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)


# ---------- 18 类装饰 ----------

def build_rock(v):
    """障碍巨石：一大一中两块圆胖石块挤在一格里，底部没入地砖；偶数档顶上一枚紫晶，奇数档一撮青苔。"""
    rng = random.Random(701 + v * 16)
    blob("Stone_Big", (-0.05, -0.03, 0.16), 0.235, scale=(1.15, 1.0, 1.05), rng=rng, jitter=0.08)
    blob("Shade_Mid", (0.15, 0.10, 0.06), 0.16, scale=(1.05, 1.0, 1.0), rng=rng, jitter=0.10)
    blob("Stone_Small", (0.10, -0.22, 0.02), 0.085, scale=(1.2, 1.0, 0.85), rng=rng, jitter=0.12)
    blob("Shade_Chip", (-0.24, 0.19, 0.01), 0.055, scale=(1.2, 1.0, 0.8), rng=rng, jitter=0.12, subdiv=1)
    if v % 2 == 0:
        spike("Crystal_Shard", (-0.10, 0.03, 0.30), (0.25, 0.1, 1.0), 0.13, 0.045, verts=6)
        spike("Crystal_ShardB", (-0.02, -0.04, 0.31), (-0.3, -0.35, 1.0), 0.09, 0.032, verts=6)
    else:
        blob("Moss_Cap", (-0.06, -0.03, 0.385), 0.13, scale=(1.15, 1.0, 0.30), rng=rng, jitter=0.06)
    rotate_all(v * 60 + 15)


def build_ruins(v):
    """障碍遗迹：深色石台上一座砂岩础 + 一根断柱（断口斜削），旁边一段倒伏的柱身、两块碎石与一枚金色碎片；整组按档每档转 90°。"""
    rng = random.Random(1601 + v)
    box("Shade_Plinth", (0, 0, 0.035), (0.62, 0.62, 0.07), bevel=0.02)
    box("Stone_Base", (-0.15, 0.12, 0.10), (0.24, 0.24, 0.06), bevel=0.015)
    h = 0.27 + 0.06 * (v & 1)
    cyl("Stone_Column", (-0.15, 0.12, 0.13 + h / 2), 0.085, h, verts=10)
    # 断口：顶上一圈略细的斜削圆台，读作"断了"
    cone("Stone_Break", (-0.15, 0.12, 0.13 + h + 0.02), 0.085, 0.055, 0.04, rot=(0.35, 0.2, 0), verts=10)
    rod("Stone_Drum", (0.05, -0.12, 0.13), (0.27, -0.21, 0.13), 0.065, verts=10)
    blob("Shade_RubbleA", (0.20, 0.19, 0.09), 0.055, scale=(1.2, 1.0, 0.8), rng=rng, subdiv=1)
    blob("Shade_RubbleB", (0.24, 0.07, 0.08), 0.035, scale=(1.2, 1.0, 0.8), rng=rng, subdiv=1)
    spike("Gold_Shard", (0.17, 0.02, 0.065), (0.3, -0.1, 1.0), 0.12, 0.03, verts=6)
    if v % 2 == 0:
        blob("Moss_Ivy", (-0.24, 0.03, 0.09), 0.07, scale=(1.1, 1.0, 0.45), rng=rng)
    rotate_all(v * 90)


def build_pines(v):
    """障碍松树丛：两三棵三层圆锥的卡通松树（深浅两种绿相间），树干短粗；高 ≤ 0.71。"""
    spots = {
        0: [((-0.16, -0.10), 1.15), ((0.18, 0.12), 0.88)],
        1: [((0.02, -0.02), 1.20), ((-0.235, 0.20), 0.74), ((0.235, -0.20), 0.80)],
        2: [((0.14, -0.12), 1.12), ((-0.18, 0.10), 0.98)],
    }[v % 3]
    for i, ((x, y), s) in enumerate(spots):
        cyl("Wood_Trunk", (x, y, 0.07 * s), 0.04 * s, 0.14 * s, verts=6, caps=False)
        slot = "Leaf" if (v + i) % 2 == 0 else "Moss"
        for tier in range(3):
            radius = (0.165 - tier * 0.037) * s
            height = (0.20 - tier * 0.012) * s
            bottom = (0.12 + tier * 0.135) * s
            cone(f"{slot}_Tier", (x, y, bottom + height / 2), radius, 0.0 if tier == 2 else 0.02 * s, height,
                 rot=(0, 0, tier * 0.5 + i), verts=8)


def build_trees(v):
    """林地小树：四个格角空一个，其余各一棵棒棒糖小树（短干 + 圆树冠 + 一团浅绿小冠）；全部在棋子圆环之外。"""
    rng = random.Random(801 + v * 8)
    for k in range(4):
        if k == v:
            continue
        s = 0.92 + 0.08 * rng.random()
        at = corner(k, 0.335)
        cyl("Wood_Trunk", (at.x, at.y, 0.055), 0.024 * s, 0.11, verts=6, caps=False)
        sphere("Leaf_Crown", (at.x, at.y, 0.15), 0.082 * s, scale=(1, 1, 0.92), seg=8, rings=6)
        out = at.normalized() * 0.022
        sphere("Moss_Tuft", (at.x + out.x, at.y + out.y, 0.205), 0.045 * s, seg=8, rings=5)


def build_desert(v):
    """荒漠点缀：一株带侧臂的胖仙人掌（顶上一朵小花）、一株矮仙人掌、一副交叉兽骨分放三个格角，两道贴地沙纹斜穿格面。"""
    rng = random.Random(901 + v * 16)
    # 沙纹（贴地薄片，不描边）
    for i in (-1, 1):
        pts = []
        for k in range(5):
            t = -0.31 + k * 0.155
            wave = 0.022 * (rng.random() * 2 - 1)
            a = math.radians(-18)
            along = (math.cos(a), math.sin(a))
            across = (-along[1], along[0])
            pts.append((along[0] * t + across[0] * (i * 0.12 + wave), along[1] * t + across[1] * (i * 0.12 + wave)))
        ribbon("Shade_Ripple", pts, 0.026)
    # 大仙人掌：格角 (0.34, 0.34)
    c = Vector((0.34, 0.34, 0))
    cyl("Leaf_Cactus", (c.x, c.y, 0.08), 0.05, 0.16, verts=8, caps=False)
    sphere("Leaf_CactusTop", (c.x, c.y, 0.16), 0.05, scale=(1, 1, 1.15), seg=8, rings=4)
    arm = -1 if v % 3 == 0 else 1
    t = Vector((1, -1, 0)).normalized() * arm
    elbow = c + t * 0.085
    rod("Leaf_ArmOut", tuple(c + t * 0.03 + Vector((0, 0, 0.085))), tuple(elbow + Vector((0, 0, 0.085))), 0.024, verts=6)
    cyl("Leaf_ArmUp", (elbow.x, elbow.y, 0.12), 0.024, 0.07, verts=6, caps=False)
    sphere("Leaf_ArmTop", (elbow.x, elbow.y, 0.155), 0.024, seg=6, rings=4)
    sphere("Bloom_Flower", (c.x, c.y, 0.218), 0.022, scale=(1, 1, 0.55), seg=6, rings=4)
    # 矮仙人掌：对角
    s = Vector((-0.34, -0.34, 0))
    cyl("Leaf_Small", (s.x, s.y, 0.045), 0.042, 0.09, verts=8, caps=False)
    sphere("Leaf_SmallTop", (s.x, s.y, 0.09), 0.042, scale=(1, 1, 1.1), seg=8, rings=4)
    # 兽骨：第三个格角，两根交叉的细骨 + 骨节
    b = Vector((0.32, -0.32, 0))
    for (dx, dy), half in (((1, 1), 0.085), ((1, -0.4), 0.06)):
        d = Vector((dx, dy, 0)).normalized()
        p0 = b + d * half + Vector((0, 0, 0.016))
        p1 = b - d * half + Vector((0, 0, 0.016))
        rod("Bone_Shaft", tuple(p0), tuple(p1), 0.012, verts=6, caps=False)
        for p in (p0, p1):
            sphere("Bone_Knob", tuple(p), 0.019, seg=6, rings=3)
    rotate_all((v % 4) * 90, mirror_x=v >= 4)


def build_marsh(v):
    """沼泽点缀：两三块贴地水洼（一片睡莲叶），两个格角上的芦苇丛（细叶 + 一根香蒲穗）。"""
    rng = random.Random(1001 + v * 16)
    sx = 1 if v % 2 == 0 else -1
    puddles = [((-0.10, -0.05), 0.20), ((0.16, 0.10), 0.12)] if v % 3 == 0 else \
        [((0.08, -0.10), 0.17), ((-0.14, 0.08), 0.14), ((0.18, 0.18), 0.08)]
    for i, ((x, y), r) in enumerate(puddles):
        disc("Water_Puddle", (x * sx, y, 0.006), r * 0.92, 0.008, rng, verts=10, wobble=0.15, rot=v * 0.5 + i)
    (px, py), pr = puddles[0]
    disc("Leaf_Pad", (px * sx + pr * 0.3, py - pr * 0.2, 0.007), 0.04, 0.002, rng, verts=7, wobble=0.05)
    for ci, (cx, cy) in enumerate(((0.33 * sx, 0.33), (-0.33 * sx, -0.33))):
        for k in range(4):
            a = math.radians(k * 90 + v * 13 + ci * 45)
            d = Vector((math.cos(a), math.sin(a), 0))
            base = Vector((cx, cy, 0)) + d * 0.028
            height = 0.14 + 0.022 * ((k + v) % 3)
            spike("Leaf_Reed", tuple(base), tuple(d * 0.22 + Vector((0, 0, 1))), height, 0.013, verts=4, smooth=True)
        rod("Wood_Stalk", (cx, cy, 0.0), (cx + 0.01, cy, 0.17), 0.006, verts=4, caps=False)
        sphere("Wood_Cattail", (cx + 0.01, cy, 0.18), 0.018, scale=(1, 1, 1.9), seg=6, rings=4)


def build_crag(v):
    """岩台石面：沿地砖内缘一圈圆胖的矮石沿（每边两块，高 ≤ 0.045），三四道贴地裂纹，两颗格角碎石。石沿只读作"加厚边缘"，不读作高一层。"""
    rng = random.Random(1101 + v * 16)
    half, band = 0.45, 0.05
    sides = [((0, half - band / 2), True, 0.9), ((0, -half + band / 2), True, 0.9),
             ((-half + band / 2, 0), False, 0.9 - 2 * band), ((half - band / 2, 0), False, 0.9 - 2 * band)]
    for s, ((cx, cy), along_x, length) in enumerate(sides):
        cut = 0.12 * (rng.random() * 2 - 1)
        for a, b in ((-length / 2, cut - 0.006), (cut + 0.006, length / 2)):
            top = 0.042 - 0.006 * rng.random()
            mid = (a + b) / 2
            size = (b - a, band, top + 0.03) if along_x else (band, b - a, top + 0.03)
            loc = (cx + (mid if along_x else 0), cy + (0 if along_x else mid), (top - 0.03) / 2)
            box("Stone_Rim", loc, size, bevel=0.013)
    turn = math.radians((v * 41) % 90)
    cracks = [((-0.12, 0.08), 0.26, 25), ((0.02, 0.02), 0.18, -40), ((0.12, -0.12), 0.22, 70), ((-0.16, -0.16), 0.14, -10)]
    for i, ((x, y), ln, ang) in enumerate(cracks[:len(cracks) - (v % 2)]):
        a = math.radians(ang) + turn
        d = Vector((math.cos(a), math.sin(a), 0))
        n = Vector((-d.y, d.x, 0))
        c = Vector((x, y, 0))
        bend = n * (0.025 * (rng.random() * 2 - 1))
        pts = [c - d * (ln / 2), c - d * (ln * 0.12) + bend, c + d * (ln / 2) - bend * 0.4]
        ribbon("Shade_Crack", [(p.x, p.y) for p in pts], 0.022)
    sx = 1 if v % 2 == 0 else -1
    blob("Shade_Chip", (0.31 * sx, 0.31, 0.018), 0.042, scale=(1.15, 1.0, 0.75), rng=rng, subdiv=1)
    blob("Stone_Chip", (-0.31 * sx, -0.31, 0.014), 0.032, scale=(1.1, 1.0, 0.8), rng=rng, subdiv=1)


def build_shallows(v):
    """浅滩：格心圈外一圈圆润的鹅卵石（棋子圆环之外，其中一颗长了青苔），两道贴地白色水纹弧。"""
    rng = random.Random(1201 + v * 16)
    sx = 1 if v % 2 == 0 else -1
    for i in (-1, 1):
        cx, cy = 0.06 * i * sx, -0.14 * i
        start = math.radians(-20 * sx + i * 8 + 40 * (rng.random() * 2 - 1))
        pts = []
        for k in range(4):
            a = start + k * 0.38
            pts.append((cx + math.cos(a) * 0.17 - math.cos(start + 0.57) * 0.17, cy - (math.sin(a) * 0.08 - math.sin(start + 0.57) * 0.08)))
        ribbon("Bone_Ripple", pts, 0.016, z=0.003)
    for i in range(6 - (v % 2)):
        r = 0.024 + 0.008 * rng.random()
        a = math.radians(i * 60 + v * 17 + 20 * (rng.random() * 2 - 1))
        slot = "Moss" if i == 2 else "Stone"
        sphere(f"{slot}_Pebble", (math.cos(a) * 0.40, math.sin(a) * 0.40, r * 0.2), r, scale=(1.1, 1.0, 0.6),
               seg=8, rings=4, rot=(0, 0, rng.random() * math.pi))


def build_fence(along_x):
    """栅栏：三根深木立柱（顶上一枚金色圆钮）+ 两道浅木横杆，厚 ≤ 0.05，立在两格之间的缝上。"""
    for x in (-0.40, 0.0, 0.40):
        box("Shade_Post", (x, 0, 0.125), (0.042, 0.042, 0.25), bevel=0.006)
        sphere("Gold_Knob", (x, 0, 0.27), 0.022, seg=8, rings=4)
    for z in (0.11, 0.21):
        box("Wood_Rail", (0, 0, z), (0.96, 0.028, 0.036), bevel=0.006)
    if not along_x:
        rotate_all(90)


def build_fence_x(_v):
    build_fence(True)


def build_fence_z(_v):
    build_fence(False)


def build_bridge(_v):
    """预置桥：七块原木色桥板（面与地砖齐平），板下两根深木托梁，两侧各三根立柱（金色柱帽），柱间两道下垂的象牙色绳栏；过桥方向沿 Godot Z。"""
    planks, seam = 7, 0.02
    w = (0.9 - (planks - 1) * seam) / planks
    for i in range(planks):
        y = -0.45 + w / 2 + i * (w + seam)
        box("Bloom_Plank", (0, y, -0.02), (0.9, w, 0.04))
    for x in (-0.28, 0.28):
        box("Shade_Stringer", (x, 0, -0.05), (0.08, 0.9, 0.02))
    side, pier, post = 0.415, 0.07, 0.04
    end = 0.45 - pier / 2 - 0.015
    tops = []
    for sx in (-side, side):
        for y, size, top, bottom in ((-end, pier, 0.112, -0.06), (end, pier, 0.112, -0.06), (0.0, post, 0.104, 0.0)):
            box("Wood_Post", (sx, y, (top + bottom) / 2), (size, size, top - bottom))
            tops.append((sx, y, top, size))
    for sx, y, top, size in tops:
        sphere("Gold_Cap", (sx, y, top), size * 0.55, scale=(1, 1, 0.55 if size > post else 0.6), seg=6, rings=3)
    reach = end
    for sx in (-side, side):
        for z in (0.100, 0.056):
            for a, b in ((-reach, 0.0), (0.0, reach)):
                mid = (a + b) / 2
                rod("Bone_Rope", (sx, a, z), (sx, mid, z - 0.02), 0.007, verts=4, caps=False)
                rod("Bone_Rope", (sx, mid, z - 0.02), (sx, b, z), 0.007, verts=4, caps=False)


def build_grass(v):
    """草丛：格角内侧一簇七片圆润草叶（深浅两种绿）+ 一团矮草墩；整簇在棋子圆环之外、出生区亮条之内。"""
    rng = random.Random(2101 + v * 11)
    c = corner(v, 0.352)
    sphere("Moss_Clump", (c.x, c.y, 0.018), 0.032, scale=(1.15, 1.15, 0.7), seg=6, rings=4)
    for i in range(7):
        a = 2 * math.pi * i / 7 + rng.random() * 0.6
        r = 0.0 if i == 0 else 0.014 + 0.02 * rng.random()
        base = c + Vector((math.cos(a) * r, math.sin(a) * r, -0.004))
        height = 0.10 + 0.05 * rng.random()
        lean = Vector((math.cos(a), math.sin(a), 0)) * 0.16
        spike(f"{'Leaf' if (i + v) % 2 == 0 else 'Moss'}_Blade", tuple(base), tuple(lean + Vector((0, 0, 1))), height, 0.019,
              verts=4, smooth=True)


def crystal_rod(slot, base, direction_deg, lean_deg, length, radius):
    """六棱晶柱 + 尖顶：从 base 出发朝方位 direction_deg、偏离竖直 lean_deg 生长（同 chibi_pieces.crystal）。"""
    d, l = math.radians(direction_deg), math.radians(lean_deg)
    u = Vector((math.sin(l) * math.cos(d), math.sin(l) * math.sin(d), math.cos(l)))
    body_end = Vector(base) + u * length * 0.72
    tip = Vector(base) + u * length
    rod(f"{slot}_Prism", base, tuple(body_end), radius, verts=6, smooth=False)
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=radius, radius2=0, depth=length * 0.28,
                                    location=(body_end + tip) / 2, rotation=(0, math.acos(u.z), math.atan2(u.y, u.x)))
    finish(f"{slot}_Tip", smooth=False)


def build_crystal(v):
    """障碍晶簇：扁圆石座上一根粗主晶 + 三根外倾副晶 + 一根浅色小晶（六棱尖顶），两块深色碎石。"""
    rng = random.Random(2201 + v * 13)
    blob("Stone_Base", (0, 0, 0.035), 0.20, scale=(1.2, 1.05, 0.42), rng=rng, jitter=0.08)
    blob("Shade_RockA", (0.21, 0.13, 0.02), 0.08, scale=(1.1, 1.0, 0.65), rng=rng, subdiv=1)
    blob("Shade_RockB", (-0.19, -0.17, 0.015), 0.06, scale=(1.2, 1.0, 0.6), rng=rng, subdiv=1)
    crystal_rod("Crystal", (0.0, 0.0, 0.05), 40, 6, 0.46 + 0.03 * v, 0.072)
    for i in range(3):
        a = 120 * i + 50 * rng.random()
        r = 0.12 + 0.03 * rng.random()
        base = (math.cos(math.radians(a)) * r, math.sin(math.radians(a)) * r, 0.05)
        crystal_rod("Crystal", base, a, 18 + 10 * rng.random(), 0.22 + 0.08 * rng.random(), 0.042 + 0.01 * rng.random())
    g = 360 * rng.random()
    crystal_rod("Bloom", (math.cos(math.radians(g)) * 0.07, math.sin(math.radians(g)) * 0.07, 0.06), g, 22, 0.20, 0.03)
    rotate_all(v * 120 + 25)


def build_brazier(v):
    """障碍火盆：方石座 + 收腰石柱 + 外张石盆（金色盆沿），盆里一团余烬、黄色内焰（Glow）与橙色火尖 / 三条火舌（都不受光）。"""
    rng = random.Random(2301 + v * 7)
    box("Shade_Base", (0, 0, 0.025), (0.32, 0.32, 0.05), bevel=0.012)
    cone("Stone_Pillar", (0, 0, 0.16), 0.12, 0.08, 0.22, verts=8)
    cone("Stone_Bowl", (0, 0, 0.325), 0.09, 0.19, 0.11, verts=10)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.188, minor_radius=0.02, major_segments=12, minor_segments=4,
                                     location=(0, 0, 0.38))
    finish("Gold_Rim")
    sphere("Bloom_Ember", (0, 0, 0.375), 0.16, scale=(1, 1, 0.3), seg=8, rings=4)
    sphere("Glow_Core", (0, 0, 0.43), 0.085, seg=8, rings=4)
    cone("Flame_Tip", (0, 0, 0.51), 0.083, 0.0, 0.16, verts=8)
    for i in range(3):
        a = math.radians(120 * i + 40 * v + 30 * rng.random())
        p = Vector((math.cos(a) * 0.09, math.sin(a) * 0.09, 0.40))
        sphere("Flame_Lick", tuple(p), 0.045, seg=6, rings=4)
        spike("Flame_LickTip", tuple(p + Vector((0, 0, 0.02))), (math.cos(a) * 0.25, math.sin(a) * 0.25, 1.0), 0.09 + 0.03 * rng.random(),
              0.044, verts=6, smooth=True)
    blob("Shade_Rubble", (0.19, -0.15, 0.02), 0.04, scale=(1.2, 1.0, 0.7), rng=rng, subdiv=1)
    rotate_all(v * 45)


def build_arch(v):
    """障碍石拱门：两根砂岩方柱（深色柱础、柱头）托一道半椭圆拱圈，拱顶一块金色拱心石，柱头上一撮青苔，门前一块坍落的碎石；两档差 90°。"""
    rng = random.Random(2401 + v * 5)
    for x in (-0.27, 0.27):
        box("Shade_Foot", (x, 0, 0.03), (0.20, 0.20, 0.06), bevel=0.014)
        box("Stone_Pillar", (x, 0, 0.26), (0.14, 0.14, 0.40), bevel=0.012)
        box("Stone_Capital", (x, 0, 0.485), (0.19, 0.19, 0.05), bevel=0.012)
    # 拱圈：外椭圆 rx 0.34 / rz 0.17、内椭圆 rx 0.20 / rz 0.09，厚 0.13（沿 y），起拱于 z = 0.51
    n, base, depth = 10, 0.51, 0.13
    verts, faces = [], []
    for i in range(n + 1):
        t = math.pi * i / n
        for rx, rz in ((0.34, 0.17), (0.20, 0.09)):
            for y in (-depth / 2, depth / 2):
                verts.append((rx * math.cos(t), y, base + rz * math.sin(t)))
    # 每个 i 有 4 个点：0 外前、1 外后、2 内前、3 内后
    for i in range(n):
        a, b = 4 * i, 4 * (i + 1)
        faces.append([a + 0, b + 0, b + 1, a + 1])   # 外弧面
        faces.append([a + 2, a + 3, b + 3, b + 2])   # 内弧面
        faces.append([a + 0, a + 2, b + 2, b + 0])   # 正面
        faces.append([a + 1, b + 1, b + 3, a + 3])   # 背面
    faces.append([0, 1, 3, 2])
    faces.append([4 * n, 4 * n + 2, 4 * n + 3, 4 * n + 1])
    mesh_object("Stone_Arch", verts, faces, smooth=False)
    box("Gold_Keystone", (0, 0, 0.645), (0.09, 0.14, 0.09), bevel=0.01)
    blob("Moss_Tuft", (-0.29, 0.02, 0.52), 0.06, scale=(1.2, 1.1, 0.45), rng=rng)
    blob("Shade_Rubble", (0.12, -0.25, 0.03), 0.07, scale=(1.2, 1.0, 0.6), rng=rng, subdiv=1)
    rotate_all(v * 90)


def build_chest(v):
    """障碍宝箱：木箱体 + 半圆拱的浅木箱盖，两道金箍、一枚金锁扣，箱旁三枚金币与两块碎石；整组斜放（两档差 90°）。"""
    rng = random.Random(2501 + v * 3)
    box("Wood_Body", (0, 0, 0.075), (0.30, 0.20, 0.15), bevel=0.012)
    semi = [(0.10 * math.cos(math.pi * i / 8), 0.075 * math.sin(math.pi * i / 8)) for i in range(9)]
    prism("Bloom_Lid", semi, 0.31, (0, 0, 0.15), rot=(0, 0, math.pi / 2))
    for x in (-0.09, 0.09):
        box("Gold_Band", (x, 0, 0.075), (0.032, 0.206, 0.152))
        band = [(0.104 * math.cos(math.pi * i / 8), 0.079 * math.sin(math.pi * i / 8)) for i in range(9)]
        prism("Gold_LidBand", band, 0.032, (x, 0, 0.15), rot=(0, 0, math.pi / 2))
    box("Gold_Lock", (0, -0.108, 0.145), (0.05, 0.02, 0.06), bevel=0.006)
    for i, (x, y) in enumerate(((0.20, -0.08), (0.17, -0.15), (0.22, -0.13))):
        cyl("Gold_Coin", (x, y, 0.006 + 0.008 * (i == 2)), 0.028, 0.009, rot=(0.2 * (i == 2), 0, 0), verts=10, smooth=False)
    blob("Shade_RockA", (-0.20, 0.13, 0.015), 0.045, scale=(1.1, 1.0, 0.6), rng=rng, subdiv=1)
    blob("Shade_RockB", (-0.22, -0.10, 0.012), 0.035, scale=(1.2, 1.0, 0.6), rng=rng, subdiv=1)
    rotate_all(v * 90 + 18)


def build_pebble(v):
    """小石块：格角内侧一颗浅色圆石 + 两颗深色小圆石，贴地（≤ 0.045）。"""
    rng = random.Random(2601 + v * 7)
    c = old_center(f"pebble_{v}")
    for i in range(3):
        a = 2 * math.pi * i / 3 + rng.random() * 1.2
        d = 0.0 if i == 0 else 0.03 + 0.004 * rng.random()
        r = 0.040 if i == 0 else 0.024 + 0.004 * rng.random()
        p = c + Vector((math.cos(a) * d, math.sin(a) * d, r * 0.25))
        sphere("Stone_Pebble" if i == 0 else "Shade_Pebble", tuple(p), r, scale=(1.15, 1.0, 0.62), seg=8, rings=5,
               rot=(0, 0, rng.random() * math.pi))


def build_flower(v):
    """小花：格角内侧三朵五瓣小花（花色按档：粉 / 黄 / 白），金色花心，绿茎；高 ≤ 0.05。"""
    rng = random.Random(2701 + v * 5)
    c = old_center(f"flower_{v}")
    for i in range(3):
        a = 2 * math.pi * i / 3 + rng.random()
        d = 0.026 + 0.006 * rng.random()
        p = c + Vector((math.cos(a) * d, math.sin(a) * d, 0))
        height = 0.030 + 0.010 * rng.random()
        rod("Leaf_Stem", tuple(p), tuple(p + Vector((0, 0, height))), 0.0045, verts=4, caps=False)
        spin = rng.random() * math.pi
        for k in range(5):
            b = spin + 2 * math.pi * k / 5
            q = p + Vector((math.cos(b) * 0.012, math.sin(b) * 0.012, height))
            sphere("Bloom_Petal", tuple(q), 0.0105, scale=(1.15, 1.15, 0.5), seg=6, rings=3)
        sphere("Gold_Heart", tuple(p + Vector((0, 0, height + 0.003))), 0.0075, scale=(1, 1, 0.7), seg=6, rings=3)


def build_stele(v):
    """信物遗址石碑：西北格角内侧一块圆顶砂岩小碑（深色碑座），碑面嵌一枚宝石；稀有档（1）宝石为金、碑顶加一道金冠。"""
    at = Vector((-0.30, 0.30, 0))
    box("Shade_Base", (at.x, at.y, 0.02), (0.18, 0.13, 0.04), bevel=0.008)
    w, h, arch = 0.06, 0.215, 0.06
    # 稀有档：碑身只到圆顶起拱处，圆顶整块换成金冠（与碑身同宽同厚、不重叠）——加宽会让冠角伸进格心 0.36 圈内。
    prism("Stone_Tablet", arch_profile(w, h, arch, steps=8) if v == 0 else [(-w, 0.0), (w, 0.0), (w, h), (-w, h)], 0.06, (at.x, at.y, 0.04))
    gem = "Gold_Gem" if v == 1 else "Crystal_Gem"
    sphere(gem, (at.x, at.y - 0.03, 0.20), 0.022, scale=(1, 0.45, 1.25), seg=6, rings=4)
    if v == 1:
        crown = [(-w, h), (w, h)]
        for i in range(1, 8):
            t = math.pi * i / 8
            crown.append((w * math.cos(t), h + (arch + 0.02) * math.sin(t)))
        prism("Gold_Crown", crown, 0.06, (at.x, at.y, 0.04))


BUILDERS = {
    "rock": build_rock, "ruins": build_ruins, "pines": build_pines, "trees": build_trees, "desert": build_desert,
    "marsh": build_marsh, "crag": build_crag, "shallows": build_shallows, "fence_x": build_fence_x, "fence_z": build_fence_z,
    "bridge": build_bridge, "grass": build_grass, "crystal": build_crystal, "brazier": build_brazier, "arch": build_arch,
    "chest": build_chest, "pebble": build_pebble, "flower": build_flower, "stele": build_stele,
}


# ---------- 核对与导出 ----------

def body_objects():
    return [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Line_")]


def evaluated_world(obj, depsgraph):
    ev = obj.evaluated_get(depsgraph)
    mesh = ev.to_mesh()
    mesh.transform(obj.matrix_world)
    return ev, mesh


def build_outline(thickness):
    """描边外壳：本体零件（flat 薄片除外）沿顶点法线外推 thickness，合成一个物体 Line_Hull（法线朝外，运行时剔除正面）。"""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    verts, faces = [], []
    for obj in body_objects():
        if obj.get(FLAT):
            continue
        ev, mesh = evaluated_world(obj, depsgraph)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        ev.to_mesh_clear()
        bm.normal_update()
        base = len(verts)
        for vert in bm.verts:
            verts.append(tuple(vert.co + vert.normal * thickness))
        for f in bm.faces:
            faces.append([base + vert.index for vert in f.verts])
        bm.free()
    if not faces:
        return None
    mesh = bpy.data.meshes.new("Line_Hull")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("Line_Hull", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def inward_parts(objects):
    """法线朝内的零件（面法线 · (面心 − 零件形心) 按面积加权之和 < 0）：描边外壳靠法线朝外，朝内会整件发黑。"""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bad = []
    for obj in objects:
        ev, mesh = evaluated_world(obj, depsgraph)
        if len(mesh.vertices):
            centroid = sum((vert.co for vert in mesh.vertices), Vector()) / len(mesh.vertices)
            score = sum(poly.area * poly.normal.dot(poly.center - centroid) for poly in mesh.polygons)
            if score <= 0:
                bad.append(obj.name)
        ev.to_mesh_clear()
    return bad


def measure(objects):
    """三角形数、Godot 坐标包围盒、格心圆内最高点。"""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    center_top = -1e9
    for obj in objects:
        ev, mesh = evaluated_world(obj, depsgraph)
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        for vert in mesh.vertices:
            g = (vert.co.x, vert.co.z, -vert.co.y)
            for i in range(3):
                lo[i] = min(lo[i], g[i])
                hi[i] = max(hi[i], g[i])
            if math.hypot(vert.co.x, vert.co.y) < CENTER_CLEAR:
                center_top = max(center_top, vert.co.z)
        ev.to_mesh_clear()
    return total, lo, hi, center_top


def file_stem(kind, v):
    return kind if VARIANTS[kind] == 1 else f"{kind}_{v}"


def bounds_of(kind, v):
    return OLD_BOUNDS.get(file_stem(kind, v)) or OLD_BOUNDS[kind]


def check_variants():
    """档数核对：TerrainParts.cs 里每个装饰 Kind 的档数（字面量或 LowPoly.*Variants）必须等于 VARIANTS。"""
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "godot", "scripts")
    with open(os.path.join(root, "LowPoly.cs"), encoding="utf-8") as f:
        consts = {m.group(1): int(m.group(2)) for m in re.finditer(r"public const int (\w+Variants) = (\d+);", f.read())}
    with open(os.path.join(root, "TerrainParts.cs"), encoding="utf-8") as f:
        source = f.read()
    found = {}
    for m in re.finditer(r'public static readonly Kind \w+ = new\("(\w+)",\s*(?:LowPoly\.(\w+)|(\d+))', source):
        found[m.group(1)] = consts.get(m.group(2)) if m.group(2) else int(m.group(3))
    problems = []
    for kind, n in VARIANTS.items():
        if found.get(kind) != n:
            problems.append(f"{kind}: 脚本 {n} 档，TerrainParts {found.get(kind)} 档")
    if problems:
        print("[chibi-terrain] 档数与 TerrainParts 不一致：" + "；".join(problems))
        sys.exit(1)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else "src/godot/parts/terrain_chibi")
    only = set(argv[1:])
    unknown = only - VARIANTS.keys()
    if unknown:
        print(f"[chibi-terrain] 未知种类 {sorted(unknown)}，可选 {sorted(VARIANTS)}")
        sys.exit(1)
    check_variants()
    missing = [k for k in VARIANTS if k not in BUILDERS]
    if missing and not only:
        print(f"[chibi-terrain] 缺少建模函数：{missing}")
        sys.exit(1)
    os.makedirs(out_dir, exist_ok=True)
    failures = []
    rows = []
    for kind in VARIANTS:
        if only and kind not in only:
            continue
        for v in range(VARIANTS[kind]):
            clear()
            _names.clear()
            BUILDERS[kind](v)
            bpy.context.view_layer.update()
            for obj in body_objects():
                apply_transform(obj)
            stem = file_stem(kind, v)
            bad = [o.name for o in bpy.data.objects if o.name.split("_", 1)[0] not in SLOTS]
            assert not bad, f"{stem}: 物体名缺少材质槽前缀 {bad}"
            body_tris, lo, hi, center_top = measure(body_objects())
            hull = build_outline(OUTLINE[kind])
            hull_tris = measure([hull])[0] if hull else 0
            slots = sorted({o.name.split("_", 1)[0] for o in body_objects()} | ({"Line"} if hull else set()))
            problems = []
            inward = inward_parts(body_objects())
            if inward:
                problems.append(f"法线朝内的零件 {inward}")
            if body_tris > MAX_BODY:
                problems.append(f"本体 {body_tris} > {MAX_BODY}")
            if hull_tris > body_tris:
                problems.append(f"描边 {hull_tris} > 本体 {body_tris}")
            if len(slots) > MAX_SLOTS:
                problems.append(f"材质槽 {len(slots)} > {MAX_SLOTS}")
            (blo, bhi) = bounds_of(kind, v)
            for i, axis in enumerate("xyz"):
                if lo[i] < blo[i] - BOUNDS_TOL or hi[i] > bhi[i] + BOUNDS_TOL:
                    problems.append(f"{axis} [{lo[i]:.3f}, {hi[i]:.3f}] 超出旧包围盒 [{blo[i]:.3f}, {bhi[i]:.3f}]")
            center_max = CENTER_MAX_Z_OF.get(kind, CENTER_MAX_Z)
            if kind in PLAYABLE and center_top > center_max:
                problems.append(f"格心半径 {CENTER_CLEAR} 内最高 {center_top:.3f} > {center_max}")
            if problems:
                failures.append(f"{stem}: {'；'.join(problems)}")
                print(f"[chibi-terrain] {stem}: 不导出——{'；'.join(problems)}")
                continue
            path = os.path.join(out_dir, f"{stem}.glb")
            bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_apply=True,
                                      export_yup=True, export_materials="NONE")
            rows.append((stem, len(body_objects()), body_tris, hull_tris, len(slots), slots))
            print(f"[chibi-terrain] {stem}: {len(body_objects())} 个零件，本体 {body_tris} + 描边 {hull_tris} 三角形，"
                  f"材质槽 {len(slots)} 个 {slots}，包围盒 ({lo[0]:.3f},{lo[1]:.3f},{lo[2]:.3f})–({hi[0]:.3f},{hi[1]:.3f},{hi[2]:.3f})"
                  f"{f'，格心圈内最高 {center_top:.3f}' if kind in PLAYABLE else ''} → {path}")
    if failures:
        print("[chibi-terrain] 未通过：" + " | ".join(failures))
        sys.exit(1)
    print(f"[chibi-terrain] 导出 {len(rows)} 件，本体最多 {max(r[2] for r in rows)} 三角形，槽最多 {max(r[4] for r in rows)} 个")


try:
    main()
except SystemExit:
    raise
except Exception:  # Blender 的 -b --python 遇到未捕获异常仍以 0 退出：这里统一转成退出码 1
    import traceback
    traceback.print_exc()
    sys.exit(1)
