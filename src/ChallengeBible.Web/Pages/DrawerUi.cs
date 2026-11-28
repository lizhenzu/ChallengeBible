using System.Net;
using Microsoft.AspNetCore.Html;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// ☰ 메뉴판 한 줄(확정 시안 부서메뉴 · 플랫폼관리메뉴, 2026-11-18): 색 아이콘 + 이름 + 한 줄 설명 + ›. 부서 홈 · 부서 관리 · 플랫폼이 같이 쓴다.
/// tone: g 초록 · b 파랑 · o 주황 · p 분홍 · v 보라 · s 회색 · r 빨강(되돌릴 수 없는 도구).
/// </summary>
/// <summary>통독 · 필사 ☰ 메뉴(_B2Drawer) 재료. Menu = (주소, 이름, 아이콘 이름, 현재 위치 키).</summary>
public sealed record B2DrawerModel(ChallengeBible.Web.Tenancy.TenantContext Tenant, string Name, string Home, string CurKey,
    (string Href, string Label, string Icon, string Key)[] Menu, (string Key, string Label)[]? Themes, string? Theme);

public static class DrawerUi
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["ot"] = """<path d="M4 5a2 2 0 0 1 2-2h13v16H6a2 2 0 0 0-2 2z"/><path d="M4 19V5"/>""",
        ["nt"] = """<path d="M4 5a2 2 0 0 1 2-2h13v16H6a2 2 0 0 0-2 2z"/><path d="M4 19V5"/><path d="M12 7v7M9 10h6"/>""",
        ["bookmark"] = """<path d="M6 3h12v18l-6-4-6 4z"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>""",
        ["copynt"] = """<path d="M4 20l4-1 11-11-3-3L5 16z"/><path d="M14 6l3 3"/><path d="M19 15v6M16 18h6"/>""",
        ["trophy"] = """<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0z"/><path d="M7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3"/>""",
        ["recite"] = """<path d="M7 17c-2 0-3-1.3-3-3 0-3 2-5.5 5-6.5M17 17c-2 0-3-1.3-3-3 0-3 2-5.5 5-6.5"/>""",
        ["pin"] = """<path d="M12 21s-7-6.2-7-11.5A7 7 0 0 1 19 9.5C19 14.8 12 21 12 21z"/><circle cx="12" cy="9.5" r="2.5"/>""",
        ["mission"] = """<path d="M4 5h6a2 2 0 0 1 2 2v12a2 2 0 0 0-2-2H4z"/><path d="M20 5h-6a2 2 0 0 0-2 2v12a2 2 0 0 1 2-2h6z"/><path d="m15 9 1.5 1.5L19 8"/>""",
        ["reading"] = """<path d="M2 6s3-2 10 0 10 0 10 0v13s-3 2-10 0-10 0-10 0z"/><path d="M12 6v13"/>""",
        ["copy"] = """<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>""",
        ["pray"] = """<path d="M19 14c1.5-1.5 3-3.2 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.8 0-3 .5-4.5 2-1.5-1.5-2.7-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4 3 5.5l7 7z"/>""",
        ["msg"] = """<path d="M4 4h16v12H8l-4 4z"/>""",
        ["todo"] = """<rect x="4" y="4" width="16" height="16" rx="3"/><path d="m8 12 3 3 5-6"/>""",
        ["photo"] = """<rect x="3" y="5" width="18" height="15" rx="2"/><circle cx="9" cy="10" r="2"/><path d="m21 16-5-5-8 8"/>""",
        ["att"] = """<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 11h18"/><path d="m9 16 2 2 4-4"/>""",
        ["book"] = """<path d="M4 4.5A2.5 2.5 0 0 1 6.5 2H20v17H6.5A2.5 2.5 0 0 0 4 21.5z"/><path d="M4 21.5A2.5 2.5 0 0 1 6.5 19H20v3H6.5A2.5 2.5 0 0 1 4 21.5z"/>""",
        ["gear"] = """<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>""",
        ["move"] = """<path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5"/>""",
        ["admin"] = """<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1 7 17M17 7l2.1-2.1"/>""",
        ["people"] = """<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0"/><path d="M16 4.5a3.5 3.5 0 0 1 0 7M21.5 20a6.5 6.5 0 0 0-4-6"/>""",
        ["platform"] = """<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>""",
        ["user"] = """<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>""",
        ["home"] = """<path d="M3 11 12 3l9 8"/><path d="M5 10v10h14V10"/>""",
        ["join"] = """<circle cx="12" cy="12" r="9"/><path d="M12 8v8M8 12h8"/>""",
        ["dept"] = """<path d="M3 21h18M5 21V8l7-5 7 5v13"/><path d="M9 21v-6h6v6"/>""",
        ["upload"] = """<path d="M12 16V4M7 9l5-5 5 5"/><path d="M4 20h16"/>""",
        ["key"] = """<circle cx="8" cy="15" r="4"/><path d="m11 12 9-9M17 6l3 3"/>""",
        ["family"] = """<circle cx="8" cy="7" r="3"/><circle cx="17" cy="9" r="2.5"/><path d="M2 20a6 6 0 0 1 12 0M14 20a4 4 0 0 1 8 0"/>""",
        ["word"] = """<path d="M4 5h6a2 2 0 0 1 2 2v12a2 2 0 0 0-2-2H4z"/><path d="M20 5h-6a2 2 0 0 0-2 2v12a2 2 0 0 1 2-2h6z"/>""",
        ["quiz"] = """<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14M12 17.5v.01"/>""",
        ["common"] = """<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 10h18M9 4v16"/>""",
        ["input"] = """<path d="M4 4h16v16H4z"/><path d="M8 9h8M8 13h5"/>""",
        ["ai"] = """<path d="M12 3l1.8 4.2L18 9l-4.2 1.8L12 15l-1.8-4.2L6 9l4.2-1.8z"/><path d="M19 15l.9 2.1L22 18l-2.1.9L19 21l-.9-2.1L16 18l2.1-.9z"/>""",
        ["bible"] = """<path d="M5 4h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3z"/><path d="M12 8v6M9.5 10.5h5"/>""",
        ["chart"] = """<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>""",
        ["rules"] = """<path d="M8 6h13M8 12h13M8 18h13"/><path d="M3 6h.01M3 12h.01M3 18h.01"/>""",
        ["cake"] = """<path d="M4 21V12a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v9"/><path d="M4 16c2 0 2-1.5 4-1.5s2 1.5 4 1.5 2-1.5 4-1.5 2 1.5 4 1.5"/><path d="M3 21h18M12 10V7"/><path d="M12 7c-1-1-1-2.5 0-4 1 1.5 1 3 0 4z"/>""",
        ["gift"] = """<rect x="3" y="8" width="18" height="13" rx="2"/><path d="M12 8v13M3 12h18"/><path d="M12 8S10 3 7.5 4.5 9 8 12 8zM12 8s2-5 4.5-3.5S15 8 12 8z"/>""",
        ["search"] = """<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>""",
        ["pulse"] = """<path d="M3 12h4l3-8 4 16 3-8h4"/>""",
        ["reset"] = """<path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/>""",
        ["sql"] = """<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>""",
        ["flask"] = """<path d="M9 3h6M10 3v6L4 19a1.5 1.5 0 0 0 1.3 2h13.4A1.5 1.5 0 0 0 20 19L14 9V3"/>""",
        ["login"] = """<path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/><path d="M10 17l5-5-5-5M15 12H3"/>""",
        ["cls"] = """<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>""",
        ["up"] = """<path d="M12 19V5M5 12l7-7 7 7"/>""",
        ["read"] = """<path d="M2 6s3-2 10 0 10 0 10 0v13s-3 2-10 0-10 0-10 0z"/><path d="M12 6v13"/>""",
        ["season"] = """<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 11h18"/>""",
        ["palette"] = """<circle cx="12" cy="12" r="9"/><circle cx="8" cy="10" r="1.2"/><circle cx="12" cy="7.5" r="1.2"/><circle cx="16" cy="10" r="1.2"/><path d="M12 21a3 3 0 0 1 0-6h2"/>""",
        // 성경미션 ☰ 메뉴(민트 통일, 2026-11-28 시안 Menu)
        ["m-home"] = """<path d="M3 10l9-7 9 7v10a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1z"/>""",
        ["m-main"] = """<rect x="3.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.5"/>""",
        ["m-book"] = """<path d="M12 6c-1.8-1.4-4-2-8-2v13c4 0 6.2.6 8 2 1.8-1.4 4-2 8-2V4c-4 0-6.2.6-8 2zM12 6v13"/>""",
        ["m-quiz"] = """<circle cx="12" cy="12" r="9"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.4-1 .9-1 1.7M12 17v.5"/>""",
        ["m-pray"] = """<path d="M12 20s-8-4.6-8-10.2A4.3 4.3 0 0 1 12 7a4.3 4.3 0 0 1 8 2.8C20 15.4 12 20 12 20z"/>""",
        ["m-recite"] = """<path d="M9 7C6 8 5 10 5 13h4v4H4v-4M19 7c-3 1-4 3-4 6h4v4h-5v-4"/>""",
        ["m-cal"] = """<rect x="3.5" y="5" width="17" height="15" rx="2"/><path d="M3.5 10h17M8 3v4M16 3v4"/>""",
        ["m-score"] = """<path d="M4 20V10M10 20V4M16 20v-8M21 20H3"/>""",
        ["m-team"] = """<circle cx="9" cy="8" r="3.5"/><circle cx="17" cy="9" r="2.5"/><path d="M2.5 20c.8-3.5 3.4-5.5 6.5-5.5s5.7 2 6.5 5.5M15.5 14.6c2.8-.3 5 1.4 6 4.4"/>""",
        ["bell"] = """<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10 21h4"/>""",
    };

    /// <summary>Admin = 관리자만 보는 메뉴 딱지 글자(부서 ☰ 메뉴판, 2026-11-21 사용자 "관리자에게만 관리").</summary>
    public const string Admin = "관리";

    /// <summary>가장 중 표시(R-366, 2026-11-22 사용자): 이름 뒤 "(가)" 하나. 보라 띠는 없앴다.</summary>
    public const string Ga = "<em class=\"imp-ga\" title=\"가장 중\">(가)</em>";

    public static IHtmlContent Row(string href, string icon, string tone, string title, string desc, bool current, bool dj = false, int count = 0, string? attr = null, string? badge = null)
    {
        var t = dj ? $"<em class=\"pd-pg\">도전!!!</em>{WebUtility.HtmlEncode(title)}" : WebUtility.HtmlEncode(title);
        if (badge is not null) t += $" <em class=\"tdr-k\" title=\"관리자만 보는 메뉴\">{WebUtility.HtmlEncode(badge)}</em>";   // "관리" 딱지(2026-11-21 사용자)
        if (count > 0) t += $" <em class=\"tdr-c num\" aria-label=\"기다리는 요청 {count}건\">{count}</em>";
        return new HtmlString(
            $"<a class=\"tdr-r\" href=\"{WebUtility.HtmlEncode(href)}\"{(current ? " aria-current=\"page\"" : "")}{(attr is null ? "" : " " + attr)}><i class=\"tdr-i t-{tone}\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{Icons[icon]}</svg></i>"
            + $"<span><b>{t}</b><small>{WebUtility.HtmlEncode(desc)}</small></span><u aria-hidden=\"true\">›</u></a>");
    }

    /// <summary>
    /// 깔끔한 한 줄(2026-11-22 확정 시안 메뉴-깔끔, ADR 0127): 색 타일 · 설명 없이 파란 선 아이콘 + 제목만.
    /// 부서 ☰ 메뉴가 쓴다. 관리자만 보는 줄에는 작은 "관리" 딱지.
    /// </summary>
    public static IHtmlContent Line(string href, string icon, string title, bool current = false, string? badge = null, string? attr = null)
    {
        var b = badge is null ? "" : $"<em class=\"tdr2-k\">{WebUtility.HtmlEncode(badge)}</em>";
        return new HtmlString(
            $"<a class=\"tdr2-r\" href=\"{WebUtility.HtmlEncode(href)}\"{(current ? " aria-current=\"page\"" : "")}{(attr is null ? "" : " " + attr)}>"
            + $"<i class=\"tdr2-i\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{Icons[icon]}</svg></i>"
            + $"<span>{WebUtility.HtmlEncode(title)}</span>{b}</a>");
    }

    /// <summary>
    /// ☰ 메뉴 「지금 누구인지」 카드(기준표 8판 · 공통 1, 2026-11-24): 로고 아래, 누르면 내 계정. 아바타 40px · 이름님 · 소속 한 줄 · ›.
    /// </summary>
    public static IHtmlContent Me(string? name, string? photo, string sub, bool imp = false)
    {
        name ??= "";
        var av = photo is { } ph ? $"<img src=\"/account/photo?handler=img&amp;v={WebUtility.HtmlEncode(ph)}\" alt=\"\" />" : $"<span aria-hidden=\"true\">{WebUtility.HtmlEncode(name.Length > 0 ? name[..1] : "·")}</span>";
        return new HtmlString($"<a class=\"tdr2-me\" href=\"/account\" aria-label=\"내 계정\"><i class=\"tdr2-av\">{av}</i><span class=\"tdr2-who\"><b>{WebUtility.HtmlEncode(Ui.Nim(name))}{(imp ? " " + Ga : "")}</b><small>{WebUtility.HtmlEncode(sub)}</small></span><u aria-hidden=\"true\">›</u></a>");
    }

    /// <summary>
    /// 설명 줄이 있는 한 줄(관리 · 플랫폼 ☰ 에서만 허용, 기준표 8판): 파란 선 아이콘 + 이름 + 12px 설명, 높이 56px. count 는 기다리는 수 배지.
    /// </summary>
    public static IHtmlContent RowD(string href, string icon, string title, string desc, bool current, int count = 0, string? attr = null)
    {
        var b = count > 0 ? $" <em class=\"tdr2-k\" aria-label=\"기다리는 요청 {count}건\">{count}</em>" : "";
        return new HtmlString(
            $"<a class=\"tdr2-r tdr2-d\" href=\"{WebUtility.HtmlEncode(href)}\"{(current ? " aria-current=\"page\"" : "")}{(attr is null ? "" : " " + attr)}>"
            + $"<i class=\"tdr2-i\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{Icons[icon]}</svg></i>"
            + $"<span class=\"tdr2-t\"><b>{WebUtility.HtmlEncode(title)}{b}</b>{(desc.Length > 0 ? $"<small>{WebUtility.HtmlEncode(desc)}</small>" : "")}</span></a>");
    }

    /// <summary>
    /// 맨 위: 사진(없으면 이름 첫 글자) · 이름 · 소속 줄 · [내 정보 · 사진 변경] · 닫기.
    /// role 을 주면(부서 ☰, 2026-11-21 확정 시안 메뉴판-정리) 소속은 교회 · 부서 한 줄 + 역할 딱지 한 줄이고, 링크 글자는 link.
    /// </summary>
    public static IHtmlContent Head(string? name, string? photo, string sub, string? role = null, bool adminRole = false, string link = "내 정보 · 사진 변경 ›", bool imp = false)
    {
        name ??= "";
        var av = photo is { } ph ? $"<img src=\"/account/photo?handler=img&amp;v={WebUtility.HtmlEncode(ph)}\" alt=\"\" />" : $"<span aria-hidden=\"true\">{WebUtility.HtmlEncode(name.Length > 0 ? name[..1] : "?")}</span>";
        var pill = string.IsNullOrEmpty(role) ? "" : $"<span class=\"tdr-role{(adminRole ? " tdr-role-a" : "")}\">{WebUtility.HtmlEncode(role)}</span>";
        var ga = imp ? $" {Ga}" : "";   // 가장 중(2026-11-22 사용자)
        return new HtmlString($"<div class=\"tdr-hd\"><a class=\"tdr-av\" href=\"/account\" aria-label=\"내 계정\">{av}</a><div class=\"tdr-who\"><b>{WebUtility.HtmlEncode(Ui.Nim(name))}{ga}</b>"
            + $"<small class=\"pd-dept\">{WebUtility.HtmlEncode(sub)}</small>{pill}<a href=\"/account\">{WebUtility.HtmlEncode(link)}</a></div><button type=\"button\" class=\"pd-x\" aria-label=\"메뉴 닫기\" data-pnav-close>✕</button></div>");
    }
}
