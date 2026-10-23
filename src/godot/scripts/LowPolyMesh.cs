using Godot;

namespace Siege.Godot;

/// <summary>
/// 地图元素的程序建模工具（map-elements-v2 D1）：基于 <see cref="SurfaceTool"/> 的静态函数，全部输出 <see cref="ArrayMesh"/>，
/// 每个网格只有 1 个 surface，顶点带平直法线与顶点色。棋子仍走 <see cref="LowPoly"/>，本类不收 <c>Prism</c>（裁决 M8）。
/// </summary>
/// <remarks>
/// <para><b>顶点色只当亮度系数</b>：材质打开 <c>VertexColorUseAsAlbedo</c>，最终颜色 = 材质底色 × 顶点色。所以按底色共用的材质仍是一色一份，
/// 压暗表照常整批改底色，出生区淡染与明暗棋盘格照旧改底色就能生效。Godot 4 的顶点色按每通道 8 位存（超过 1 的部分会被截掉），
/// 常规面系数的有效区间是 <see cref="MinShade"/>–1：以 1 为本色，色差只往暗处走；阴刻线允许局部更暗，古金铭文使用顶点色分通道调色。</para>
/// <para><b>确定性</b>：抖动与色差一律取自整数散列 <see cref="Hash"/>，不用随机数；同一组参数产出逐字节相同的网格。</para>
/// <para>绕序约定与 <see cref="LowPoly"/> 的棱柱相同：按"期望朝外"的方向用叉积定顶点顺序，不依赖调用方给的顺逆时针。</para>
/// </remarks>
public static class LowPolyMesh
{
    /// <summary>顶点色亮度系数下限（design D1：约 0.92）。</summary>
    public const float MinShade = 0.92f;

    /// <summary>顶点色亮度系数上限：8 位顶点色存不下大于 1 的值。</summary>
    public const float MaxShade = 1f;

    /// <summary>整数散列（murmur 风格的位混合）：同一 (seed, i) 永远得同一个值。</summary>
    public static uint Hash(int seed, int i)
    {
        unchecked
        {
            uint h = ((uint)seed * 0x9E3779B1u) ^ ((uint)i * 0x85EBCA77u) ^ 0x27D4EB2Fu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>散列换算成 [0, 1) 的浮点。</summary>
    public static float Unit(int seed, int i) => (Hash(seed, i) >> 8) * (1f / 16777216f);

    /// <summary>散列换算成 [−1, 1) 的浮点。</summary>
    public static float Signed(int seed, int i) => (Unit(seed, i) * 2f) - 1f;

    /// <summary>
    /// 顶面四边倒角的板（地砖顶板、衬底、外框、石沿）。原点在顶面中心，板体占 y ∈ [<paramref name="top"/> − size.Y, <paramref name="top"/>]、
    /// x ∈ ±size.X / 2、z ∈ ±size.Z / 2——包围盒与同尺寸的盒子逐值相同。底面不建（总压在别的东西上）。
    /// </summary>
    /// <param name="size">外形尺寸。</param>
    /// <param name="bevel">倒角宽（同时是倒角下沉的高度）；≤ 0 表示不倒角。</param>
    /// <param name="shade">整块的亮度系数。</param>
    /// <param name="seed">顶面色差的散列种子。</param>
    /// <param name="facetJitter">顶面四个三角面的色差幅度（相对 <paramref name="shade"/>，四面去均值，整块平均亮度不变）；顶面的扇心也按散列偏离中心至多 0.13 × 边长。</param>
    /// <param name="top">顶面所在的 y（缺省 0：原点在顶面）。</param>
    /// <param name="sideShade">四个侧面相对 <paramref name="shade"/> 的系数。</param>
    public static ArrayMesh BeveledSlab(Vector3 size, float bevel, float shade, int seed = 0, float facetJitter = 0f, float top = 0f, float sideShade = 1f)
    {
        var tool = Begin();
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        float b = Math.Clamp(bevel, 0f, Math.Min(Math.Min(hx, hz), size.Y) * 0.5f);
        float bottom = top - size.Y;

        // 顶面：内缩 b 的矩形，按偏心扇心切成四个三角面，四面各带一点色差（去均值）。
        var inner = new[]
        {
            new Vector3(-hx + b, top, -hz + b), new Vector3(hx - b, top, -hz + b),
            new Vector3(hx - b, top, hz - b), new Vector3(-hx + b, top, hz - b),
        };
        float jitter = facetJitter > 0f ? facetJitter : 0f;
        var apex = new Vector3(jitter > 0f ? Signed(seed, 0) * 0.13f * (hx - b) : 0f, top, jitter > 0f ? Signed(seed, 1) * 0.13f * (hz - b) : 0f);
        float[] facet = new float[4];
        float mean = 0f;
        for (int k = 0; k < 4; k++)
        {
            facet[k] = Signed(seed, 2 + k);
            mean += facet[k] * 0.25f;
        }

        for (int k = 0; k < 4; k++)
        {
            float s = shade * (1f + (jitter * (facet[k] - mean)));
            Tri(tool, apex, inner[k], inner[(k + 1) % 4], Vector3.Up, s);
        }

        // 倒角：内缩矩形 → 外沿（下沉 b），四条梯形。
        var outer = new[]
        {
            new Vector3(-hx, top - b, -hz), new Vector3(hx, top - b, -hz),
            new Vector3(hx, top - b, hz), new Vector3(-hx, top - b, hz),
        };
        var faces = new[] { Vector3.Forward, Vector3.Right, Vector3.Back, Vector3.Left };
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            if (b > 0f)
            {
                Vector3 hint = (faces[k] + Vector3.Up).Normalized();
                Quad(tool, inner[k], inner[n], outer[n], outer[k], hint, shade);
            }

            // 侧面：外沿 → 板底。
            Vector3 lowK = new(outer[k].X, bottom, outer[k].Z), lowN = new(outer[n].X, bottom, outer[n].Z);
            Quad(tool, outer[k], outer[n], lowN, lowK, faces[k], shade * sideShade);
        }

        return tool.Commit();
    }

    /// <summary>
    /// 共用地砖的雕刻角饰。两枚很短的 L 形刻线避开格心与格边，和顶板合并为一个 surface；
    /// 颜色仍由 BoardView 按地表覆盖，顶点色只把刻线轻压暗。
    /// </summary>
    public static ArrayMesh CarvedTile(Vector3 size, float bevel, int variant)
    {
        var parts = new List<(ArrayMesh, Transform3D)>
        {
            (BeveledSlab(size, bevel, 0.98f, seed: 101 + variant, facetJitter: 0.035f, sideShade: 0.95f), Transform3D.Identity),
        };
        float inner = size.X * 0.325f;
        float outer = size.X * 0.41f;
        float y = 0.0015f;
        for (int i = 0; i < 2; i++)
        {
            float sx = ((variant + i) & 1) == 0 ? 1f : -1f;
            float sz = i == 0 ? 1f : -1f;
            Vector3[] line =
            [
                new(sx * inner, y, sz * outer),
                new(sx * outer, y, sz * outer),
                new(sx * outer, y, sz * inner),
            ];
            parts.Add((Ribbon(line, 0.012f, shade: 0.70f, minShade: 0.68f), Transform3D.Identity));
        }

        return Merge(parts);
    }

    /// <summary>
    /// 分层侧面（地块的一层侧面带）：原点在该层带顶面中心，体占 y ∈ [−size.Y, 0]、x ∈ ±size.X / 2、z ∈ ±size.Z / 2。
    /// 每个侧面按高度分 <paramref name="bands"/> 条色带（亮度系数各不相同），沿面横向分 4 段，段点按散列<b>只向内</b>凹（外轮廓不超出 ±size / 2）。
    /// 顶面、底面不建：顶上压着衬底（铺满整格），底下是下一层带或底座。
    /// </summary>
    /// <param name="size">外形尺寸（链接图上恒为 CellSize × LayerHeight × CellSize）。</param>
    /// <param name="bands">色带数（不含岩沿与坡脚）。</param>
    /// <param name="jitter">段点向内凹的最大深度。</param>
    /// <param name="seed">散列种子（不同变体给不同种子）。</param>
    /// <param name="lip">
    /// 0 = 缓坡（每条棱都与格边齐平，相邻高台连成一整块实心的土）；&gt; 0 = 崖壁：顶边留一圈与格边齐平的岩沿，岩沿下的岩体内缩 <paramref name="lip"/>
    /// （格角处内缩 0.4 倍、段点处 0.7 倍，相邻崖壁之间读出竖向节理），坡脚收回格边、与下一层齐平。
    /// </param>
    /// <param name="crevice">崖壁每个面上一道岩缝的额外深度（位置按散列取）；缓坡给 0。</param>
    public static ArrayMesh Strata(Vector3 size, int bands, float jitter, int seed, float lip = 0f, float crevice = 0f)
    {
        bands = Math.Max(1, bands);
        bool cliff = lip > 0f;
        float depth = size.Y;

        // 行（自上而下）：y、格角处的内缩、段点处的固定内缩、是否参与散列凹凸 / 岩缝、以下一条带的亮度系数。
        var rows = new List<(float Y, float Corner, float Inner, bool Rough, float Shade)>();
        if (cliff)
        {
            const float lipHeight = 0.045f, underside = 0.025f, foot = 0.05f;
            rows.Add((0f, 0f, 0f, false, 1f));                                       // 岩沿正面
            rows.Add((-lipHeight, 0f, 0f, false, 0.93f));                            // 岩沿底面（朝下，压暗）
            float bodyTop = -(lipHeight + underside), bodyBottom = -(depth - foot);
            for (int k = 0; k < bands; k++)
            {
                float y = bodyTop + ((bodyBottom - bodyTop) * k / bands);
                rows.Add((y, lip * 0.4f, lip * 0.7f, k > 0, BandShade(seed, k, 0.95f, 0.04f)));
            }

            rows.Add((bodyBottom, lip * 0.4f, lip * 0.7f, false, 0.95f));           // 坡脚：收回格边
            rows.Add((-depth, 0f, 0f, false, 0f));
        }
        else
        {
            for (int k = 0; k <= bands; k++)
            {
                bool edge = k == 0 || k == bands;
                rows.Add((-depth * k / bands, 0f, 0f, !edge, k == bands ? 0f : BandShade(seed, k, 0.96f, 0.04f)));
            }
        }

        var tool = Begin();
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;

        // 四个面：起点角、终点角（沿面方向）、朝外法线。角点按格角顺序排，相邻两面共用同一个角。
        var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
        var outward = new[] { new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f) };
        const int segments = 4;
        for (int face = 0; face < 4; face++)
        {
            Vector2 a = corners[face], b = corners[(face + 1) % 4], n = outward[face];
            int salt = (face + 1) * 1000;

            // 段点沿面的位置：两端是格角，中间三点按散列前后错开（不出段界）。
            float[] t = new float[segments + 1];
            for (int c = 0; c <= segments; c++)
            {
                t[c] = c == 0 || c == segments ? c / (float)segments : (c + (Signed(seed, salt + c) * 0.28f)) / segments;
            }

            int creviceAt = 1 + (int)(Hash(seed, salt + 7) % (segments - 1));
            var grid = new Vector3[rows.Count, segments + 1];
            for (int r = 0; r < rows.Count; r++)
            {
                (float y, float corner, float inner, bool rough, _) = rows[r];

                // 该行的外轮廓矩形内缩 corner（格角处两个面共用这一点，面与面严丝合缝），段点再沿法线向内多凹一点。
                Vector2 start = a - (n * corner) + (Tangent(face) * corner);
                Vector2 end = b - (n * corner) - (Tangent(face) * corner);
                for (int c = 0; c <= segments; c++)
                {
                    float extra = 0f;
                    if (c > 0 && c < segments)
                    {
                        extra = inner - corner;
                        if (rough)
                        {
                            extra += jitter * Unit(seed, salt + (r * 31) + c);
                            if (cliff && c == creviceAt)
                            {
                                extra += crevice;
                            }
                        }
                    }

                    Vector2 p = start.Lerp(end, t[c]) - (n * extra);
                    grid[r, c] = new Vector3(p.X, y, p.Y);
                }
            }

            var normal = new Vector3(n.X, 0f, n.Y);
            for (int r = 0; r + 1 < rows.Count; r++)
            {
                float band = rows[r].Shade;
                for (int c = 0; c < segments; c++)
                {
                    // 每格面片再带一点散列色差（±1.5%），平直着色下读出碎岩 / 土块的块面感。
                    float s = band * (1f - (0.015f * Unit(seed, salt + 500 + (r * 17) + c)));
                    Quad(tool, grid[r, c], grid[r, c + 1], grid[r + 1, c + 1], grid[r + 1, c], normal, s);
                }
            }
        }

        return tool.Commit();

        // 沿面方向（起点角 → 终点角）的单位向量。
        static Vector2 Tangent(int face) => face switch
        {
            0 => new Vector2(1f, 0f),
            1 => new Vector2(0f, 1f),
            2 => new Vector2(-1f, 0f),
            _ => new Vector2(0f, -1f),
        };
    }

    /// <summary>
    /// 崖壁的错缝砌石面：在原有岩层与深缝前方嵌入两排小块面石。面石仍在整格轮廓内，
    /// 顶部岩沿、底部坡脚和格角保持原有形状；一层只用一份网格及岩灰材质。
    /// </summary>
    public static ArrayMesh CoursedCliff(Vector3 size, int seed)
    {
        var parts = new List<(ArrayMesh, Transform3D)>
        {
            (Strata(size, bands: 3, jitter: 0.02f, seed: seed, lip: 0.05f, crevice: 0.05f), Transform3D.Identity),
        };

        float edge = (size.X * 0.5f) - 0.045f;
        float front = -(size.Z * 0.5f) + 0.008f;
        float courseHeight = size.Y * 0.27f;
        float gapY = size.Y * 0.05f;
        float firstTop = -size.Y * 0.23f;
        const float joint = 0.012f;
        var faceBasis = new Basis(Vector3.Right, -Mathf.Pi * 0.5f);

        for (int face = 0; face < 4; face++)
        {
            var turn = new Transform3D(new Basis(Vector3.Up, -face * Mathf.Pi * 0.5f), Vector3.Zero);
            for (int row = 0; row < 2; row++)
            {
                // 上下两排错开竖缝；散列只轻微移动内部切口，不改变外轮廓。
                float[] cuts = row == 0
                    ? [-edge, -size.X * 0.16f, size.X * 0.16f, edge]
                    : [-edge, -size.X * 0.275f, 0f, size.X * 0.275f, edge];
                for (int i = 1; i + 1 < cuts.Length; i++)
                {
                    cuts[i] += Signed(seed, (face * 31) + (row * 11) + i) * size.X * 0.018f;
                }

                for (int block = 0; block + 1 < cuts.Length; block++)
                {
                    float left = cuts[block] + (block == 0 ? 0f : joint * 0.5f);
                    float right = cuts[block + 1] - (block + 2 == cuts.Length ? 0f : joint * 0.5f);
                    float lowered = Unit(seed, 100 + (face * 29) + (row * 7) + block) * 0.004f;
                    float top = firstTop - (row * (courseHeight + gapY)) - lowered;
                    float shade = 0.95f + (0.05f * Unit(seed, 200 + (face * 29) + (row * 7) + block));
                    ArrayMesh stone = BeveledSlab(new Vector3(right - left, 0.028f, courseHeight - 0.002f), 0.007f,
                        shade, seed: seed + (face * 41) + (row * 13) + block, facetJitter: 0.02f, sideShade: 0.97f);
                    var at = new Vector3((left + right) * 0.5f, top - (courseHeight * 0.5f), front);
                    parts.Add((stone, turn * new Transform3D(faceBasis, at)));
                }
            }
        }

        return Merge(parts);
    }

    /// <summary>
    /// 多面岩块（岩石、碎石、垂岩）：低细分球（<paramref name="facets"/> 条经线、facets / 2 圈纬线），顶点按散列沿径向抖动，平直着色。
    /// 原点在球心；网格是闭合的（相邻三角面共用抖动后的同一点）。
    /// </summary>
    /// <param name="stretch">
    /// 各轴的拉伸（缺省不拉伸）：直接乘在顶点上、再按三角面自身求法线，所以压扁的鹅卵石、拉长的香蒲穗法线仍正确——
    /// 不要在 <see cref="Merge"/> 的变换里做非均匀缩放（<c>AppendFrom</c> 只用 basis 转法线）。
    /// </param>
    public static ArrayMesh FacetRock(float radius, int facets, float jitter, int seed, Vector3? stretch = null)
    {
        Vector3 scale = stretch ?? Vector3.One;
        facets = Math.Max(3, facets);
        int rings = Math.Max(2, facets / 2);
        var tool = Begin();

        // 顶点表：两极各 1 点，中间 (rings − 1) 圈各 facets 点。散列序号按 (圈, 经线) 编号，两极取 −1 / −2。
        Vector3 Point(int ring, int segment)
        {
            if (ring <= 0)
            {
                return Vector3.Up * radius * (1f + (jitter * Signed(seed, -1))) * scale;
            }

            if (ring >= rings)
            {
                return Vector3.Down * radius * (1f + (jitter * Signed(seed, -2))) * scale;
            }

            int index = ((segment % facets) + facets) % facets;
            float polar = MathF.PI * ring / rings;
            float azimuth = 2f * MathF.PI * index / facets;
            var direction = new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(azimuth));
            return direction * radius * (1f + (jitter * Signed(seed, (ring * facets) + index))) * scale;
        }

        int face = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int s = 0; s < facets; s++)
            {
                Vector3 a = Point(ring, s), b = Point(ring, s + 1), c = Point(ring + 1, s + 1), d = Point(ring + 1, s);
                float shade = 1f - (0.06f * Unit(seed, 10_000 + face++));
                if (ring == 0)
                {
                    Tri(tool, a, d, c, (a + c + d) / 3f, shade);
                }
                else if (ring == rings - 1)
                {
                    Tri(tool, a, b, d, (a + b + d) / 3f, shade);
                }
                else
                {
                    Quad(tool, a, b, c, d, (a + b + c + d) * 0.25f, shade);
                }
            }
        }

        return tool.Commit();
    }

    /// <summary>
    /// 细条（裂纹、水纹、芦苇等）：沿折线 <paramref name="points"/> 铺一条宽 <paramref name="size"/> 的带。
    /// <paramref name="upright"/> 为假时贴地（在 XZ 平面内向两侧各展开一半宽，朝上）；为真时立起（从折线向上展开整宽，朝折线的左侧）。
    /// </summary>
    public static ArrayMesh Ribbon(IReadOnlyList<Vector3> points, float size, bool upright = false, float shade = 1f,
        float minShade = MinShade, Color? tint = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        var tool = Begin();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector3 p = points[i], q = points[i + 1];
            var along = new Vector3(q.X - p.X, 0f, q.Z - p.Z);
            if (along.LengthSquared() < 1e-10f)
            {
                continue;
            }

            Vector3 side = Vector3.Up.Cross(along.Normalized());
            if (upright)
            {
                Vector3 lift = Vector3.Up * size;
                if (tint is { } color)
                {
                    Quad(tool, p, q, q + lift, p + lift, side, color);
                }
                else
                {
                    Quad(tool, p, q, q + lift, p + lift, side, shade, minShade);
                }
            }
            else
            {
                Vector3 half = side * (size * 0.5f);
                if (tint is { } color)
                {
                    Quad(tool, p - half, q - half, q + half, p + half, Vector3.Up, color);
                }
                else
                {
                    Quad(tool, p - half, q - half, q + half, p + half, Vector3.Up, shade, minShade);
                }
            }
        }

        return tool.Commit();
    }

    /// <summary>
    /// 把几块网格按各自的变换并成一个网格（仍是 1 个 surface）：多块拼成的部件（石沿、桥、栅栏）用它，一件部件只占一次绘制。
    /// 法线随变换的旋转部分一起转，顶点色原样保留；输入顺序即顶点顺序，所以结果仍是确定的。
    /// </summary>
    public static ArrayMesh Merge(IEnumerable<(ArrayMesh Mesh, Transform3D Transform)> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var tool = Begin();
        foreach ((ArrayMesh mesh, Transform3D transform) in parts)
        {
            tool.AppendFrom(mesh, 0, transform);
        }

        return tool.Commit();
    }

    /// <summary>
    /// 一根直梁（栏杆、横杆、立柱）：从 <paramref name="from"/> 到 <paramref name="to"/>，截面 <paramref name="width"/> × <paramref name="height"/>（宽沿水平、与梁垂直，高沿竖直）。
    /// 两端面都建，底面也建（横杆悬空，侧下方看得到）；竖直的梁（立柱）请用 <see cref="BeveledSlab"/>。
    /// </summary>
    public static ArrayMesh Beam(Vector3 from, Vector3 to, float width, float height, float shade = 1f)
    {
        var tool = Begin();
        Vector3 along = to - from;
        var flat = new Vector3(along.X, 0f, along.Z);
        if (flat.LengthSquared() < 1e-10f)
        {
            return tool.Commit();
        }

        Vector3 side = Vector3.Up.Cross(flat.Normalized()) * (width * 0.5f);
        Vector3 rise = Vector3.Up * (height * 0.5f);
        Vector3 center = (from + to) * 0.5f;

        // 截面四角（自左上起逆着看），两端各一圈。
        Vector3[] start = [from + side + rise, from - side + rise, from - side - rise, from + side - rise];
        Vector3[] end = [to + side + rise, to - side + rise, to - side - rise, to + side - rise];
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Vector3 mid = (start[k] + start[n] + end[k] + end[n]) * 0.25f;
            float s = k == 2 ? shade * 0.94f : shade;   // 底面略暗
            Quad(tool, start[k], start[n], end[n], end[k], mid - center, s);
        }

        Quad(tool, start[0], start[1], start[2], start[3], from - to, shade);
        Quad(tool, end[0], end[1], end[2], end[3], to - from, shade);
        return tool.Commit();
    }

    /// <summary>
    /// 竖立的棱台 / 棱锥（树干、锥形树冠、仙人掌柱、芦苇叶）：底面是 <paramref name="sides"/> 边形、在 y = 0，顶面在 y = <paramref name="height"/>，
    /// 顶面中心可水平偏移 <paramref name="lean"/>（x, z），做出斜立的叶片。<paramref name="topRadius"/> ≤ 0 时收成一个尖。原点在底面中心。
    /// 侧面都建；顶面（非尖顶时）建；底面只在 <paramref name="bottomCap"/> 为真时建（悬空的树冠要，插在地上的不要）。网格闭合，所以竖起的细叶两面都看得到——
    /// 这是 <see cref="Ribbon"/> 立起时做不到的（它只有一面），也是 <see cref="Beam"/> 做不到的（竖直输入返回空网格）。
    /// </summary>
    public static ArrayMesh Frustum(float bottomRadius, float topRadius, float height, int sides, float shade = 1f, Vector2 lean = default, float yaw = 0f, bool bottomCap = false)
    {
        sides = Math.Max(3, sides);
        var tool = Begin();
        var top = new Vector3(lean.X, height, lean.Y);
        bool apex = topRadius <= 0f;
        Vector3 Ring(float radius, Vector3 center, int k)
        {
            float a = yaw + (2f * MathF.PI * k / sides);
            return center + new Vector3(MathF.Cos(a) * radius, 0f, MathF.Sin(a) * radius);
        }

        for (int k = 0; k < sides; k++)
        {
            Vector3 b0 = Ring(bottomRadius, Vector3.Zero, k), b1 = Ring(bottomRadius, Vector3.Zero, k + 1);
            if (apex)
            {
                Tri(tool, b0, b1, top, ((b0 + b1 + top) / 3f) - (top * (1f / 3f)), shade);
            }
            else
            {
                Vector3 t0 = Ring(topRadius, top, k), t1 = Ring(topRadius, top, k + 1);
                Quad(tool, b0, b1, t1, t0, ((b0 + b1 + t0 + t1) * 0.25f) - (top * 0.5f), shade);
                Tri(tool, top, t0, t1, Vector3.Up, shade);
            }

            if (bottomCap)
            {
                Tri(tool, Vector3.Zero, b0, b1, Vector3.Down, shade * 0.95f);
            }
        }

        return tool.Commit();
    }

    /// <summary>
    /// 贴地的扁斑块（水洼）：<paramref name="sides"/> 边形，各顶点半径按散列在 ±<paramref name="jitter"/> 内错开（星形，从中心扇形三角化总是合法），
    /// z 向乘 <paramref name="aspect"/> 压成椭圆。顶面在 y = <paramref name="thickness"/>、底在 y = 0；顶面与侧面都建，底面不建（压在地砖上）。原点在底面中心。
    /// </summary>
    public static ArrayMesh Patch(float radius, int sides, float thickness, int seed, float jitter, float aspect = 1f, float shade = 1f)
    {
        sides = Math.Max(3, sides);
        var tool = Begin();
        var outline = new Vector3[sides];
        for (int k = 0; k < sides; k++)
        {
            float a = 2f * MathF.PI * k / sides;
            float r = radius * (1f + (jitter * Signed(seed, k)));
            outline[k] = new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r * aspect);
        }

        var center = new Vector3(0f, thickness, 0f);
        var lift = new Vector3(0f, thickness, 0f);
        for (int k = 0; k < sides; k++)
        {
            Vector3 a = outline[k], b = outline[(k + 1) % sides];
            Tri(tool, center, a + lift, b + lift, Vector3.Up, shade);
            Quad(tool, a + lift, b + lift, b, a, new Vector3(a.X + b.X, 0f, a.Z + b.Z), shade * 0.96f);
        }

        return tool.Commit();
    }

    /// <summary>
    /// 放样（浮岛岩层、垂岩）：若干圈点数相同的环自上而下排列，相邻两圈连成四边面；某圈收成一点即成尖（退化的三角面自动跳过）。
    /// 每条带的亮度系数取 <paramref name="shades"/>[k]（第 k 圈到第 k + 1 圈）；可选封顶 / 封底（从该圈中心扇形三角化，环须是星形）。
    /// 面朝向按"从环的中轴朝外"判定，所以环的点序顺逆都可以。
    /// </summary>
    public static ArrayMesh Loft(IReadOnlyList<Vector3[]> rings, IReadOnlyList<float> shades, bool capTop = false, bool capBottom = false)
    {
        ArgumentNullException.ThrowIfNull(rings);
        ArgumentNullException.ThrowIfNull(shades);
        var tool = Begin();
        for (int k = 0; k + 1 < rings.Count; k++)
        {
            Vector3[] upper = rings[k], lower = rings[k + 1];
            Vector3 axis = (Centroid(upper) + Centroid(lower)) * 0.5f;
            for (int i = 0; i < upper.Length; i++)
            {
                int j = (i + 1) % upper.Length;
                Vector3 middle = (upper[i] + upper[j] + lower[i] + lower[j]) * 0.25f;
                var outward = new Vector3(middle.X - axis.X, 0f, middle.Z - axis.Z);
                Quad(tool, upper[i], upper[j], lower[j], lower[i], outward, shades[Math.Min(k, shades.Count - 1)]);
            }
        }

        if (capTop)
        {
            Fan(rings[0], Vector3.Up, shades[0]);
        }

        if (capBottom)
        {
            Fan(rings[^1], Vector3.Down, MinShade);
        }

        return tool.Commit();

        void Fan(Vector3[] ring, Vector3 outward, float shade)
        {
            Vector3 center = Centroid(ring);
            for (int i = 0; i < ring.Length; i++)
            {
                Tri(tool, center, ring[i], ring[(i + 1) % ring.Length], outward, shade);
            }
        }

        static Vector3 Centroid(Vector3[] ring)
        {
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 p in ring)
            {
                sum += p;
            }

            return sum / ring.Length;
        }
    }

    /// <summary>某条色带的亮度系数：<paramref name="baseShade"/> 起，按散列在 ±<paramref name="spread"/> / 2 内错开。</summary>
    private static float BandShade(int seed, int band, float baseShade, float spread) =>
        baseShade + (spread * 0.5f * Signed(seed, 900 + band));

    private static SurfaceTool Begin()
    {
        var tool = new SurfaceTool();
        tool.Begin(global::Godot.Mesh.PrimitiveType.Triangles);
        return tool;
    }

    /// <summary>四边形 a-b-c-d（沿边依次排列）拆成两个三角面。</summary>
    private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float shade,
        float minShade = MinShade)
    {
        Tri(tool, a, b, c, outward, shade, minShade);
        Tri(tool, a, c, d, outward, shade, minShade);
    }

    private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color tint)
    {
        Tri(tool, a, b, c, outward, tint);
        Tri(tool, a, c, d, outward, tint);
    }

    /// <summary>
    /// 一个平直着色的三角面：法线取三角面自身的法线（朝 <paramref name="outward"/> 一侧），绕序按 Godot 的正面约定（从正面看顺时针）调整。
    /// 退化三角面（面积为 0）跳过。常规面使用灰度亮度系数，阴刻线可传入更低的局部下限。
    /// </summary>
    private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float shade,
        float minShade = MinShade)
    {
        float s = Math.Clamp(shade, minShade, MaxShade);
        Tri(tool, a, b, c, outward, new Color(s, s, s));
    }

    private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color tint)
    {
        Vector3 cross = (b - a).Cross(c - a);
        if (cross.LengthSquared() < 1e-12f)
        {
            return;
        }

        bool counterClockwise = cross.Dot(outward) > 0f;
        Vector3 normal = (counterClockwise ? cross : -cross).Normalized();
        foreach (Vector3 v in counterClockwise ? new[] { a, c, b } : new[] { a, b, c })
        {
            tool.SetNormal(normal);
            tool.SetColor(tint);
            tool.AddVertex(v);
        }
    }
}
