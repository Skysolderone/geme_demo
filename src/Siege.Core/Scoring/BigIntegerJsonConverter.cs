using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siege.Core.Scoring;

/// <summary>
/// 势力 / 军势值（<see cref="BigInteger"/>）的 JSON 读写：System.Text.Json 不自带任意精度整数支持，存档与跑局日志共用这一份。
/// </summary>
/// <remarks>
/// <para>写出：精确十进制整数的 JSON 数字（不加引号、无小数、无指数），位数不限——数值较小时与此前 64 位整数的写法逐字节相同。</para>
/// <para>读入：接受 JSON 数字与十进制整数字符串两种写法；带小数、指数或其他字符一律抛 <see cref="JsonException"/>，
/// 不经过任何浮点类型，读回的值与写出的值逐位一致。</para>
/// </remarks>
public sealed class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string text = reader.TokenType switch
        {
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            _ => throw new JsonException($"势力值必须是整数或整数字符串，实际为 {reader.TokenType}。"),
        };

        return BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger value)
            ? value
            : throw new JsonException($"势力值必须是精确十进制整数，实际为 '{text}'。");
    }

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture), skipInputValidation: false);
    }
}
