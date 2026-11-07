using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ChallengeBible.Tests.Support;

/// <summary>
/// 페이지를 Edge headless 에서 돌리고, 페이지 안 시험 스크립트가 POST /result 로 보낸 결과를 받는다.
/// Windows 의 Edge 는 --dump-dom 을 표준 출력으로 주지 않아, 작은 서버로 페이지를 주고 결과를 돌려받는다.
/// </summary>
internal static class EdgePage
{
    private const string EdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    public static bool Available => File.Exists(EdgePath);

    public static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ChallengeBible.Web")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "ChallengeBible.Web", "wwwroot");
    }

    /// <summary>실제 페이지 HTML 에 실제 스크립트 · CSS 와 시험 스크립트를 넣는다.</summary>
    public static string Inline(string page, string script, string checks, params string[] extraCss)
    {
        var root = WebRoot();
        page = Regex.Replace(page, "<link[^>]*stylesheet[^>]*>", "");
        page = Regex.Replace(page, "<script[^>]*\\ssrc=[^>]*></script>", "");
        var css = string.Concat(new[] { "site.css" }.Concat(extraCss).Select(f => File.ReadAllText(Path.Combine(root, "css", f))));
        page = page.Replace("</head>", "<style>" + css + "</style></head>");
        return page.Replace("</body>", "<script>" + File.ReadAllText(Path.Combine(root, "js", script)) + "</script><script>" + checks + "</script></body>");
    }

    /// <summary>Edge 는 실행 파일이 곧 끝나고 자식 프로세스가 남는다. 이 시험의 임시 프로필을 쓰는 Edge 만 끈다(쓰고 있는 Edge 는 건드리지 않음).</summary>
    private static void StopEdgeUsing(string profileName)
    {
        var script = "Get-CimInstance Win32_Process -Filter \"name='msedge.exe'\" | Where-Object { $_.CommandLine -like '*" + profileName + "*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";
        var psi = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-Command", script }) psi.ArgumentList.Add(a);
        using var stop = Process.Start(psi);
        stop?.WaitForExit(30000);
    }

    /// <summary>시험 결과 글을 돌려준다. 60초 안에 오지 않으면 null.</summary>
    public static async Task<string?> RunAsync(string page)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var ctx = await listener.GetContextAsync();
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(ctx.Request.InputStream);
                        result.TrySetResult(await reader.ReadToEndAsync());
                    }
                    else if (ctx.Request.Url!.AbsolutePath == "/")
                    {
                        var bytes = Encoding.UTF8.GetBytes(page);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                    }
                    else ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) { /* 끝낼 때 멈춘다 */ }
            catch (ObjectDisposedException) { /* 끝낼 때 멈춘다 */ }
        });

        var dir = Directory.CreateTempSubdirectory("edgepage");
        var psi = new ProcessStartInfo(EdgePath) { UseShellExecute = false };
        foreach (var a in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--window-size=500,1400",
            "--user-data-dir=" + dir.FullName, $"http://127.0.0.1:{port}/" })
            psi.ArgumentList.Add(a);
        using var edge = Process.Start(psi)!;
        try
        {
            var finished = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            return finished == result.Task ? (await result.Task).Trim() : null;
        }
        finally
        {
            try { edge.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* 이미 끝남 */ }
            StopEdgeUsing(dir.Name);
            listener.Stop();
            await server;
            // Edge 가 일부 창을 늦게 닫으면 임시 폴더가 남을 수 있다. 시험 결과와는 상관없다.
            try { await Task.Delay(500); dir.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
