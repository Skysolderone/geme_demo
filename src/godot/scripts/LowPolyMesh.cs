using Godot;

namespace Siege.Godot;

/// <summary>
/// 地图元素的程序建模工具（map-elements-v2 D1）：基于 <see cref="SurfaceTool"/> 的静态函数，全部输出 <see cref="ArrayMesh"/>，
/// 每个网格只有 1 个 surface，顶点带平直法线与顶点色。棋子仍走 <see cref="LowPoly"/>，本类不收 <c>Prism</c>（裁决 M8）。
/// </summary>
/// <remarks>
/// <para><b>顶点色只当亮度系数</b>：材质打开 <c>VertexColorUseAsAlbedo</c>，最终颜色 = 材质底色 × 顶点色。所以按底色共用的材质仍是一色一份，
/// 压暗表照常整批改底色，出生区淡染与明暗棋盘格照旧改底色就能生效。Godot 4 的顶点色按每通道 8 位存（超过 1 的部分会被截掉），
/// 所以系数的有效区间是 <see cref="MinShade"/>–1：以 1 为本色，色差只往暗处走。</para>
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
    /// 多面岩块（岩石、碎石、垂岩）：低细分球（<paramref name="facets"/> 条经线、facets / 2 圈纬线），顶点按散列沿径向抖动，平直着色。
    /// 原点在球心；网格是闭合的（相邻三角面共用抖动后的同一点）。
    /// </summary>
    public static ArrayMesh FacetRock(float radius, int facets, float jitter, int seed)
    {
        facets = Math.Max(3, facets);
        int rings = Math.Max(2, facets / 2);
        var tool = Begin();

        // 顶点表：两极各 1 点，中间 (rings − 1) 圈各 facets 点。散列序号按 (圈, 经线) 编号，两极取 −1 / −2。
        Vector3 Point(int ring, int segment)
        {
            if (ring <= 0)
            {
                return Vector3.Up * radius * (1f + (jitter * Signed(seed, -1)));
            }

            if (ring >= rings)
            {
                return Vector3.Down * radius * (1f + (jitter * Signed(seed, -2)));
            }

            int index = ((segment % facets) + facets) % facets;
            float polar = MathF.PI * ring / rings;
            float azimuth = 2f * MathF.PI * index / facets;
            var direction = new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(azimuth));
            return direction * radius * (1f + (jitter * Signed(seed, (ring * facets) + index)));
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
    public static ArrayMesh Ribbon(IReadOnlyList<Vector3> points, float size, bool upright = false, float shade = 1f)
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
                Quad(tool, p, q, q + lift, p + lift, side, shade);
            }
            else
            {
                Vector3 half = side * (size * 0.5f);
                Quad(tool, p - half, q - half, q + half, p + half, Vector3.Up, shade);
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
    private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float shade)
    {
        Tri(tool, a, b, c, outward, shade);
        Tri(tool, a, c, d, outward, shade);
    }

    /// <summary>
    /// 一个平直着色的三角面：法线取三角面自身的法线（朝 <paramref name="outward"/> 一侧），绕序按 Godot 的正面约定（从正面看顺时针）调整。
    /// 退化三角面（面积为 0）跳过。顶点色是灰度亮度系数，截到 [<see cref="MinShade"/>, <see cref="MaxShade"/>]。
    /// </summary>
    private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float shade)
    {
        Vector3 cross = (b - a).Cross(c - a);
        if (cross.LengthSquared() < 1e-12f)
        {
            return;
        }

        bool counterClockwise = cross.Dot(outward) > 0f;
        Vector3 normal = (counterClockwise ? cross : -cross).Normalized();
        float s = Math.Clamp(shade, MinShade, MaxShade);
        var color = new Color(s, s, s);
        foreach (Vector3 v in counterClockwise ? new[] { a, c, b } : new[] { a, b, c })
        {
            tool.SetNormal(normal);
            tool.SetColor(color);
            tool.AddVertex(v);
        }
    }
}
