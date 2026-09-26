using System.Globalization;
using System.Text;

namespace Siege.Core.Carry;

/// <summary>读取档案的结果类别（carry-in-out「档案缺失、损坏与版本不符的恢复」）。</summary>
public enum CarryProfileLoadStatus
{
    /// <summary>正常读取。</summary>
    Loaded,

    /// <summary>档案不存在，已新建空档案。</summary>
    Created,

    /// <summary>档案损坏：原文件已改名备份，已重置为空档案。</summary>
    CorruptReset,

    /// <summary>档案来自更新的版本：只读，文件不被写回，本局按关闭带入带出进行。</summary>
    NewerVersion,
}

/// <summary>读取档案的结果：类别、档案路径、损坏时的备份路径与原因、版本更新时的文件版本。</summary>
public sealed record CarryProfileLoad(CarryProfileLoadStatus Status, string Path, string? BackupPath = null, string? Reason = null, int? FileVersion = null)
{
    /// <summary>给用户的提示（终端与图形版共用）。</summary>
    public string Message => Status switch
    {
        CarryProfileLoadStatus.Loaded => $"档案：{Path}",
        CarryProfileLoadStatus.Created => $"已新建档案：{Path}",
        CarryProfileLoadStatus.CorruptReset => $"档案损坏，已备份并重置（{Reason}）。备份：{BackupPath}",
        CarryProfileLoadStatus.NewerVersion =>
            $"档案 {Path} 来自更新的版本（格式版本 {FileVersion}，本程序支持 {CarryProfile.CurrentVersion}）：档案保持不变，本局不提供带入、局终不结算。",
        _ => throw new ArgumentOutOfRangeException(nameof(Status), Status, "未知读取结果。"),
    };
}

/// <summary>
/// 档案存取（carry-in-out「本地玩家档案」等，design.md D9）：读、兑换、开局扣除、结算。路径由入口注入；AI 不读写档案。
/// </summary>
/// <remarks>
/// <para>Core 的规则代码不依赖本类；Core 里的文件 I/O 只在这一个类。构造不做任何 I/O——入口可以先解析选项、后决定是否读档。</para>
/// <para>写入先写同目录的 <c>.tmp</c>，再用 <see cref="File.Move(string, string, bool)"/> 覆盖原文件：替换之前中断时原档案保持写入前的状态。
/// 内存中的档案只在写入成功后更新。两个入口同时写同一份档案时后写者覆盖（不加文件锁，design.md Risks）。</para>
/// <para>损坏备份的文件名带本地时间 <c>yyyyMMdd-HHmmss</c>。时钟由入口注入（Core 永不读时钟，determinism.md），只用于这个文件名、不进任何对局或记录；测试注入固定时钟。</para>
/// </remarks>
public sealed class CarryProfileStore
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _beforeReplace;
    private CarryProfile? _profile;
    private bool _readOnly;

    /// <param name="path">档案路径（入口给出：<c>--profile</c> 或 <see cref="DefaultPath"/>）。</param>
    /// <param name="clock">损坏备份文件名用的本地时钟（入口注入；按它自己的时区格式化）。</param>
    public CarryProfileStore(string path, Func<DateTimeOffset> clock)
        : this(path, clock, beforeReplace: null)
    {
    }

    /// <param name="beforeReplace">测试接缝：临时文件写完、替换原文件之前调用（参数为临时文件路径），用来模拟"替换前进程终止"。</param>
    internal CarryProfileStore(string path, Func<DateTimeOffset> clock, Action<string>? beforeReplace)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(clock);
        FilePath = path;
        _clock = clock;
        _beforeReplace = beforeReplace;
    }

    /// <summary>缺省档案路径：用户数据目录下的 <c>Siege/profile.json</c>（Windows 为 <c>%APPDATA%\Siege\profile.json</c>），终端与图形版共用。</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Siege", "profile.json");

    public string FilePath { get; }

    /// <summary>档案来自更新的版本：只读，任何写入都被拒绝。</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>当前档案（内存与磁盘一致）。未读取或只读时抛出。</summary>
    public CarryProfile Current => _profile ?? throw new InvalidOperationException(
        _readOnly ? $"档案 {FilePath} 来自更新的版本，本程序不读取其内容。" : "档案尚未读取（先调用 Load）。");

    /// <summary>
    /// 读取档案：不存在时新建空档案；损坏时先把原文件改名为 <c>&lt;原文件名&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> 备份、再写空档案；
    /// 版本更新时不写任何文件、进入只读。返回结果类别与给用户的提示。
    /// </summary>
    public CarryProfileLoad Load()
    {
        _readOnly = false;
        _profile = null;
        if (!File.Exists(FilePath))
        {
            Save(CarryProfile.Empty);
            return new CarryProfileLoad(CarryProfileLoadStatus.Created, FilePath);
        }

        string text = File.ReadAllText(FilePath, Encoding.UTF8);
        try
        {
            _profile = CarryProfile.Parse(text);
            return new CarryProfileLoad(CarryProfileLoadStatus.Loaded, FilePath);
        }
        catch (CarryProfileVersionException ex)
        {
            _readOnly = true;
            return new CarryProfileLoad(CarryProfileLoadStatus.NewerVersion, FilePath, FileVersion: ex.FileVersion);
        }
        catch (FormatException ex)
        {
            // MUST NOT 在没有备份的情况下覆盖无法解析的档案：先改名备份（备份名已被占用时另取序号，不覆盖先前的备份），再写空档案。
            string backup = BackupPath();
            File.Move(FilePath, backup);
            Save(CarryProfile.Empty);
            return new CarryProfileLoad(CarryProfileLoadStatus.CorruptReset, FilePath, backup, ex.Message);
        }
    }

    /// <summary>
    /// 兑换 1 件补给（carry-in-out「补给兑换」）：补给点不少于价格时扣点、库存 +1 并立即写入档案，返回 <c>true</c>；
    /// 补给点不足时拒绝，<paramref name="refusal"/> 给出原因，档案不变。
    /// </summary>
    public bool TryExchange(SupplyKind kind, out string? refusal)
    {
        CarryProfile profile = Writable();
        int price = Supplies.PriceOf(kind);
        if (profile.Points < price)
        {
            refusal = $"补给点不足（需要 {price}，现有 {profile.Points}）";
            return false;
        }

        Save(profile.WithStock(kind, 1) with { Points = profile.Points - price });
        refusal = null;
        return true;
    }

    /// <summary>
    /// 开局（carry-in-out「开局带入与在途记录」）：从库存扣除带入的补给（未带入则不扣），写入在途记录。
    /// 征召签须已由建局解析出类型（取 <c>MatchFlow.CarryIns</c> 中本机玩家的那一项）。
    /// 残留的在途记录必须先由 <see cref="SettleAbandoned"/> 处理；库存为 0 的补给不能带。
    /// </summary>
    public void Begin(string matchId, CarryIn? carry)
    {
        ArgumentException.ThrowIfNullOrEmpty(matchId);
        CarryProfile profile = Writable();
        if (profile.InFlight is { } residual)
        {
            throw new InvalidOperationException($"档案还有对局 {residual.MatchId} 的在途记录，须先按中途退出结算。");
        }

        if (carry is not null && profile.StockOf(carry.Kind) < 1)
        {
            throw new InvalidOperationException($"{carry.Kind} 库存为 0，不能带入。");
        }

        CarryProfile begun = carry is null ? profile : profile.WithStock(carry.Kind, -1);
        Save(begun with { InFlight = new CarryInFlight(matchId, carry) });
    }

    /// <summary>
    /// 把残留的在途记录按"中途退出"结算（carry-in-out「中途退出与截断」）：带入的补给丢失、带出 0，清除在途记录。
    /// 返回被结算的记录；没有残留时返回 <c>null</c>、不写档案。
    /// </summary>
    public CarryInFlight? SettleAbandoned()
    {
        CarryProfile profile = Writable();
        if (profile.InFlight is not { } abandoned)
        {
            return null;
        }

        Save(profile with { InFlight = null });
        return abandoned;
    }

    /// <summary>
    /// 把一次带出结算写入档案：补给点加上 <see cref="CarryOutResult.Points"/>，<see cref="CarryOutResult.Returned"/> 时把在途记录里的补给还回库存，清除在途记录。
    /// 同一在途记录只结算一次：在途记录已清除或对局标识不匹配时忽略并返回 <c>false</c>。
    /// "未结算"（截断局）不得写入档案；结算里的带入与在途记录不一致即响亮失败。
    /// </summary>
    public bool Settle(string matchId, CarryOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        CarryProfile profile = Writable();
        if (result.Outcome == CarryOutcome.Unsettled)
        {
            throw new ArgumentException("未结算的结果（截断局）不写入档案。", nameof(result));
        }

        if (profile.InFlight is not { } flight || flight.MatchId != matchId)
        {
            return false;
        }

        if (flight.Carry != result.CarryIn)
        {
            throw new ArgumentException(
                $"结算的带入（{result.CarryIn?.ToString() ?? "无"}）与在途记录（{flight.Carry?.ToString() ?? "无"}）不一致。", nameof(result));
        }

        CarryProfile settled = result.Returned && flight.Carry is { } carry ? profile.WithStock(carry.Kind, 1) : profile;
        Save(settled with { Points = profile.Points + result.Points, InFlight = null });
        return true;
    }

    private CarryProfile Writable() => IsReadOnly
        ? throw new InvalidOperationException($"档案 {FilePath} 来自更新的版本，本程序不写回。")
        : Current;

    /// <summary>原子写入：先写同目录临时文件，再替换原文件；成功后才更新内存中的档案。</summary>
    private void Save(CarryProfile profile)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, profile.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _beforeReplace?.Invoke(temporary);
        File.Move(temporary, FilePath, overwrite: true);
        _profile = profile;
    }

    private string BackupPath()
    {
        string stamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string backup = $"{FilePath}.corrupt-{stamp}";
        for (int n = 2; File.Exists(backup); n++)
        {
            backup = $"{FilePath}.corrupt-{stamp}-{n}";
        }

        return backup;
    }
}
