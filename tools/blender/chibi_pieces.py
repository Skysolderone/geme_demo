"""卡通棋子（v2）：在 Blender 里用代码生成十种棋子（5 个闭盔角色 + 5 个建筑）并导出 glb。

运行（无界面，全部十种）：
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python tools/blender/chibi_pieces.py -- src/godot/parts/pieces_chibi
只生成其中几种：在输出目录后面追加键名，如 `... -- src/godot/parts/pieces_chibi basic chain`。
生成后必须在 Godot 里重新导入：Godot --headless --path src/godot --import

约定（与 PieceModels.MergeScene 一致）：
- 物体名 = "<材质槽>_<部件名>"，槽只能是 Stone / Shade / Metal / Enamel / Cloth / Crystal / Ink / Line；
  运行时按槽合并并重新上色（卡通分阶材质），glb 里的材质不使用。
  Enamel = 阵营色，Stone = 石料 / 皮肤，Metal = 蓝灰钢甲，Shade = 深色（面甲缝、靴、铁器），Cloth = 木料 / 皮革，
  Crystal = 晶体（运行时取阵营色提亮），Line = 描边外壳（运行时 cull front、无光照、深色）。
- Line_Hull 由脚本把全部本体零件沿顶点法线外推 OUTLINE 得到，法线不翻转（运行时剔除正面，只画背面一圈）。
- 空物体 Anchor_Emblem 标出徽记的摆放处：局部 +Z = 徽记正面法线，局部 +Y = 图案上方，缩放 = 徽记边长。
- 不建父子层级，导出时应用修改器与变换。
- Blender 坐标：x 右、-y 朝前（导出后为 Godot +Z，即镜头一侧）、z 上；单位与棋盘格一致。
- 脚下的阵营色细圆环（外半径 0.36、环宽 0.05、高 0.02）由 PieceModels 追加；角色与建筑直接从格面 z = G = 0 站起
  （环在脚外一圈，不垫脚），占地半宽 ≤ 0.42。
- 对局相机俯角 60°：辨认靠俯视剪影（冠饰、道具、屋顶）与阵营色块。
- 每颗本体（含运行时圆环 RUNTIME_RING_TRIS）≤ MAX_BODY 个三角形、本体 + 描边 ≤ MAX_TOTAL、材质槽（含 Line）≤ MAX_SLOTS，超过即报错退出（退出码 1）。
"""
import math
import os
import re
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

G = 0.0  # 站立面 = 格面（v2 第二轮起脚下不再垫方块，改为脚外一圈细圆环）
SLOTS = {"Stone", "Shade", "Metal", "Enamel", "Cloth", "Crystal", "Ink", "Line"}
MAX_BODY = 1200
RUNTIME_RING_TRIS = 96  # PieceModels 运行时并进 Enamel 槽的脚下圆环（24 段 × 顶面 + 外壁），计入本体预算
MAX_TOTAL = 2400
MAX_SLOTS = 7
MAX_HALF_WIDTH = 0.42
OUTLINE = 0.03  # 描边外壳厚度（沿顶点法线外推）
ANCHOR = "Anchor_Emblem"


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def finish(name, smooth=True, bevel=0.0):
    slot = name.split("_", 1)[0]
    assert slot in SLOTS and slot != "Line", name
    assert name not in bpy.data.objects, f"重名零件 {name}"
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 1
    if smooth:
        obj.data.shade_smooth()
    else:
        obj.data.shade_flat()
    return obj


# ---------- 基础图元（段数按每颗 ≤ 1200 三角形压低） ----------

def sphere(name, loc, radius, scale=(1, 1, 1), seg=10, rings=6, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=radius, location=loc, rotation=rot)
    bpy.context.active_object.scale = scale
    return finish(name)


def cyl(name, loc, radius, depth, rot=(0, 0, 0), verts=12, smooth=True, bevel=0.0, caps=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot,
                                        end_fill_type="NGON" if caps else "NOTHING")
    return finish(name, smooth, bevel)


def cone(name, loc, r1, r2, depth, rot=(0, 0, 0), verts=12, smooth=True, caps=True):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=rot,
                                    end_fill_type="NGON" if caps else "NOTHING")
    return finish(name, smooth)


def box(name, loc, size, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    bpy.context.active_object.scale = size
    return finish(name, smooth=False, bevel=bevel)


def frame_rotation(axis_z, axis_y):
    """局部 z 轴对齐 axis_z、局部 y 轴尽量对齐 axis_y 的欧拉角。"""
    z = Vector(axis_z).normalized()
    y = Vector(axis_y)
    y = (y - z * y.dot(z)).normalized()
    x = y.cross(z)
    return Matrix((x, y, z)).transposed().to_euler()


def torus(name, loc, major, minor, rot=(0, 0, 0), major_seg=12, minor_seg=4, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=major_seg,
                                     minor_segments=minor_seg, location=loc, rotation=rot)
    bpy.context.active_object.scale = scale
    return finish(name)


def rod(name, p0, p1, radius, verts=6, caps=True, smooth=True):
    """两点之间的圆杆（手臂、矛杆、锤柄、旗杆）。"""
    d = Vector(p1) - Vector(p0)
    mid = (Vector(p0) + Vector(p1)) / 2
    tilt = math.acos(max(-1.0, min(1.0, d.z / d.length)))
    heading = math.atan2(d.y, d.x)
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=d.length, location=mid,
                                        rotation=(0, tilt, heading),
                                        end_fill_type="NGON" if caps else "NOTHING")
    return finish(name, smooth)


def aligned_box(name, loc, size, axis, bevel=0.0):
    """局部 z 轴对齐到 axis 的方块（剑刃、锤头这类斜置零件）。"""
    ax = Vector(axis).normalized()
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=(0, math.acos(ax.z), math.atan2(ax.y, ax.x)))
    bpy.context.active_object.scale = size
    return finish(name, smooth=False, bevel=bevel)


def shell(name, loc, radius, cut_z, seg=14, rings=8, scale=(1, 1, 1)):
    """球壳：删掉局部 z < cut_z 的顶点，得到开口半球（敞口盔）。"""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=radius, location=loc)
    obj = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < cut_z], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.scale = scale
    return finish(name)


def prism(name, profile, thickness, loc, rot=(0, 0, 0)):
    """把 xz 平面上的凸轮廓沿 y 挤出成厚板（盾、旗、碑、门、屋顶）。轮廓坐标相对 loc，正面朝 -y。"""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    front = [bm.verts.new((x, -thickness / 2, z)) for x, z in profile]
    back = [bm.verts.new((x, thickness / 2, z)) for x, z in profile]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[j], front[i], back[i], back[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("tmp", mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = rot
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    return finish(name, smooth=False)


def arch_profile(half_w, rect_h, arch_h, steps=8):
    """矩形下半 + 半椭圆顶的凸轮廓（门、碑）。"""
    pts = [(-half_w, 0.0), (half_w, 0.0), (half_w, rect_h)]
    for i in range(1, steps):
        t = math.pi * i / steps
        pts.append((half_w * math.cos(t), rect_h + arch_h * math.sin(t)))
    pts.append((-half_w, rect_h))
    return pts


def local_point(rot, p, origin):
    return Vector(origin) + Euler(rot).to_matrix() @ Vector(p)


def emblem_anchor(loc, normal, up, size, lift=0.006):
    """徽记锚点：局部 +Z = 法线（徽记正面朝向），局部 +Y = 图案上方，缩放 = 徽记边长。"""
    assert ANCHOR not in bpy.data.objects, "每颗棋子只有一个徽记锚点"
    obj = bpy.data.objects.new(ANCHOR, None)
    bpy.context.collection.objects.link(obj)
    n = Vector(normal).normalized()
    obj.location = Vector(loc) + n * lift
    obj.rotation_euler = frame_rotation(n, up)
    obj.scale = (size, size, size)
    return obj


def prism_anchor(loc, rot, thickness, center, size):
    """挂在 prism 正面（-y 面）上的徽记：center 是轮廓坐标 (x, z)。"""
    m = Euler(rot).to_matrix()
    p = local_point(rot, (center[0], -thickness / 2, center[1]), loc)
    emblem_anchor(p, m @ Vector((0, -1, 0)), m @ Vector((0, 0, 1)), size)


# ---------- 共用的闭盔骑士：大圆盔（T 字面甲）占身高近一半，短身，阵营色羽缨与罩袍 ----------

HEAD_R = 0.165
HEAD_Z = G + 0.40
HEAD_TOP = HEAD_Z + HEAD_R
SHOULDER = (0.12, 0.0, G + 0.255)


def knight(plume=True, tabard="Enamel"):
    for side, tag in ((-1, "L"), (1, "R")):
        sphere(f"Shade_Boot{tag}", (side * 0.065, -0.02, G + 0.035), 0.056, scale=(1, 1.3, 0.75), seg=6, rings=4)
    # 罩袍：下宽上窄的圆台（阵营色），腰带一圈深色
    cone(f"{tabard}_Tabard", (0, 0, G + 0.16), 0.15, 0.10, 0.22, verts=12)
    cyl("Shade_Belt", (0, 0, G + 0.12), 0.14, 0.03, verts=12, caps=False)
    for side, tag in ((-1, "L"), (1, "R")):
        sphere(f"Metal_Pauldron{tag}", (side * SHOULDER[0], 0, SHOULDER[2]), 0.066, scale=(1, 1, 0.8), seg=6, rings=4)
    # 闭盔：大圆球 + 正面 T 字形面甲缝（深色）
    sphere("Metal_Helmet", (0, 0, HEAD_Z), HEAD_R, seg=14, rings=8)
    # 面甲缝贴在盔面上偏上的位置（横缝仰角 26°、竖缝 6°），60° 俯视下能看到 T 字
    visor_bar("Shade_VisorH", 26, (0.15, 0.036))
    visor_bar("Shade_VisorV", 6, (0.038, 0.11))
    if plume:
        # 羽缨：盔顶一道前后向的阵营色冠，尾端向后下方卷
        cyl("Metal_PlumeBase", (0, -0.01, HEAD_TOP - 0.005), 0.03, 0.03, verts=6)
        sphere("Enamel_Plume", (0, 0.02, HEAD_TOP + 0.04), 0.078, scale=(0.6, 1.45, 0.95), seg=10, rings=6)
        sphere("Enamel_PlumeTail", (0, 0.16, HEAD_TOP - 0.03), 0.06, scale=(0.55, 1.1, 1.15), seg=8, rings=5)


def visor_bar(name, elevation_deg, size, depth=0.07, rise=0.02):
    """贴在盔球正面、仰角 elevation_deg 处的面甲缝：size = (宽, 高)，凸出盔面 rise。"""
    e = math.radians(elevation_deg)
    n = Vector((0, -math.cos(e), math.sin(e)))
    up = Vector((0, math.sin(e), math.cos(e)))
    center = Vector((0, 0, HEAD_Z)) + n * (HEAD_R + rise - depth / 2)
    bpy.ops.mesh.primitive_cube_add(size=1, location=center, rotation=frame_rotation(n, up))
    bpy.context.active_object.scale = (size[0], size[1], depth)
    return finish(name, smooth=False)


def arm(side, hand_loc):
    tag = "L" if side < 0 else "R"
    rod(f"Metal_Arm{tag}", (side * SHOULDER[0], 0, SHOULDER[2] - 0.01), hand_loc, 0.03, caps=False)
    sphere(f"Metal_Hand{tag}", hand_loc, 0.04, seg=6, rings=4)


# ---------- 普通子（Rounded）：剑士——羽缨闭盔、左手圆盾、右手短剑 ----------

def build_basic():
    knight()
    # 圆盾：压低放在左前方，免得被大圆盔挡住；盾面法线偏离竖直 34°（镜头方向偏离竖直 30°），正对镜头，阵营色盾面 + 银色包边
    loc = (-0.19, -0.17, G + 0.17)
    rot = (math.radians(34), 0, math.radians(-15))
    # 盾半径 0.145、包边内沿 0.129；徽记外接圆半径 0.61 × 边长 ≤ 内沿 → 边长 0.21
    cyl("Enamel_Shield", loc, 0.145, 0.03, rot=rot, verts=14, smooth=False)
    torus("Metal_ShieldRim", loc, 0.145, 0.016, rot=rot, major_seg=14, minor_seg=4)
    m = Euler(rot).to_matrix()
    n = m @ Vector((0, 0, 1))
    emblem_anchor(Vector(loc) + n * 0.015, n, m @ Vector((0, 1, 0)), 0.21)
    arm(-1, (-0.15, -0.10, G + 0.18))
    # 短剑：右手斜举
    hand = Vector((0.17, -0.08, G + 0.17))
    arm(1, tuple(hand))
    axis = Vector((0.22, -0.12, 1.0)).normalized()
    aligned_box("Metal_Blade", hand + axis * 0.16, (0.045, 0.016, 0.24), axis)
    aligned_box("Shade_Guard", hand + axis * 0.035, (0.11, 0.03, 0.026), axis)
    sphere("Shade_Pommel", hand - axis * 0.05, 0.022, seg=6, rings=4)


# ---------- 哨兵子（Crossed）：长矛兵——身后两根长矛交叉成 X，胸前小盾 ----------

def build_sentry():
    knight()
    # 两根长矛交叉在头顶后上方，俯视是一个 X；矛杆木色、矛尖银色、矛缨阵营色
    cross = Vector((0, 0.13, G + 0.64))
    lean = math.radians(31)
    for i, side in enumerate((1, -1)):
        d = Vector((side * math.sin(lean), 0.0, math.cos(lean)))
        lo, hi = cross - d * 0.64, cross + d * 0.38
        rod(f"Cloth_Spear{i}", tuple(lo), tuple(hi), 0.017, verts=6)
        tip = hi + d * 0.065
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.045, radius2=0, depth=0.14, location=tip,
                                        rotation=(0, math.acos(d.z), math.atan2(d.y, d.x)))
        finish(f"Metal_SpearTip{i}", smooth=False)
        cyl(f"Enamel_Tassel{i}", tuple(hi - d * 0.03), 0.032, 0.05,
            rot=(0, math.acos(d.z), math.atan2(d.y, d.x)), verts=6)
    sphere("Enamel_Binding", tuple(cross), 0.036, seg=6, rings=4)
    # 胸前小盾（鸢形），上沿后仰 40°
    profile = [(-0.10, 0.065), (0.10, 0.065), (0.10, -0.05), (0.0, -0.13), (-0.10, -0.05)]
    loc = (0, -0.15, G + 0.18)
    rot = (math.radians(-40), 0, 0)
    prism("Enamel_ChestShield", profile, 0.03, loc, rot)
    prism_anchor(loc, rot, 0.03, (0, -0.01), 0.15)
    arm(-1, (-0.15, -0.07, G + 0.19))
    arm(1, (0.19, 0.02, G + 0.25))


# ---------- 旗手子（Banner）：左侧竖直旗杆，杆顶向右展开一面大旗 ----------

def build_bannerman():
    knight()
    px, py = -0.21, -0.02
    top = G + 1.05
    cyl("Cloth_Pole", (px, py, (G + top) / 2), 0.017, top - G, verts=6)
    sphere("Metal_Finial", (px, py, top + 0.025), 0.032, seg=6, rings=4)
    # 旗面：从旗杆向右展开、尖尾，上沿后仰 25°（俯视下旗面多露一些），超出头顶羽缨
    flag = [(0.0, 0.0), (0.0, -0.30), (0.34, -0.30), (0.42, -0.15), (0.34, 0.0)]
    loc = (px + 0.012, py, top - 0.03)
    rot = (math.radians(-25), 0, 0)
    prism("Enamel_Flag", flag, 0.024, loc, rot)
    prism_anchor(loc, rot, 0.024, (0.17, -0.15), 0.26)
    arm(-1, (px + 0.02, -0.06, G + 0.25))
    arm(1, (0.15, -0.07, G + 0.18))


# ---------- 匠人（Tooling）：敞口盔、阵营色皮围裙、肩扛斜向大锤 ----------

def build_artisan():
    knight(plume=False, tabard="Cloth")
    # 去掉闭盔，换成露脸的头 + 阵营色敞口盔
    for name in ("Metal_Helmet", "Shade_VisorH", "Shade_VisorV"):
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    sphere("Stone_Face", (0, 0, HEAD_Z - 0.01), HEAD_R * 0.96, seg=14, rings=8)
    for side, tag in ((-1, "L"), (1, "R")):
        sphere(f"Shade_Eye{tag}", (side * 0.055, -0.145, HEAD_Z - 0.005), 0.022, scale=(0.8, 0.5, 1.2), seg=6, rings=4)
    shell("Enamel_Cap", (0, 0, HEAD_Z + 0.005), HEAD_R * 1.04, 0.035, seg=14, rings=8)
    cyl("Metal_CapBrim", (0, -0.02, HEAD_Z + 0.045), 0.20, 0.018, verts=14, smooth=False)
    # 围裙：身前一块阵营色梯形板，徽记在围裙上
    apron = [(-0.115, -0.11), (0.115, -0.11), (0.09, 0.07), (-0.09, 0.07)]
    loc = (0, -0.145, G + 0.16)
    rot = (math.radians(-14), 0, 0)
    prism("Enamel_Apron", apron, 0.02, loc, rot)
    prism_anchor(loc, rot, 0.02, (0, -0.02), 0.16)
    # 大锤：右手在身前握柄，柄斜压左肩，深色锤头在左肩后上方，锤头与锤柄垂直
    grip = Vector((0.15, -0.12, G + 0.18))
    head_c = Vector((-0.25, 0.12, HEAD_Z + 0.10))
    rod("Cloth_HammerHandle", tuple(grip), tuple(head_c), 0.022)
    arm(1, tuple(grip))
    arm(-1, (-0.13, -0.03, SHOULDER[2] + 0.07))
    d = head_c - grip
    # 锤头长轴：与锤柄垂直，且在 60° 俯视画面里也与锤柄的投影垂直（见 v1 推导）
    sx, sy = d.x, 0.866 * d.y + 0.5 * d.z
    k = -sx / sy
    det = 0.866 * d.z - 0.5 * d.y
    b_ = (k * d.z + 0.5 * d.x) / det
    c_ = (-d.x - d.y * b_) / d.z
    up = Vector((1.0, b_, c_)).normalized()
    aligned_box("Shade_HammerHead", tuple(head_c), (0.14, 0.14, 0.30), up)
    for tag, s in (("A", 1), ("B", -1)):
        aligned_box(f"Metal_HammerBand{tag}", tuple(head_c + up * (s * 0.10)), (0.152, 0.152, 0.04), up)


# ---------- 铁链子（Interlock）：链枷兵——左手鸢形盾，右手链枷（相扣中空环 + 铁球） ----------

def build_chain():
    knight()
    profile = [(-0.11, 0.10), (0.11, 0.10), (0.11, -0.04), (0.0, -0.17), (-0.11, -0.04)]
    # 盾面后仰 50°，60° 俯视下接近正对镜头
    loc = (-0.17, -0.12, G + 0.23)
    rot = (math.radians(-50), 0, math.radians(-25))
    prism("Enamel_KiteShield", profile, 0.03, loc, rot)
    prism_anchor(loc, rot, 0.03, (0, 0.005), 0.19)
    arm(-1, (-0.13, -0.06, G + 0.21))
    # 链枷：短柄斜向右上，链条三节相扣（相邻两节互相垂直），末端深色铁球
    hand = Vector((0.17, -0.07, G + 0.23))
    arm(1, tuple(hand))
    tip = hand + Vector((0.04, -0.01, 0.13))
    rod("Cloth_FlailHandle", tuple(hand - Vector((0, 0, 0.04))), tuple(tip), 0.02)
    u = Vector((0.22, -0.10, 1.0)).normalized()
    a = u.cross(Vector((0, 1, 0))).normalized()   # 第一节的环面法线（环面正对镜头）
    major, minor, stretch, pitch = 0.052, 0.015, 1.4, 0.098
    for i in range(3):
        c = tip + u * (0.05 + i * pitch)
        axis = a if i % 2 == 0 else u.cross(a)
        # 环默认在局部 xy 平面、轴为局部 z；先沿局部 y 拉长再把局部 y 对齐链条方向
        bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=10,
                                         minor_segments=4, location=c, rotation=frame_rotation(axis, u))
        bpy.context.active_object.scale = (1, stretch, 1)
        finish(f"Shade_Link{i}")
    ball = tip + u * (0.05 + 2 * pitch + major * stretch + 0.075)
    sphere("Shade_Ball", tuple(ball), 0.07, seg=8, rings=6)
    for i, dv in enumerate(((1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, 1))):
        dv = Vector(dv)
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.025, radius2=0, depth=0.06,
                                        location=ball + dv * 0.078,
                                        rotation=frame_rotation(dv, Vector((0, 1, 0)) if dv.z else Vector((0, 0, 1))))
        finish(f"Shade_Spike{i}", smooth=False)


# ---------- 堡垒子（TowerMass）：圆石塔——阵营色垛口顶、木门、塔身挂旗 ----------

def build_fortress():
    cyl("Shade_Footing", (0, 0, G + 0.03), 0.31, 0.06, verts=16, smooth=False)
    cyl("Stone_Tower", (0, 0, G + 0.31), 0.27, 0.50, verts=18)
    cone("Enamel_Corbel", (0, 0, G + 0.59), 0.27, 0.325, 0.06, verts=18)
    cyl("Enamel_Parapet", (0, 0, G + 0.655), 0.325, 0.07, verts=18)
    cyl("Cloth_Floor", (0, 0, G + 0.695), 0.27, 0.02, verts=12, smooth=False)
    for i in range(8):
        ang = math.radians(22.5 + 45 * i)
        box(f"Enamel_Merlon{i}", (math.cos(ang) * 0.285, math.sin(ang) * 0.285, G + 0.725),
            (0.075, 0.115, 0.08), rot=(0, 0, ang))
    # 拱形木门 + 两道窗缝
    prism("Cloth_Door", arch_profile(0.075, 0.12, 0.06), 0.05, (0, -0.255, G + 0.06))
    for side, tag in ((-1, "L"), (1, "R")):
        ang = math.radians(-90 + side * 42)
        box(f"Shade_Slit{tag}", (math.cos(ang) * 0.262, math.sin(ang) * 0.262, G + 0.43), (0.032, 0.04, 0.09),
            rot=(0, 0, ang + math.pi / 2))
    # 塔身正面挂旗（尖尾），挂杆银色
    banner = [(-0.10, 0.0), (0.10, 0.0), (0.10, -0.17), (0.0, -0.23), (-0.10, -0.17)]
    loc = (0, -0.283, G + 0.55)
    rot = (math.radians(-6), 0, 0)
    prism("Enamel_Banner", banner, 0.016, loc, rot)
    rod("Metal_BannerRod", (-0.125, -0.285, G + 0.555), (0.125, -0.285, G + 0.555), 0.011)
    prism_anchor(loc, rot, 0.016, (0, -0.09), 0.17)


# ---------- 界碑子（Slab）：矮宽圆顶石碑，阵营色碑额，碑面阵营色石板 + 徽记 ----------

def build_boundary():
    box("Shade_Base", (0, 0.0, G + 0.035), (0.72, 0.26, 0.07), bevel=0.012)
    w, h, arch = 0.30, 0.19, 0.16
    loc = (0, 0.03, G + 0.07)
    rot = (math.radians(-28), 0, 0)
    prism("Stone_Stele", arch_profile(w, h, arch, steps=10), 0.12, loc, rot)
    # 碑额：碑顶圆弧部分外包一层阵营色（比碑身略厚、略宽）
    crown = [(-w - 0.012, h - 0.01), (w + 0.012, h - 0.01)]
    for i in range(1, 10):
        t = math.pi * i / 10
        crown.append(((w + 0.012) * math.cos(t), h - 0.01 + (arch + 0.014) * math.sin(t)))
    prism("Enamel_Crown", crown, 0.14, loc, rot)
    # 碑面石板（阵营色）与徽记
    # 碑额从 h - 0.01 起包住圆弧，石板只能占碑额以下（0.015–0.185）
    panel_c = local_point(rot, (0, -0.064, 0.10), loc)
    box("Enamel_Panel", tuple(panel_c), (0.36, 0.012, 0.17), rot=rot)
    m = Euler(rot).to_matrix()
    emblem_anchor(panel_c + m @ Vector((0, -0.006, 0)), m @ Vector((0, -1, 0)), m @ Vector((0, 0, 1)), 0.16)


# ---------- 连珠子（Connection）：两座小塔用一段城墙相连，墙面挂旗 ----------

def build_line():
    for side, tag in ((-1, "L"), (1, "R")):
        x = side * 0.26
        cyl(f"Stone_Tower{tag}", (x, 0, G + 0.22), 0.12, 0.44, verts=12)
        cone(f"Enamel_Roof{tag}", (x, 0, G + 0.44 + 0.11), 0.15, 0.0, 0.22, verts=12, smooth=False)
        box(f"Shade_Window{tag}", (x, -0.115, G + 0.32), (0.035, 0.03, 0.08))
    box("Stone_Wall", (0, 0, G + 0.14), (0.34, 0.13, 0.28), bevel=0.006)
    box("Enamel_WallCap", (0, 0, G + 0.29), (0.34, 0.145, 0.025))
    for i, x in enumerate((-0.105, 0.0, 0.105)):
        box(f"Enamel_Merlon{i}", (x, 0, G + 0.33), (0.065, 0.13, 0.06))
    banner = [(-0.10, 0.0), (0.10, 0.0), (0.10, -0.15), (0.0, -0.20), (-0.10, -0.15)]
    loc = (0, -0.075, G + 0.265)
    rot = (math.radians(-8), 0, 0)
    prism("Enamel_Banner", banner, 0.014, loc, rot)
    prism_anchor(loc, rot, 0.014, (0, -0.08), 0.16)


# ---------- 倍增子（Radial）：修道院——尖顶钟楼，顶上一枚四向放射的风向标 ----------

def build_multiplier():
    box("Stone_Nave", (0, 0.08, G + 0.13), (0.40, 0.46, 0.26), bevel=0.006)
    prism("Enamel_Roof", [(-0.24, 0.0), (0.24, 0.0), (0.0, 0.20)], 0.50, (0, 0.08, G + 0.26))
    box("Stone_Belfry", (0, -0.17, G + 0.23), (0.21, 0.21, 0.46), bevel=0.006)
    box("Shade_BellOpening", (0, -0.17, G + 0.38), (0.085, 0.22, 0.10))
    box("Shade_BellOpeningX", (0, -0.17, G + 0.38), (0.22, 0.085, 0.10))
    cone("Enamel_Spire", (0, -0.17, G + 0.46 + 0.17), 0.17, 0.0, 0.34, rot=(0, 0, math.radians(45)), verts=4,
         smooth=False)
    # 风向标：竖杆 + 四向放射的尖臂（俯视是一颗四角星）
    vane_z = G + 0.92
    rod("Metal_VanePole", (0, -0.17, G + 0.76), (0, -0.17, vane_z + 0.03), 0.011)
    sphere("Metal_VaneHub", (0, -0.17, vane_z), 0.03, seg=6, rings=4)
    for i in range(4):
        ang = math.radians(90 * i)
        dv = Vector((math.cos(ang), math.sin(ang), 0))
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.03, radius2=0, depth=0.13,
                                        location=Vector((0, -0.17, vane_z)) + dv * 0.09,
                                        rotation=frame_rotation(dv, Vector((0, 0, 1))))
        finish(f"Metal_VaneArm{i}", smooth=False)
    # 拱门 + 门楣挂牌（阵营色）+ 徽记
    prism("Cloth_Door", arch_profile(0.055, 0.10, 0.045), 0.03, (0, -0.272, G + 0.0))
    # 门楣挂牌后仰 40°：钟楼正面在 60° 俯视下被压扁，挂牌要多朝上才露得出徽记
    loc = (0, -0.29, G + 0.245)
    rot = (math.radians(-40), 0, 0)
    prism("Enamel_Lintel", [(-0.09, -0.075), (0.09, -0.075), (0.09, 0.075), (-0.09, 0.075)], 0.016, loc, rot)
    prism_anchor(loc, rot, 0.016, (0, 0), 0.15)


# ---------- 协同子（MultiNode）：水晶祭坛——八角石台上长出多簇晶体 ----------

def crystal(name, base, direction_deg, lean_deg, length, radius):
    """六棱柱 + 尖顶，从 base 出发朝方位 direction_deg、偏离竖直 lean_deg 生长。"""
    d, l = math.radians(direction_deg), math.radians(lean_deg)
    u = Vector((math.sin(l) * math.cos(d), math.sin(l) * math.sin(d), math.cos(l)))
    body_end = Vector(base) + u * length * 0.7
    tip = Vector(base) + u * length
    rod(f"Crystal_{name}", base, tuple(body_end), radius, verts=6, smooth=False)
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=radius, radius2=0, depth=length * 0.3,
                                    location=(body_end + tip) / 2, rotation=(0, math.acos(u.z), math.atan2(u.y, u.x)))
    finish(f"Crystal_{name}Tip", smooth=False)


def build_synergy():
    oct_rot = (0, 0, math.radians(22.5))
    cyl("Stone_Plinth", (0, 0, G + 0.04), 0.35, 0.08, rot=oct_rot, verts=8, smooth=False)
    cyl("Enamel_Trim", (0, 0, G + 0.095), 0.335, 0.03, rot=oct_rot, verts=8, smooth=False)
    cyl("Stone_Altar", (0, 0.02, G + 0.15), 0.29, 0.08, rot=oct_rot, verts=8, smooth=False)
    top = G + 0.19
    # 台面前沿一块阵营色石板，徽记朝上
    box("Enamel_Plaque", (0, -0.175, top + 0.005), (0.22, 0.13, 0.012))
    emblem_anchor((0, -0.175, top + 0.011), (0, 0, 1), (0, 1, 0), 0.12)
    crystal("Center", (0, 0.07, top - 0.02), 90, 4, 0.46, 0.068)
    crystal("CenterB", (0.05, 0.05, top - 0.02), 20, 22, 0.30, 0.045)
    crystal("Left", (-0.16, 0.06, top - 0.02), 160, 26, 0.33, 0.052)
    crystal("LeftB", (-0.12, -0.04, top - 0.02), 210, 34, 0.20, 0.036)
    crystal("Right", (0.17, 0.08, top - 0.02), 20, 28, 0.30, 0.050)
    crystal("RightB", (0.13, -0.05, top - 0.02), 320, 36, 0.19, 0.034)
    crystal("Back", (-0.02, 0.20, top - 0.02), 100, 30, 0.26, 0.044)


PIECES = {
    "basic": build_basic,
    "fortress": build_fortress,
    "line": build_line,
    "multiplier": build_multiplier,
    "synergy": build_synergy,
    "artisan": build_artisan,
    "bannerman": build_bannerman,
    "chain": build_chain,
    "sentry": build_sentry,
    "boundary": build_boundary,
}


def body_objects():
    return [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Line_")]


def evaluated_world(obj, depsgraph):
    """应用修改器与物体变换后的网格（调用方负责 to_mesh_clear）。"""
    ev = obj.evaluated_get(depsgraph)
    mesh = ev.to_mesh()
    mesh.transform(obj.matrix_world)
    return ev, mesh


def build_outline():
    """描边外壳：全部本体零件沿顶点法线外推 OUTLINE，合成一个物体 Line_Hull（法线保持朝外，运行时剔除正面）。"""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    verts, faces = [], []
    for obj in body_objects():
        ev, mesh = evaluated_world(obj, depsgraph)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        ev.to_mesh_clear()
        bm.normal_update()
        base = len(verts)
        for v in bm.verts:
            verts.append(tuple(v.co + v.normal * OUTLINE))
        for f in bm.faces:
            faces.append([base + v.index for v in f.verts])
        bm.free()
    mesh = bpy.data.meshes.new("Line_Hull")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("Line_Hull", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def triangles_and_extent(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total, half_w, low = 0, 0.0, 1e9
    for obj in objects:
        ev, mesh = evaluated_world(obj, depsgraph)
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        for v in mesh.vertices:
            half_w = max(half_w, abs(v.co.x), abs(v.co.y))
            low = min(low, v.co.z)
        ev.to_mesh_clear()
    return total, half_w, low


def check_ring_budget():
    """RUNTIME_RING_TRIS 与 PieceModels.FootRingSegments 手工同步（每段顶面 + 外壁 = 4 个三角形）：对不上就不导出。"""
    source = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "godot", "scripts", "PieceModels.cs")
    with open(source, encoding="utf-8") as f:
        match = re.search(r"const int FootRingSegments = (\d+);", f.read())
    if match is None:
        print(f"[chibi] {source} 里找不到 FootRingSegments，无法核对 RUNTIME_RING_TRIS")
        sys.exit(1)
    segments = int(match.group(1))
    if segments * 4 != RUNTIME_RING_TRIS:
        print(f"[chibi] PieceModels.FootRingSegments = {segments}（{segments * 4} 个三角形）与 RUNTIME_RING_TRIS = {RUNTIME_RING_TRIS} 不一致")
        sys.exit(1)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else "src/godot/parts/pieces_chibi")
    only = set(argv[1:])
    unknown = only - PIECES.keys()
    if unknown:
        print(f"[chibi] 未知键名 {sorted(unknown)}，可选 {sorted(PIECES)}")
        sys.exit(1)
    check_ring_budget()
    os.makedirs(out_dir, exist_ok=True)
    failures = []
    for key, build in PIECES.items():
        if only and key not in only:
            continue
        clear()
        build()
        bad = [o.name for o in bpy.data.objects if o.name != ANCHOR and o.name.split("_", 1)[0] not in SLOTS]
        assert not bad, f"{key}: 物体名缺少材质槽前缀 {bad}"
        anchors = [o for o in bpy.data.objects if o.name == ANCHOR]
        body_tris, half_w, low = triangles_and_extent(body_objects())
        hull = build_outline()
        hull_tris, _, _ = triangles_and_extent([hull])
        # 运行时总会有 Enamel 槽（脚下圆环并在里面），glb 里没有 Enamel 零件也照算一槽。
        slots = sorted({o.name.split("_", 1)[0] for o in bpy.data.objects if o.type == "MESH"} | {"Enamel"})
        problems = []
        if body_tris + RUNTIME_RING_TRIS > MAX_BODY:
            problems.append(f"本体 {body_tris} + 圆环 {RUNTIME_RING_TRIS} > {MAX_BODY}")
        if body_tris + RUNTIME_RING_TRIS + hull_tris > MAX_TOTAL:
            problems.append(f"合计 {body_tris + RUNTIME_RING_TRIS + hull_tris} > {MAX_TOTAL}")
        if len(slots) > MAX_SLOTS:
            problems.append(f"材质槽 {len(slots)} > {MAX_SLOTS}")
        if half_w > MAX_HALF_WIDTH:
            problems.append(f"占地半宽 {half_w:.3f} > {MAX_HALF_WIDTH}")
        if len(anchors) != 1:
            problems.append(f"徽记锚点 {len(anchors)} 个")
        if low < G - 0.035:
            problems.append(f"最低点 {low:.3f} 低于格面")
        if problems:
            failures.append(f"{key}: {'；'.join(problems)}")
            print(f"[chibi] {key}: 不导出——{'；'.join(problems)}")
            continue
        path = os.path.join(out_dir, f"{key}.glb")
        bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_apply=True,
                                  export_yup=True, export_materials="NONE")
        parts = len(body_objects())
        print(f"[chibi] {key}: {parts} 个零件，本体 {body_tris} + 圆环 {RUNTIME_RING_TRIS} + 描边 {hull_tris} = "
              f"{body_tris + RUNTIME_RING_TRIS + hull_tris} 三角形，"
              f"占地半宽 {half_w:.3f}，材质槽 {len(slots)} 个 {slots} → {path}")
    if failures:
        print("[chibi] 未通过：" + " | ".join(failures))
        sys.exit(1)


main()
