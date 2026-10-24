using System.Reflection;

namespace ChallengeBible.Web.Services;

/// <summary>최근 오류 한 묶음(같은 종류 · 위치 · 첫 줄은 한 줄로, 시안 운영점검). 개인 내용(입력값 · 비밀번호 · 접속 문자열)은 담지 않는다.</summary>
public sealed record ErrorGroup(string Type, string Message, string Where, string LastUrl, DateTime LastUtc, string LastTraceId, int Count, string Stack);

/// <summary>
/// 운영 점검의 최근 오류(확정: 서버 메모리에 최근 50건, 앱이 다시 켜지면 비움). 남기는 것: 주소(물음표 뒤는 뺌) · 시각 · 요청 번호 ·
/// 오류 종류 · 첫 줄(300자까지) · 우리 코드 위치. 요청 본문 · 쿼리 문자열 · 사용자 정보는 남기지 않는다.
/// </summary>
public sealed class ErrorLog(TimeProvider time)
{
    public const int Capacity = 50;
    private readonly LinkedList<(string Key, string Type, string Message, string Where, string Url, DateTime Utc, string TraceId, string Stack)> _items = new();
    private readonly Lock _gate = new();

    public void Record(HttpContext http, Exception ex)
    {
        if (ex is OperationCanceledException && http.RequestAborted.IsCancellationRequested) return;   // 사용자가 화면을 떠남 — 오류 아님
        var type = ex.GetType().Name;
        var message = FirstLine(ex.Message);
        var stack = OurFrames(ex);
        var where = stack.Split('\n').Skip(1).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0)?.Replace("at ", "") ?? "";
        var url = $"{http.Request.Method} {http.Request.Path}";   // 쿼리 문자열은 남기지 않는다
        lock (_gate)
        {
            _items.AddFirst(($"{type}|{where}|{message}", type, message, where, url, time.GetUtcNow().UtcDateTime, http.TraceIdentifier, stack));
            while (_items.Count > Capacity) _items.RemoveLast();
        }
    }

    /// <summary>최근 것부터, 같은 오류는 묶어 횟수와 마지막 발생을 보여 준다.</summary>
    public IReadOnlyList<ErrorGroup> Groups()
    {
        lock (_gate)
            return _items.GroupBy(i => i.Key)
                .Select(g => { var last = g.First(); return new ErrorGroup(last.Type, last.Message, last.Where, last.Url, last.Utc, last.TraceId, g.Count(), last.Stack); })
                .OrderByDescending(g => g.LastUtc).ToList();
    }

    private static string FirstLine(string s)
    {
        var line = (s ?? "").Split('\n')[0].Trim();
        return line.Length > 300 ? line[..300] + "…" : line;
    }

    /// <summary>첫 줄(종류: 메시지) + 우리 코드(ChallengeBible) 줄만 12줄까지 — 라이브러리 깊은 줄과 파일 경로는 뺀다.</summary>
    private static string OurFrames(Exception ex)
    {
        var lines = (ex.ToString() ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var ours = lines.Skip(1).Where(l => l.TrimStart().StartsWith("at ChallengeBible", StringComparison.Ordinal))
            .Select(l => { var i = l.IndexOf(" in ", StringComparison.Ordinal); return i > 0 ? l[..i] : l; }).Take(12);
        return string.Join('\n', new[] { $"{ex.GetType().FullName}: {FirstLine(ex.Message)}" }.Concat(ours));
    }
}

/// <summary>운영 점검의 가동 · 버전: 앱이 켜진 시각, 빌드 때 넣은 커밋 번호(InformationalVersion "+커밋"), 빌드 시각.</summary>
public sealed class AppInfo(TimeProvider time)
{
    public DateTime StartedUtc { get; } = time.GetUtcNow().UtcDateTime;

    public string Commit { get; } = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } v
        && v.IndexOf('+') is var i and >= 0 ? v[(i + 1)..] : "";

    public DateTime BuiltUtc { get; } = File.GetLastWriteTimeUtc(Assembly.GetExecutingAssembly().Location);
}

public static class ErrorLogExtensions
{
    /// <summary>처리되지 않은 오류를 최근 오류에 남기고 그대로 다시 던진다(오류 화면 · 응답은 기존대로).</summary>
    public static IApplicationBuilder UseErrorLog(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            try { await next(); }
            catch (Exception ex)
            {
                http.RequestServices.GetRequiredService<ErrorLog>().Record(http, ex);
                throw;
            }
        });
}
