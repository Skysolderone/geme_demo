using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 演出音效提示（show-sound-cues 1.1 / 1.2，design.md D1）。
/// <see cref="SoundCues.Between"/> 只比较推进前后的遮罩与跨过的节拍，不读时钟、不消费随机；这里用固定增量逐步推进断言每帧导出的提示。
/// 创建时间线本身没有 <c>Advance</c> 跨越，图形侧以 <see cref="ShowMask.Empty"/> 为"前"调一次（<see cref="Start"/>），首拍在进度 0 就"当前"的节拍由此发声。
/// </summary>
/// <remarks>
/// 变异验证（show-sound-cues 3.1）：
/// M-SC1「同帧不合并」——<c>SoundCues.Between</c> 里落子改为 <c>before.Hidden</c> 每离开一枚各出一个提示（去掉合并）→ 红 2（压缩落子整批一次、同帧合并）。
/// M-SC2「零时长仍导出」——<c>ShowTimeline.Mask()</c> 去掉 <c>IsFinished ||</c> 守卫 → 本类红 1（零时长无提示：零时长时间线的遮罩仍带领地段 / 横幅中间态），
/// 连同 <c>无人值守运行下演出时长为零Tests.零时长模式创建即播完</c>、<c>横幅节拍Tests.无人值守仍零时长</c>、<c>落子与提子节拍的内容Tests.飘字寿命跨节拍</c> 共红 4。
/// M-SC3「跳过的段也发声」——领地提示去掉 <c>!change.TerritoryDelta.IsZero</c>、只看段序号跨过 → 红 2（提子、领地段为零只响军势）。
/// 每条变异前 cp 备份、还原后 cmp 逐字节校验并刷新 mtime，还原后全量 2041 通过 / 9 跳过（与变异前相同）。
/// tiered-number-show 1.5（design.md D7）：提示带档位、新增"揭示"。投影写法：一档只写种类（"Placement"），高于一档写"种类:档位"（"Group:2"）。
/// M-SC1 的写法随之失效（提示按种类各至多一个，无从"不合并"），由下面 M-SD1 / M-SD6 取代；M-SC2 复跑红 6、M-SC3 复跑红 3（同上两条，加 军势揭示节拍Tests.压缩后遮罩与音效按压缩后的时刻）。
/// 变异验证（tiered-number-show 段 A）。每条变异由脚本做：二进制读入原文并另存备份 → 断言锚点恰命中 1 次 → 改写 → 跑 SettlementShow + TacticalLayers 两个命名空间（基线 215 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。
/// M-SD1「同帧合并取最低档」——<c>SoundCues.PowerStagesEntered</c> 里领地的 <c>Math.Max</c> 改为取最小的非零档 → 红 1（同帧合并）。
/// M-SD2「揭示只响第一声」——<c>RevealTierStarted</c> 里推进前已有已开始的步即不再发声 → 红 3（揭示逐步发声且档位递增、序列确定性、军势揭示节拍Tests.压缩后遮罩与音效按压缩后的时刻）。
/// M-SD3「军势到账恒一档」——<c>PowerStagesEntered</c> 里军势恒取一档 → 红 6（提子、两段到账两声、领地段为零只响军势、同帧合并、揭示逐步发声且档位递增、序列确定性）。
/// M-SD4「揭示提示排到军势之后」——<c>Between</c> 里揭示那一段挪到军势之后 → 红 1（揭示逐步发声且档位递增 的同帧顺序）。
/// M-SD5「跨过整拍不发声」——<c>RevealTierStarted</c> 不看本帧跨过的节拍 → 红 1（揭示逐步发声且档位递增）。
/// M-SD6「取最后一步的档」——<c>Math.Max(highest, started[i])</c> 改为 <c>started[i]</c> → 红 1（揭示逐步发声且档位递增 的跨条目一帧）。
/// M-SD7「步的开始时刻不算已开始」——<c>PowerRevealBeat.StepTiersStartedBy</c> 里 <c>&gt;</c> 改为 <c>&gt;=</c> → 红 4（揭示逐步发声且档位递增、序列确定性、零时长无提示、军势揭示节拍Tests.压缩后遮罩与音效按压缩后的时刻）。
/// M-SD8「枚举里揭示挪到横幅之后」→ 红 1（揭示逐步发声且档位递增 的枚举序断言）。
/// </remarks>
public class 演出音效提示Tests
{
    /// <summary>创建瞬间的提示（图形侧 BeginShow 时的那一次调用）。</summary>
    private static string Start(ShowTimeline timeline) => Text(SoundCues.Between(ShowMask.Empty, timeline.Mask(), []));

    /// <summary>推进一帧并导出本帧提示（图形侧 AdvanceShow 每帧的做法）。</summary>
    private static string Step(ShowTimeline timeline, int ms, bool fast = false)
    {
        ShowMask before = timeline.Mask();
        ImmutableArray<SettlementBeat> crossed = timeline.Advance(ms, fast);
        return Text(SoundCues.Between(before, timeline.Mask(), crossed));
    }

    /// <summary>提示序列的文本投影：一档只写种类，高于一档写"种类:档位"。</summary>
    private static string Text(ImmutableArray<SoundCue> cues) => string.Join(",", cues.Select(c => c.Tier == 1 ? $"{c.Kind}" : $"{c.Kind}:{c.Tier}"));

    private static ImmutableArray<PlacedPiece> Pieces(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new PlacedPiece(Coord.Parse($"A{i}"), PieceType.Basic, P1))];

    [Fact]
    public void 落子逐枚发声()
    {
        // 3 枚：整拍 750 ms，第 i 枚在 i×250 ms 起出现；三次推进各导出一个"落子"。
        var timeline = new ShowTimeline([new PlacementBeat(Pieces(3))], ShowDuration.Normal);
        Assert.Equal(string.Empty, Start(timeline));
        Assert.Equal("Placement", Step(timeline, 100));   // 100 ms：A1 出现
        Assert.Equal(string.Empty, Step(timeline, 100));  // 200 ms：没有新棋子
        Assert.Equal("Placement", Step(timeline, 150));   // 350 ms：A2 出现
        Assert.Equal("Placement", Step(timeline, 250));   // 600 ms：A3 出现
        Assert.Equal(string.Empty, Step(timeline, 150));  // 750 ms：播完，没有新棋子
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 压缩落子整批一次()
    {
        // 本机玩家自己的确认闪动：3 枚同步出现，只响一声；单帧跨过整个落子节拍同样只响一声。
        var compressed = new ShowTimeline([new PlacementBeat(Pieces(3), Compressed: true)], ShowDuration.Normal);
        Assert.Equal(string.Empty, Start(compressed));
        Assert.Equal("Placement", Step(compressed, 100));
        Assert.Equal(string.Empty, Step(compressed, 100));

        var crossed = new ShowTimeline([new PlacementBeat(Pieces(3))], ShowDuration.Normal);
        Assert.Equal("Placement", Step(crossed, 2000));
        Assert.True(crossed.IsFinished);
    }

    [Fact]
    public void 提子()
    {
        // 1.2 算例：落子 2 枚（500 ms）→ 提子（600 ms）→ 势力（900 ms，P1 只有军势增量、P2 只有军势减量）。
        var timeline = new ShowTimeline(CaptureExampleBeats(), ShowDuration.Normal);
        Assert.Equal(string.Empty, Start(timeline));
        Assert.Equal("Placement", Step(timeline, 250));            // C3 出现
        Assert.Equal("Placement,Capture", Step(timeline, 250));    // 500 ms：C4 出现并跨入提子节拍
        Assert.Equal(string.Empty, Step(timeline, 100));           // 提子进行中不重复
        Assert.Equal("Group:2", Step(timeline, 500));              // 1100 ms：跨入势力节拍，领地增量为 0 → 直接军势段，只响军势（P1 +4 二档、P2 −2 一档，取最高）
        Assert.Equal(string.Empty, Step(timeline, 900));           // 播完
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 信物()
    {
        var beats = ImmutableArray.Create<SettlementBeat>(new RelicRevealBeat([new RevealedRelic(Coord.Parse("E5"), "烽火")]));
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        Assert.Equal("Relic", Start(timeline));                    // 首拍在进度 0 即当前
        Assert.Equal(string.Empty, Step(timeline, 300));
        Assert.Equal(string.Empty, Step(timeline, 300));
        Assert.True(timeline.IsFinished);

        // 放在落子之后：跨入时发声；单帧连跨落子与信物两拍也各响一声。
        var after = new ShowTimeline([new PlacementBeat(Pieces(1)), beats[0]], ShowDuration.Normal);
        Assert.Equal("Placement,Relic", Step(after, 250));
        var jump = new ShowTimeline([new PlacementBeat(Pieces(1)), beats[0]], ShowDuration.Normal);
        Assert.Equal("Placement,Relic", Step(jump, 5000));
    }

    [Fact]
    public void 两段到账两声()
    {
        // P1 领地 +3（一档）、军势 +6（二档）：领地段 0–350 ms、军势段 350–700 ms、定格到 900 ms。
        var timeline = new ShowTimeline([new PowerBeat([new PowerChange(P1, 5, 14, 2, 1, 3, 6)])], ShowDuration.Normal);
        Assert.Equal("Territory", Start(timeline));
        Assert.Equal(string.Empty, Step(timeline, 100));
        Assert.Equal("Group:2", Step(timeline, 250));              // 350 ms：进入军势段
        Assert.Equal(string.Empty, Step(timeline, 100));
        Assert.Equal(string.Empty, Step(timeline, 250));           // 700 ms：定格
        Assert.Equal(string.Empty, Step(timeline, 200));           // 900 ms：播完
        Assert.True(timeline.IsFinished);

        // 提速下单帧跨过两段：两声都要有。
        var fast = new ShowTimeline([new PowerBeat([new PowerChange(P1, 5, 14, 2, 1, 3, 6)])], ShowDuration.Normal);
        Assert.Equal("Territory", Start(fast));
        Assert.Equal("Group:2", Step(fast, 100, fast: true));    // 400 ms
    }

    [Fact]
    public void 领地段为零只响军势()
    {
        // 只有军势增量：Pending → 军势段，没有领地段就没有"领地到账"。
        var timeline = new ShowTimeline([new PowerBeat([new PowerChange(P1, 5, 9, 2, 1)])], ShowDuration.Normal);
        Assert.Equal("Group:2", Start(timeline));                  // 军势 +4：二档
        Assert.Equal(string.Empty, Step(timeline, 900));
    }

    [Fact]
    public void 同帧合并()
    {
        // 两名玩家同时进入领地段，增量 +3（一档）与 +9（三档）→ 只导出一个"领地到账"，档位取最高的三档；
        // 同时进入军势段同理：+6（二档）与 −3（一档）→ 一个"军势到账"、二档。
        PowerChange p1 = new(P1, 5, 14, 2, 1, 3, 6);
        PowerChange p2 = new(P2, 6, 12, 1, 2, 9, -3);
        var timeline = new ShowTimeline([new PowerBeat([p1, p2])], ShowDuration.Normal);
        ImmutableArray<SoundCue> territory = SoundCues.Between(ShowMask.Empty, timeline.Mask(), []);
        Assert.Equal(new SoundCue(SoundCueKind.Territory, 3), Assert.Single(territory));
        Assert.Equal("Group:2", Step(timeline, 350));

        // 高档在前、低档在后同样取最高（不是取最后一个）。
        var reversed = new ShowTimeline([new PowerBeat([p2 with { Player = P1 }, p1 with { Player = P2 }])], ShowDuration.Normal);
        Assert.Equal("Territory:3", Start(reversed));

        // 落子 4 枚单帧全部出现 + 跨入提子：落子只一声。
        var many = new ShowTimeline([new PlacementBeat(Pieces(4)), new CaptureBeat([new CapturedPiece(Coord.Parse("D3"), P2, PieceType.Basic)])], ShowDuration.Normal);
        Assert.Equal("Placement,Capture", Step(many, 1000));
    }

    [Fact]
    public void 揭示逐步发声且档位递增()
    {
        // 军势揭示节拍中一个四步条目（军势 10：档位一、一、二、三），每步 220 ms：四次导出各一个"揭示"，档位依次一、一、二、三。
        // 第一步在节拍进度 0 就开始——节拍排在首位时由创建瞬间的那一次调用导出，其后三次推进各一步。
        ImmutableArray<SettlementBeat> beats = [new PowerRevealBeat([RevealEntry.From(Group(P1, 5, 2, 1, 10, "C3"))])];
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        ImmutableArray<SoundCue> first = SoundCues.Between(ShowMask.Empty, timeline.Mask(), []);
        Assert.Equal(new SoundCue(SoundCueKind.Reveal, 1), Assert.Single(first));
        Assert.Equal(string.Empty, Step(timeline, 100));          // 第一步进行中不重复
        Assert.Equal("Reveal", Step(timeline, 120));              // 220 ms：第二步（一档）
        Assert.Equal("Reveal:2", Step(timeline, 220));            // 440 ms：第三步（二档）
        Assert.Equal("Reveal:3", Step(timeline, 220));            // 660 ms：末步（三档）
        Assert.Equal(string.Empty, Step(timeline, 220));          // 880 ms：播完，没有新的一步
        Assert.True(timeline.IsFinished);

        // 排在落子之后：四次推进各导出一个"揭示"；跨入揭示节拍的那一帧同时有落子，按节拍顺序落子在前。
        var after = new ShowTimeline([new PlacementBeat(Pieces(1)), beats[0]], ShowDuration.Normal);
        Assert.Equal("Placement,Reveal", Step(after, 250));
        Assert.Equal("Reveal", Step(after, 220));
        Assert.Equal("Reveal:2", Step(after, 220));
        Assert.Equal("Reveal:3", Step(after, 220));

        // 同帧同种合并取最高档：一帧跨过第二、三步（一档与二档）→ 一个二档；一帧跨过整个节拍 → 一个三档。
        var merged = new ShowTimeline(beats, ShowDuration.Normal);
        Assert.Equal("Reveal", Start(merged));
        Assert.Equal("Reveal:2", Step(merged, 500));
        Assert.Equal("Reveal:3", Step(merged, 5000));
        Assert.True(merged.IsFinished);
        var jump = new ShowTimeline([new PlacementBeat(Pieces(1)), beats[0], new PowerBeat([new PowerChange(P1, 5, 14, 2, 1, 3, 6)])], ShowDuration.Normal);
        Assert.Equal("Placement,Reveal:3,Territory", Step(jump, 250 + 880));   // 落子 → 揭示（整拍跨过）→ 进入领地段：同帧按节拍顺序排
        Assert.Equal("Group:2", Step(jump, 350));

        // "揭示"排在信物之后、领地之前（枚举序即同帧排序序号）。
        Assert.Equal(
            [SoundCueKind.Placement, SoundCueKind.Capture, SoundCueKind.Relic, SoundCueKind.Reveal, SoundCueKind.Territory, SoundCueKind.Group, SoundCueKind.Banner],
            Enum.GetValues<SoundCueKind>());

        // 取最高档而不是最后一步的档：一帧里先开始前一条目的末步（军势 20，四档），再开始后一条目的第一步（军势 36 的第一步，二档）→ 四档。
        var across = new ShowTimeline(
            [new PlacementBeat(Pieces(1)), new PowerRevealBeat([RevealEntry.From(Group(P1, 20, 0, 0, 20, "C3")), RevealEntry.From(Group(P1, 20, 4, 1, 36, "G7"))])],
            ShowDuration.Normal);
        Assert.Equal("Placement,Reveal:4", Step(across, 250 + 440 + 10));

        // 两个条目（军势 3 一步、军势 36 四步：档位一 | 二、三、四、五）：跨条目连续数步，逐步照各步的档位发声。
        var two = new ShowTimeline(
            [new PowerRevealBeat([RevealEntry.From(Group(P1, 3, 0, 0, 3, "C3")), RevealEntry.From(Group(P1, 20, 4, 1, 36, "G7"))])],
            ShowDuration.Normal);
        Assert.Equal("Reveal", Start(two));
        Assert.Equal(["Reveal:2", "Reveal:3", "Reveal:4", "Reveal:5"], Enumerable.Range(0, 4).Select(_ => Step(two, 220)));
    }

    [Fact]
    public void 横幅逐条()
    {
        ImmutableArray<string> banners = [BannerBeat.EliminatedText(P2), BannerBeat.EliminatedText(P3), BannerBeat.MatchEndedText];
        var timeline = new ShowTimeline([new BannerBeat(banners)], ShowDuration.Normal);
        Assert.Equal("Banner", Start(timeline));
        Assert.Equal(string.Empty, Step(timeline, 400));
        Assert.Equal("Banner", Step(timeline, 400));               // 800 ms：第 2 条
        Assert.Equal("Banner", Step(timeline, 800));               // 1600 ms：第 3 条
        Assert.Equal(string.Empty, Step(timeline, 800));           // 播完
        Assert.True(timeline.IsFinished);

        // 单帧跨过后两条：同帧合并为一个。
        var jump = new ShowTimeline([new BannerBeat(banners)], ShowDuration.Normal);
        Assert.Equal("Banner", Start(jump));
        Assert.Equal("Banner", Step(jump, 5000));
        Assert.True(jump.IsFinished);
    }

    [Fact]
    public void 零时长无提示()
    {
        // 首拍是势力（进度 0 即领地段）、末拍横幅：正常时长下创建就有声，零时长下创建与推进都没有任何提示。
        ImmutableArray<SettlementBeat> beats = [new PowerBeat([new PowerChange(P1, 5, 14, 2, 1, 3, 6)]), new BannerBeat([BannerBeat.MatchEndedText])];
        Assert.Equal("Territory", Start(new ShowTimeline(beats, ShowDuration.Normal)));

        var zero = new ShowTimeline(beats, ShowDuration.Zero);
        Assert.True(zero.IsFinished);
        Assert.Equal(string.Empty, Start(zero));
        Assert.Equal(string.Empty, Step(zero, 16));
        Assert.Equal(string.Empty, Step(zero, 5000, fast: true));
        Assert.Equal(string.Empty, Text(SoundCues.Between(ShowMask.Empty, ShowMask.Empty, [])));

        // 首拍是军势揭示（军势 36：第一步二档）：正常时长下创建就有"揭示"，零时长下创建与推进都没有。
        ImmutableArray<SettlementBeat> reveal = [new PowerRevealBeat([RevealEntry.From(Group(P1, 20, 4, 1, 36, "C3"))])];
        Assert.Equal("Reveal:2", Start(new ShowTimeline(reveal, ShowDuration.Normal)));
        var zeroReveal = new ShowTimeline(reveal, ShowDuration.Zero);
        Assert.Equal(string.Empty, Start(zeroReveal));
        Assert.Equal(string.Empty, Step(zeroReveal, 5000));
    }

    [Fact]
    public void 序列确定性()
    {
        // 六种节拍齐全，固定 100 ms 步进两遍：逐帧提示序列相同，且与预期时刻表一致。
        static ImmutableArray<SettlementBeat> Beats() =>
        [
            new PlacementBeat(Pieces(2)),                                                                      // 0–500
            new CaptureBeat([new CapturedPiece(Coord.Parse("D3"), P2, PieceType.Basic)]),                      // 500–1100
            new RelicRevealBeat([new RevealedRelic(Coord.Parse("E5"), "烽火")]),                               // 1100–1700
            new PowerRevealBeat([RevealEntry.From(Group(P1, 5, 2, 1, 10, "C3"))]),                             // 1700–2580：四步 1700 / 1920 / 2140 / 2360，档位一、一、二、三
            new PowerBeat([new PowerChange(P1, 5, 14, 2, 1, 3, 6), new PowerChange(P2, 6, 4, 1, 2, -1, -1)]),  // 2580–3480：领地 2580、军势 2930（+6 二档）
            new BannerBeat([BannerBeat.EliminatedText(P2), BannerBeat.MatchEndedText]),                        // 3480–5080：第 2 条 4280
        ];

        static List<string> Run()
        {
            var timeline = new ShowTimeline(Beats(), ShowDuration.Normal);
            var log = new List<string>();
            string start = Start(timeline);
            if (start.Length > 0)
            {
                log.Add($"0:{start}");
            }

            for (int t = 100; t <= 5100; t += 100)
            {
                string cues = Step(timeline, 100);
                if (cues.Length > 0)
                {
                    log.Add($"{t}:{cues}");
                }
            }

            Assert.True(timeline.IsFinished);
            return log;
        }

        List<string> first = Run();
        Assert.Equal(
            [
                "100:Placement", "300:Placement", "500:Capture", "1100:Relic",
                "1700:Reveal", "2000:Reveal", "2200:Reveal:2", "2400:Reveal:3",
                "2600:Territory", "3000:Group:2", "3500:Banner", "4300:Banner",
            ],
            first);
        Assert.Equal(first, Run());
    }
}
