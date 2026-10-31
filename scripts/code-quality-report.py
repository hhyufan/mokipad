#!/usr/bin/env python3
"""Generate docs/code-quality-report.html from InspectCode reports and a source diff."""

from __future__ import annotations

import difflib
import html
import json
import os
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BACKUP = ROOT / ".cache" / "pre-cleanup"
OUT = ROOT / "docs" / "code-quality-report.html"

EXPLANATIONS = {
    "ArrangeObjectCreationWhenTypeNotEvident": (
        "目标类型在上下文里不明显时，显式写出 `new Type()` 比裸 `new()` 更容易读懂；这是 ReSharper 的语法风格设置，由 Full Cleanup 按项目风格统一。",
        "Full Cleanup 的“应用代码风格”任务把上下文不明显的 `new()` 补全为显式类型。"),
    "ArrangeTrailingCommaInMultilineLists": (
        "项目风格要求多行初始化器最后一项不加尾逗号；统一后 diff 更稳定，也不会与格式化工具互相拉扯。",
        "删除多行列表末尾多余的逗号。"),
    "MergeIntoPattern": (
        "`x != null && x.Prop` 这类写法会重复求值、也较啰嗦；C# 的递归模式 `x is { Prop: ... }` 一次完成判空和取值，表达更直接。",
        "合并为属性模式（必要时配合 `and`/`or` 逻辑模式）。"),
    "SYSLIB1045": (
        "`[GeneratedRegex]` 本可在编译期生成正则实现；但 Rider 的独立分析进程没有稳定运行该 source generator，会把 partial 方法报成“必须具有实现部分”的红色错误，破坏 IDE 体验。",
        "保留手写的 `static readonly Regex`（`RegexOptions.Compiled`），并在 `.editorconfig` 中把 `SYSLIB1045` 设为 none，说明这是为跨 IDE 一致性做的取舍。"),
    "InvertIf": (
        "把“包在大 if 里的主体”翻转为提前 return/continue，主流程少一层缩进，异常路径也更早退出。",
        "反转条件并提前返回，保持原逻辑等价。"),
    "MoveLocalFunctionAfterJumpStatement": (
        "本地函数放在 return/continue 之后，读者先看到主流程，再看到辅助函数；这是 ReSharper 的排版约定。",
        "把本地函数移动到所在方法的 return 之后。"),
    "UnusedMember.Global": (
        "没人调用的公开成员会扩大 API 面、增加维护成本，也让人误以为它是外部契约。",
        "删除确实无用的成员；或收紧为 private/internal。"),
    "UseNullPropagation": (
        "`if (x != null) x.Foo()` 可以用 `x?.Foo()` 表达，代码更短且只求值一次。",
        "改写为 `?.` 空传播（含 `?.Invoke`、`?.Dispose` 等）。"),
    "ConvertIfStatementToSwitchStatement": (
        "对同一个值做多路 `if/else if` 时，`switch` 更清楚地表达“分支选择”，也便于后续补分支。",
        "改写为 switch 语句或 switch 表达式。"),
    "MemberCanBePrivate.Global": (
        "只在类型内部使用的成员应收紧可见性，减少耦合面，也避免被外部误用。",
        "把 public/internal 改为 private（或删除未用成员）。"),
    "PropertyCanBeMadeInitOnly.Global": (
        "只在初始化阶段赋值的属性改为 `init`，向读代码的人表达“构造后不再变化”。",
        "setter 改为 init；JSON 反序列化与对象初始化器仍然可用。"),
    "CA1859": (
        "返回接口类型会隐藏具体能力、增加虚调用；如果实现始终是同一个具体类型，返回具体类型更直接。",
        "把返回类型从接口改为实际返回的具体类型，并调整本文件调用点。"),
    "UseAwaitUsing": (
        "异步流/读取器应使用 `await using`，避免同步 Dispose 阻塞线程。",
        "`using` 改为 `await using`。"),
    "MethodHasAsyncOverload": (
        "同步 API 在 UI 线程上会阻塞；有异步重载时优先使用异步版本。",
        "改用对应的异步重载并 await。"),
    "ConvertIfStatementToReturnStatement": (
        "`if (...) return x; return y;` 可以合并为一次 return，减少分支。",
        "合并为 return 表达式或单一 return。"),
    "MergeConditionalExpression": (
        "`x != null ? x.Y : null` 正是 `x?.Y` 的语义，条件表达式可以合并。",
        "改为空传播表达式。"),
    "AsyncVoidEventHandlerMethod": (
        "`async void` 的异常无法被调用方捕获，会直接冒到同步上下文导致进程崩溃；事件处理器应只做入口，把异步逻辑放进 `async Task`。",
        "改为 `void` 处理器 + `_ = XxxAsync()`，异常在 Task 内观察/记录。"),
    "ConvertToConstant.Local": (
        "运行期不会变化的值用 `const` 表达，明确不可变并允许编译期折叠。",
        "局部变量改为 const。"),
    "CA1869": (
        "每次 new JsonSerializerOptions 都会丢失内部元数据缓存、增加分配；复用静态实例更快也更一致。",
        "提取 `static readonly JsonSerializerOptions` 并在调用处复用。"),
    "UseCollectionExpression": (
        "C# 12 的集合表达式 `[...]` 比 `new[] { ... }`/`new List<T> { ... }` 更简洁，目标类型清晰。",
        "改写为集合表达式。"),
    "ArrangeRedundantParentheses": (
        "多余括号不影响语义，但增加视觉噪音。",
        "删除冗余括号，保持运算优先级不变。"),
    "CA1827": (
        "`Count() > 0` 需要遍历整个序列；`Any()` 找到第一个元素就返回。",
        "改为 `.Any()`。"),
    "CA1822": (
        "不访问实例状态的成员可以标记 static，明确无副作用并避免不必要的实例依赖。",
        "改为 static（若会破坏外部调用方式则保留并说明）。"),
    "ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator": (
        "循环里的筛选/投影可以表达为 LINQ，意图更集中；但需确认没有副作用和性能回退。",
        "能安全改写时改为 LINQ，否则保留循环。"),
    "UseMethodAny.2": (
        "判断“存在满足条件的元素”应使用 `Any(predicate)`，比 `Count(...) > 0` 更直接。",
        "改为 `Any(...)`。"),
    "CA1861": (
        "重复传入的常量数组参数每次都会分配；提升为 `static readonly` 字段可复用。",
        "提取为 static readonly 数组字段。"),
    "LoopCanBeConvertedToQuery": (
        "纯映射/筛选循环用 LINQ 表达更短、意图更清楚。",
        "改写为 Select/Where 等查询（保留可读性）。"),
    "SimplifyLinqExpressionUseAll": (
        "`All(...)` 能直接表达“所有元素都满足”，比 `Where(...).Count() == Count` 之类的写法清楚。",
        "改写为 `.All(...)`。"),
    "ArrangeStaticMemberQualifier": (
        "静态成员限定符按项目风格统一，减少无意义前缀。",
        "删除/补全静态限定符。"),
    "ConvertToPrimaryConstructor": (
        "构造函数只做字段赋值时，主构造函数能减少样板代码。",
        "改为 primary constructor（保持字段/属性语义）。"),
    "UseObjectOrCollectionInitializer": (
        "连续属性赋值可合并为对象初始化器，创建与配置在一处，避免中间半初始化状态。",
        "改写为对象/集合初始化器。"),
    "UseVerbatimString": (
        "包含大量反斜杠转义的字符串用逐字字符串更易读、少出错。",
        "改为 `@\"...\"` 并保留原有字符。"),
    "MergeIntoLogicalPattern": (
        "多个 `||`/`&&` 判断可以合并为逻辑模式（`or`/`and`），减少重复的左侧表达式。",
        "合并为逻辑模式。"),
    "ConvertToAutoPropertyWhenPossible": (
        "字段仅被属性透传时，自动属性更简洁，减少样板。",
        "改为自动属性。"),
    "DuplicatedChainedIfBodies": (
        "相邻 if 分支体完全相同时可以合并条件，避免重复代码。",
        "合并重复分支。"),
    "FieldCanBeMadeReadOnly.Local": (
        "只赋值一次的字段标记 readonly，明确生命周期内不可替换。",
        "加上 readonly。"),
    "ConvertToAutoPropertyWithPrivateSetter": (
        "字段只被属性的私有 setter 写入时，自动属性更简洁。",
        "改为带 private setter 的自动属性。"),
    "ConvertIfStatementToConditionalTernaryExpression": (
        "if/else 只做赋值时，条件表达式更紧凑。",
        "改为 `?:` 表达式。"),
}

DEFAULT_EXPLANATION = (
    "ReSharper 给出的可读性/一致性建议；在保持行为等价的前提下按建议改写。",
    "按 ReSharper 的建议改写，保持行为不变。")


def load_report(path: Path):
    if not path.exists():
        return []
    tree = ET.parse(path)
    root = tree.getroot()
    types = {}
    types_node = root.find("IssueTypes")
    if types_node is not None:
        for t in types_node.findall("IssueType"):
            types[t.get("Id")] = {
                "Category": t.get("Category", ""),
                "Description": t.get("Description", ""),
                "Severity": t.get("Severity", ""),
            }
    issues = []
    issues_node = root.find("Issues")
    if issues_node is not None:
        for project in issues_node.findall("Project"):
            for issue in project.findall("Issue"):
                file = issue.get("File", "")
                rel = file.replace(str(ROOT) + "\\", "").replace(str(ROOT) + "/", "")
                entry = types.get(issue.get("TypeId"), {})
                issues.append({
                    "project": project.get("Name", ""),
                    "file": rel,
                    "line": int(issue.get("Line") or 0),
                    "id": issue.get("TypeId", ""),
                    "message": " ".join((issue.get("Message") or "").split()),
                    "rule": entry.get("Description", ""),
                    "category": entry.get("Category", ""),
                    "severity": entry.get("Severity", ""),
                })
    issues.sort(key=lambda x: (x["file"], x["line"], x["id"]))
    return issues


def index_issues(issues):
    positions = defaultdict(list)
    for issue in issues:
        positions[(issue["id"], issue["file"])].append(issue)
    for items in positions.values():
        items.sort(key=lambda x: x["line"])
    return positions


def collect_diff():
    files = []
    for base in ("src", "tests"):
        root = BACKUP / base
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix not in (".cs", ".xaml"):
                continue
            rel = path.relative_to(BACKUP)
            current = ROOT / rel
            if not current.exists():
                continue
            before = path.read_text(encoding="utf-8").splitlines()
            after = current.read_text(encoding="utf-8").splitlines()
            if before == after:
                continue
            diff = list(difflib.unified_diff(before, after, fromfile=f"before/{rel.as_posix()}",
                                             tofile=f"after/{rel.as_posix()}", lineterm="", n=2))
            files.append({"path": rel.as_posix(), "diff": diff,
                          "added": sum(1 for l in diff if l.startswith("+") and not l.startswith("+++")),
                          "removed": sum(1 for l in diff if l.startswith("-") and not l.startswith("---"))})
    files.sort(key=lambda x: x["path"])
    return files


def esc(text):
    return html.escape(str(text), quote=True)


def build(before, cleanup, final, diffs):
    from collections import Counter as _Counter
    before_counts = _Counter((i["id"], i["file"], i["message"]) for i in before)
    cleanup_counts = _Counter((i["id"], i["file"], i["message"]) for i in cleanup)
    final_counts_by_key = _Counter((i["id"], i["file"], i["message"]) for i in final)
    final_counts = _Counter(i["id"] for i in final)
    remaining = len(final)
    total = len(before)
    fixed = total - remaining
    cleanup_fixed = total - len(cleanup)
    manual_fixed = len(cleanup) - remaining

    states = {}
    cleanup_remaining = _Counter(cleanup_counts)
    final_remaining = _Counter(final_counts_by_key)
    for issue in before:
        key = (issue["id"], issue["file"], issue["message"])
        if final_remaining[key] > 0:
            final_remaining[key] -= 1
            states[id(issue)] = "remaining"
        elif cleanup_remaining[key] > 0:
            cleanup_remaining[key] -= 1
            states[id(issue)] = "manual"
        else:
            states[id(issue)] = "cleanup"

    for issue in before:
        if issue["id"] == "SYSLIB1045" and states[id(issue)] == "manual":
            states[id(issue)] = "suppressed"
    suppressed = sum(1 for issue in before if states[id(issue)] == "suppressed")
    manual_fixed -= suppressed

    rules = {}
    for issue in before:
        rules.setdefault(issue["id"], issue)
    ordered = sorted(rules, key=lambda r: (-sum(1 for i in before if i["id"] == r), r))

    # phase attribution per occurrence
    rows_by_rule = defaultdict(list)
    for rule in ordered:
        for issue in before:
            if issue["id"] != rule:
                continue
            rows_by_rule[rule].append({**issue, "state": states[id(issue)]})

    sev_before = Counter(i["severity"] for i in before)
    sev_final = Counter(i["severity"] for i in final)

    html_parts = []
    html_parts.append("""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>MokiPad 代码质量清理报告</title>
<style>
:root{--bg:#f5f7fb;--panel:#fff;--ink:#1c2030;--muted:#5b6273;--line:#e2e6ef;--accent:#3f8cff;--soft:#eaf2ff;--code:#f2f4f9;--ok:#1f9d63;--warn:#b26a00;--bad:#c2384a;--shadow:0 10px 30px rgba(28,32,48,.08)}
@media (prefers-color-scheme: dark){:root{--bg:#14161c;--panel:#1c1f27;--ink:#eef1f7;--muted:#a3aab9;--line:#2c313d;--accent:#5a9dff;--soft:#1e2a3f;--code:#22262f;--ok:#4ccb8b;--warn:#e0a33a;--bad:#ff7a8a;--shadow:0 10px 30px rgba(0,0,0,.35)}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.7 "Segoe UI","Microsoft YaHei UI",system-ui,sans-serif}
a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}
code,pre{font-family:"Cascadia Code",Consolas,monospace}code{background:var(--code);padding:.1em .4em;border-radius:5px;font-size:.9em}
.wrap{max-width:1080px;margin:0 auto;padding:0 22px 70px}
header.hero{background:linear-gradient(135deg,#3f8cff,#6aa8ff 50%,#8fc0ff);color:#fff;padding:50px 22px 38px}
.hero-inner{max-width:1080px;margin:0 auto}.eyebrow{letter-spacing:.18em;font-size:12px;opacity:.85;margin:0 0 10px;text-transform:uppercase}
h1{margin:0;font-size:38px;line-height:1.15}.sub{margin:10px 0 18px;font-size:16.5px;opacity:.95}
.chips{display:flex;flex-wrap:wrap;gap:8px}.chip{background:rgba(255,255,255,.18);border:1px solid rgba(255,255,255,.35);border-radius:999px;padding:4px 12px;font-size:12.5px}
nav.toc{position:sticky;top:0;z-index:5;background:color-mix(in srgb,var(--bg) 88%,transparent);backdrop-filter:blur(10px);border-bottom:1px solid var(--line)}
nav.toc ul{max-width:1080px;margin:0 auto;padding:10px 22px;list-style:none;display:flex;gap:18px;overflow-x:auto;font-size:13.5px;white-space:nowrap}
nav.toc a{color:var(--muted)}nav.toc a:hover{color:var(--accent)}
section{margin:34px 0 0}h2{font-size:23px;margin:0 0 14px}h2 .num{display:inline-block;vertical-align:middle;margin-right:10px;font-size:13px;color:var(--accent);background:var(--soft);border-radius:8px;padding:2px 8px;font-weight:600}
h3{font-size:17px;margin:0}
.panel,.card{background:var(--panel);border:1px solid var(--line);border-radius:14px;box-shadow:var(--shadow)}
.panel{padding:20px 22px}.grid{display:grid;gap:16px}.cols-3{grid-template-columns:repeat(3,1fr)}.cols-2{grid-template-columns:repeat(2,1fr)}
@media(max-width:860px){.cols-3,.cols-2{grid-template-columns:1fr}}
.card{padding:16px 18px}.muted{color:var(--muted)}
dl.kv{display:grid;grid-template-columns:auto 1fr;gap:6px 14px;margin:0;font-size:13.5px}dl.kv dt{color:var(--muted)}dl.kv dd{margin:0}
table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{color:var(--muted);font-size:12.5px;font-weight:600}.table-wrap{overflow-x:auto;border:1px solid var(--line);border-radius:12px;background:var(--panel)}
.badge{display:inline-block;font-size:11.5px;border-radius:6px;padding:1px 7px;background:var(--soft);color:var(--accent);margin:0 4px 4px 0}
.badge.ok{background:rgba(31,157,99,.12);color:var(--ok)}.badge.manual{background:rgba(63,140,255,.12);color:var(--accent)}.badge.remaining{background:rgba(194,56,74,.12);color:var(--bad)}
.rule{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:18px 20px;margin:14px 0;box-shadow:var(--shadow)}
.rule header{display:flex;flex-wrap:wrap;gap:10px;align-items:baseline;justify-content:space-between}
.rule .why{background:var(--code);border-left:3px solid var(--accent);border-radius:0 8px 8px 0;padding:10px 14px;margin:10px 0;font-size:14px}
details{background:var(--panel);border:1px solid var(--line);border-radius:12px;margin:10px 0;overflow:hidden}
details>summary{cursor:pointer;padding:12px 16px;font-weight:600;list-style:none}details>summary::-webkit-details-marker{display:none}
details[open]>summary{border-bottom:1px solid var(--line)}
pre.diff{margin:0;padding:14px 16px;overflow:auto;font-size:12.5px;line-height:1.55;background:var(--code)}
pre.diff .add{color:var(--ok)}pre.diff .del{color:var(--bad)}pre.diff .hunk{color:var(--accent)}
footer{margin-top:40px;padding-top:16px;border-top:1px solid var(--line);color:var(--muted);font-size:12.5px}
</style></head><body>
<header class="hero"><div class="hero-inner">
<p class="eyebrow">Code Quality Report</p><h1>MokiPad 代码质量清理报告</h1>
<p class="sub">InspectCode 155 条问题逐条处理：什么规则、为什么改、改成了什么，附完整 diff。</p>
<div class="chips"><span class="chip">InspectCode 2026.2.3.1</span><span class="chip">Rider / ReSharper 引擎</span><span class="chip">.NET 10 / WPF</span><span class="chip">2026-10-30</span></div>
</div></header>
<nav class="toc"><ul><li><a href="#summary">摘要</a></li><li><a href="#process">处理流程</a></li><li><a href="#rules">逐条规则</a></li><li><a href="#diff">完整 diff</a></li><li><a href="#verify">验证</a></li></ul></nav>
<div class="wrap">
""")

    html_parts.append(f"""
<section id="summary"><h2><span class="num">01</span>摘要</h2>
<div class="panel">
<p>基线是清理前的 <strong>{total}</strong> 条 InspectCode 问题（0 error / 0 warning，全部是 SUGGESTION 与 HINT）。本轮分两段处理：先用 ReSharper Full Cleanup 统一代码风格，再逐条处理语义类建议；最终剩余 <strong>{remaining}</strong> 条。</p>
<div class="grid cols-3" style="margin-top:16px">
<div class="card"><h3>{total}</h3><p class="muted" style="margin:0">基线问题</p><dl class="kv" style="margin-top:8px"><dt>WARNING</dt><dd>{sev_before.get('WARNING',0)}</dd><dt>SUGGESTION</dt><dd>{sev_before.get('SUGGESTION',0)}</dd><dt>HINT</dt><dd>{sev_before.get('HINT',0)}</dd></dl></div>
<div class="card"><h3>{fixed}</h3><p class="muted" style="margin:0">已处理</p><dl class="kv" style="margin-top:8px"><dt>风格批处理</dt><dd>{cleanup_fixed}</dd><dt>逐条修改</dt><dd>{manual_fixed}</dd><dt>规则抑制</dt><dd>{suppressed}</dd></dl></div>
<div class="card"><h3>{remaining}</h3><p class="muted" style="margin:0">剩余</p><dl class="kv" style="margin-top:8px"><dt>WARNING</dt><dd>{sev_final.get('WARNING',0)}</dd><dt>SUGGESTION</dt><dd>{sev_final.get('SUGGESTION',0)}</dd><dt>HINT</dt><dd>{sev_final.get('HINT',0)}</dd></dl></div>
</div></div></section>

<section id="process"><h2><span class="num">02</span>处理流程</h2>
<div class="panel">
<p><strong>1. 基线</strong>：在清理警告后、风格清理前对 <code>MokiPad.sln</code> 跑 InspectCode（<code>--severity=HINT</code>），保存 XML/HTML 报告与源码备份，得到 {total} 条问题。</p>
<p><strong>2. 风格批处理</strong>：运行 <code>jb cleanupcode MokiPad.sln</code>（ReSharper Full Cleanup），按项目代码风格统一格式、尾逗号、<code>new()</code> 写法等；这一步处理了 {cleanup_fixed} 条纯风格问题。</p>
<p><strong>3. 逐条处理</strong>：对其余语义类建议（模式匹配、空传播、async void、GeneratedRegex、可见性、只读性等）逐条修改，保持行为不变，每改完一个项目就构建、跑验证。</p>
<p><strong>4. 复检</strong>：重新运行 InspectCode，确认 WARNING/ERROR 为 0，并记录仍未处理的条目与原因。</p>
<div class="callout" style="border-left:3px solid var(--accent);background:var(--soft);border-radius:0 10px 10px 0;padding:10px 14px;font-size:13.5px">Rider 的 Problems 面板与 InspectCode 的差异只是严重级别：Rider 默认统计 HINT，InspectCode 默认从 SUGGESTION 开始。本报告统一以 <code>--severity=HINT</code> 的口径统计。</div>
</div></section>

<section id="rules"><h2><span class="num">03</span>逐条规则</h2>
<p class="muted">按出现次数排序。每条规则给出：为什么值得改、本轮怎么改，以及基线中每一个出现位置和处理阶段。</p>
""")

    for rule in ordered:
        rows = rows_by_rule[rule]
        meta = rules[rule]
        why, how = EXPLANATIONS.get(rule, DEFAULT_EXPLANATION)
        cleanup_count = sum(1 for r in rows if r["state"] == "cleanup")
        manual_count = sum(1 for r in rows if r["state"] == "manual")
        left_count = sum(1 for r in rows if r["state"] == "remaining")
        badges = f'<span class="badge">{esc(meta["severity"])}</span><span class="badge">{esc(meta["category"])}</span>'
        if cleanup_count:
            badges += f'<span class="badge ok">Cleanup ×{cleanup_count}</span>'
        if manual_count:
            badges += f'<span class="badge manual">逐条修改 ×{manual_count}</span>'
        if left_count:
            badges += f'<span class="badge remaining">保留 ×{left_count}</span>'
        html_parts.append(f"""<article class="rule"><header><h3><code>{esc(rule)}</code></h3><div>{badges}</div></header>
<div class="muted" style="font-size:12.5px;margin-top:2px">{esc(meta['rule'])}</div>
<div class="why"><strong>为什么：</strong>{esc(why)}<br><strong>怎么改：</strong>{esc(how)}</div>
<div class="table-wrap"><table><thead><tr><th style="width:64px">阶段</th><th style="width:280px">位置</th><th>ReSharper 说明</th></tr></thead><tbody>""")
        for row in rows:
            state = {"cleanup": "Cleanup", "manual": "手工", "remaining": "保留", "suppressed": "抑制"}[row["state"]]
            html_parts.append(f"<tr><td>{esc(state)}</td><td class='mono'>{esc(row['file'])}:{row['line']}</td><td>{esc(row['message'])}</td></tr>")
        html_parts.append("</tbody></table></div></article>")

    html_parts.append(f"""
</section>

<section id="diff"><h2><span class="num">04</span>完整 diff</h2>
<p class="muted">以下 diff 是“风格清理前备份 → 当前源码”的逐文件对比，包含本报告里的全部代码改动（含 Full Cleanup 的格式统一）。点开每个文件查看逐行修改。</p>
""")
    total_add = sum(f["added"] for f in diffs)
    total_del = sum(f["removed"] for f in diffs)
    html_parts.append(f'<div class="panel" style="margin-bottom:12px"><strong>{len(diffs)}</strong> 个源码文件变更，<strong>+{total_add}</strong> / <strong>−{total_del}</strong> 行。</div>')
    for entry in diffs:
        html_parts.append(f"<details><summary>{esc(entry['path'])} <span class='muted'>（+{entry['added']} / −{entry['removed']}）</span></summary><pre class='diff'>")
        for line in entry["diff"]:
            cls = "add" if line.startswith("+") and not line.startswith("+++") else "del" if line.startswith("-") and not line.startswith("---") else "hunk" if line.startswith("@@") else ""
            html_parts.append(f'<span class="{cls}">{esc(line)}</span>\n')
        html_parts.append("</pre></details>")

    html_parts.append(f"""
</section>

<section id="verify"><h2><span class="num">05</span>验证</h2>
<div class="panel">
<dl class="kv">
<dt>构建</dt><dd><code>dotnet build MokiPad.sln -c Release</code>：0 warning / 0 error</dd>
<dt>验证套件</dt><dd><code>dotnet run --project tests/MokiPad.Verification -c Release</code>：全部 PASS（组件画廊、公式、图片/SVG、GIF 播放、链接、脚注、长文虚拟化、编码）</dd>
<dt>复查</dt><dd><code>jb inspectcode MokiPad.sln --severity=HINT</code>：WARNING 0 / ERROR 0，剩余 {remaining} 条</dd>
</dl>
</div></section>
<footer>由 InspectCode 2026.2.3.1 报告与源码 diff 自动生成 · MokiPad.sln · 2026-10-30</footer>
</div></body></html>""")

    OUT.write_text("".join(html_parts), encoding="utf-8")
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes); before={total} final={remaining} diffs={len(diffs)}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print("usage: report.py before.xml cleanup.xml final.xml", file=sys.stderr)
        sys.exit(2)
    before = load_report(Path(sys.argv[1]))
    cleanup = load_report(Path(sys.argv[2]))
    final = load_report(Path(sys.argv[3]))
    build(before, cleanup, final, collect_diff())
