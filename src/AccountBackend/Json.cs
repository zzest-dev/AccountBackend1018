using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccountBackend;

public sealed class RequestError(string message) : Exception(message);

public static class J
{
    public static void Need(bool value, string message) { if (!value) throw new RequestError(message); }
    public static string S(this JToken? x, string key, string fallback = "") => x?[key]?.Type == JTokenType.String ? (string)x[key]! : fallback;
    public static long I(this JToken? x, string key, long fallback = 0) => x?[key]?.Type == JTokenType.Integer ? (long)x[key]! : fallback;
    public static double D(this JToken? x, string key, double fallback = 0) => Number(x?[key]) ? (double)x![key]! : fallback;
    public static bool Number(JToken? x) => x?.Type is JTokenType.Integer or JTokenType.Float && double.IsFinite((double)x);
    public static bool Bool(this JToken? x, string key) => x?[key]?.Type == JTokenType.Boolean && (bool)x[key]!;
    public static bool Nil(JToken? x) => x == null || x.Type == JTokenType.Null;
    public static bool Keys(JObject x, params string[] keys) => x.Properties().All(p => keys.Contains(p.Name));
    public static TimeProvider Clock { get; set; } = TimeProvider.System;
    public static long Now => Clock.GetUtcNow().ToUnixTimeSeconds();
    public static double Time => Clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000d;
    public static string Random(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string Dump(JToken? value) => value?.ToString(Formatting.None) ?? "null";
    public static string Canon(JToken? x, bool ascii = false)
    {
        Need(x?.Type!=JTokenType.Float || Number(x), "invalid_json_data");
        if (x is JObject o) return "{" + string.Join(", ", o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonConvert.ToString(p.Name, '"', ascii ? StringEscapeHandling.EscapeNonAscii : StringEscapeHandling.Default) + ": " + Canon(p.Value, ascii))) + "}";
        if (x is JArray a) return "[" + string.Join(", ", a.Select(v => Canon(v, ascii))) + "]";
        return JsonConvert.SerializeObject(x, new JsonSerializerSettings { StringEscapeHandling = ascii ? StringEscapeHandling.EscapeNonAscii : StringEscapeHandling.Default });
    }
    public static bool Same(JToken? a, JToken? b)
    {
        if ((Nil(a) || a!.Type == JTokenType.String && (string?)a == "") && (Nil(b) || b!.Type == JTokenType.String && (string?)b == "")) return true;
        if (Number(a) && Number(b)) return Math.Abs((double)a! - (double)b!) <= Math.Max(0.00001, Math.Max(Math.Abs((double)a!), Math.Abs((double)b!)) * 1e-9);
        if (a is JObject x && b is JObject y) return x.Properties().Select(p=>p.Name).Union(y.Properties().Select(p=>p.Name)).All(k=>Same(x[k],y[k]));
        if (a is JArray xx && b is JArray yy) return xx.Count==yy.Count && xx.Zip(yy).All(v=>Same(v.First,v.Second));
        return JToken.DeepEquals(a,b);
    }
    public static JObject Parse(string text) => JObject.Parse(text);
}
