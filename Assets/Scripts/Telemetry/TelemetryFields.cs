using System.Globalization;
using System.Text;

/// <summary>
/// 极简 JSON 对象构造器（只用于 metadata）。
/// 不引第三方 JSON 库，避免额外的程序集依赖；每条事件一次分配，量小无所谓。
/// 用法：new TelemetryFields().Str("scene", "S1").Num("x", 1.5f).Int("jumps", 3)
/// </summary>
public sealed class TelemetryFields
{
    private readonly StringBuilder sb = new StringBuilder(192);

    public TelemetryFields()
    {
        sb.Append('{');
    }

    public TelemetryFields Str(string key, string value)
    {
        Key(key);
        sb.Append('"').Append(Escape(value)).Append('"');
        return this;
    }

    public TelemetryFields Num(string key, float value)
    {
        Key(key);
        if (float.IsNaN(value) || float.IsInfinity(value)) sb.Append('0');
        else sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryFields Int(string key, int value)
    {
        Key(key);
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryFields Long(string key, long value)
    {
        Key(key);
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public TelemetryFields Bool(string key, bool value)
    {
        Key(key);
        sb.Append(value ? "true" : "false");
        return this;
    }

    /// <summary>直接塞一段已经合法的 JSON（高级用法，注意自己保证合法）</summary>
    public TelemetryFields Raw(string key, string rawJson)
    {
        Key(key);
        sb.Append(string.IsNullOrEmpty(rawJson) ? "null" : rawJson);
        return this;
    }

    /// <summary>闭合对象并返回 JSON 文本</summary>
    public string Build()
    {
        sb.Append('}');
        return sb.ToString();
    }

    private void Key(string key)
    {
        if (sb.Length > 1) sb.Append(',');
        sb.Append('"').Append(Escape(key)).Append("\":");
    }

    /// <summary>按 JSON 规范转义（控制字符、引号、反斜杠）</summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        StringBuilder outSb = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            string replacement = null;

            switch (c)
            {
                case '"': replacement = "\\\""; break;
                case '\\': replacement = "\\\\"; break;
                case '\n': replacement = "\\n"; break;
                case '\r': replacement = "\\r"; break;
                case '\t': replacement = "\\t"; break;
                case '\b': replacement = "\\b"; break;
                case '\f': replacement = "\\f"; break;
                default:
                    if (c < ' ') replacement = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                    break;
            }

            if (replacement != null)
            {
                if (outSb == null)
                {
                    outSb = new StringBuilder(s.Length + 16);
                    outSb.Append(s, 0, i);
                }
                outSb.Append(replacement);
            }
            else if (outSb != null)
            {
                outSb.Append(c);
            }
        }

        return outSb == null ? s : outSb.ToString();
    }
}
