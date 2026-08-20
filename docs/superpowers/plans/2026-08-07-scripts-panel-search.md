# Scripts Panel Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an operator find the right call script in seconds — ranked search, an instant cache-backed open, and personal recent/frequent sections.

**Architecture:** Three new self-contained services carry the logic — `ScriptSearch` ranks (pure, no UI, no IO), `ScriptUsageService` stores per-operator usage in `%AppData%`, `ScriptCache` keeps the script tree on disk. `ScriptsDialog` stays a picker: it wires those three into two left-panel modes (tree when the search box is empty, flat ranked list when it is not) and still knows nothing about `uniqueId` resolution or `LoggedCallService`.

**Tech Stack:** C# / .NET 8 (`net8.0-windows10.0.17763`), Avalonia 11, xunit 2.5.3, `System.Text.Json`.

**Spec:** [docs/superpowers/specs/2026-08-07-scripts-panel-search-design.md](../specs/2026-08-07-scripts-panel-search-design.md)

---

## Conventions used throughout this plan

- The repo root is `C:\work\vv-phone-widget`. All paths below are relative to it.
- Main project uses **block-scoped** namespaces (`namespace OrbitalSIP.Services { ... }`). Test project uses **file-scoped** (`namespace OrbitalSIP.Tests;`). Match the file you are in.
- Build: `dotnet build vv-phone-widget.sln`
- Test: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
- Services that touch disk take an **optional file path and an optional clock** in the constructor. Production passes neither; tests pass a temp path and a fixed clock. `LoggedCallService` hardcodes its path and is therefore untestable — do not copy that part of it, copy only its `Load`/`Save`/`lock` shape.

## File Structure

**Create:**

| File | Responsibility |
|---|---|
| `OrbitalSIP/Services/ScriptSearch.cs` | Pure ranking: tokenize a query, score scripts, build breadcrumbs, highlight ranges and snippets. No IO, no Avalonia types. |
| `OrbitalSIP/Services/ScriptUsageService.cs` | Per-operator usage counters in `%AppData%/OrbitalSIP/script-usage.json`. |
| `OrbitalSIP/Services/ScriptCache.cs` | The script tree on disk in `%AppData%/OrbitalSIP/scripts-cache.json`, keyed by backend + user, 24h TTL. |
| `OrbitalSIP/Views/ScriptRowFactory.cs` | Builds the Avalonia controls for one result row / one tree node header. Keeps `ScriptsDialog.axaml.cs` from growing another 200 lines. |
| `OrbitalSIP.Tests/ScriptSearchTests.cs` | Ranking tests. |
| `OrbitalSIP.Tests/ScriptUsageServiceTests.cs` | Usage store tests. |
| `OrbitalSIP.Tests/ScriptCacheTests.cs` | Cache tests. |

**Modify:**

| File | Change |
|---|---|
| `OrbitalSIP/Models/ScriptModels.cs` | `ScriptsResult` gains `FromCache` and `CachedAt`. |
| `OrbitalSIP/Services/ScriptService.cs` | `GetCachedScripts()` reads the cache synchronously; `GetScriptsAsync()` writes through to it. |
| `OrbitalSIP/App.axaml.cs` | Register `ScriptUsage`. |
| `OrbitalSIP/Views/ScriptsDialog.axaml` | Recent/Frequent sections, flat result list, stale banner, clear button. |
| `OrbitalSIP/Views/ScriptsDialog.axaml.cs` | Two modes, cache-first load, keyboard navigation, usage recording. |
| `OrbitalSIP/Views/ScriptsWindowLauncher.cs` | Optional call-direction hint. |
| `OrbitalSIP/Views/ActiveCallView.axaml.cs` | Keep `isOutgoing`, pass it to the launcher. |
| `OrbitalSIP/Views/RecentsView.axaml.cs` | Pass `vm.Entry.Direction` to the launcher. |
| `OrbitalSIP/Assets/i18n/{ru,kk,tg,uz}.json` | 8 new keys. |

---

## Task 1: ScriptSearch — types and token matching

A query is split into tokens on whitespace. A script matches only if **every** token is found somewhere in it (AND). Matching is case-insensitive substring matching. Only `IsActive` scripts participate; the whole tree is flattened, so a child is found even when its parent is not.

**Files:**
- Create: `OrbitalSIP/Services/ScriptSearch.cs`
- Test: `OrbitalSIP.Tests/ScriptSearchTests.cs`

- [ ] **Step 1: Write the failing test**

Create `OrbitalSIP.Tests/ScriptSearchTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Ranking for the scripts panel. The tree can hold hundreds of scripts, so the
/// picker flattens it and orders by relevance instead of by the alphabet.
/// </summary>
public class ScriptSearchTests
{
    private static CallScript Script(
        string id,
        string title,
        string? description = null,
        string? categoryId = null,
        List<string>? steps = null,
        List<CallScript>? children = null,
        bool isActive = true) => new CallScript
    {
        Id = id,
        Title = title,
        Description = description,
        CategoryId = categoryId,
        Steps = steps,
        Children = children,
        IsActive = isActive
    };

    [Fact]
    public void EveryTokenMustMatch_OtherwiseTheScriptIsDropped()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Возврат товара"),
            Script("2", "Возврат денег за товар")
        };

        var result = ScriptSearch.Run(roots, "возврат товар");

        Assert.Equal(2, result.TotalMatches);

        var single = ScriptSearch.Run(roots, "возврат денег");
        Assert.Single(single.Matches);
        Assert.Equal("2", single.Matches[0].Script.Id);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var roots = new List<CallScript> { Script("1", "Возврат Товара") };

        Assert.Single(ScriptSearch.Run(roots, "ВОЗВРАТ").Matches);
    }

    [Fact]
    public void InactiveScriptsNeverAppear()
    {
        var roots = new List<CallScript> { Script("1", "Возврат товара", isActive: false) };

        Assert.Empty(ScriptSearch.Run(roots, "возврат").Matches);
    }

    [Fact]
    public void ChildIsFoundEvenWhenItsParentDoesNotMatch()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Продажи", children: new List<CallScript> { Script("2", "Возврат товара") })
        };

        var result = ScriptSearch.Run(roots, "возврат");

        Assert.Single(result.Matches);
        Assert.Equal("2", result.Matches[0].Script.Id);
    }

    [Fact]
    public void CategoryFilterNarrowsTheResult()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Возврат товара", categoryId: "sales"),
            Script("2", "Возврат по гарантии", categoryId: "support")
        };

        var result = ScriptSearch.Run(roots, "возврат", categoryId: "support");

        Assert.Single(result.Matches);
        Assert.Equal("2", result.Matches[0].Script.Id);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: FAIL — build error, `ScriptSearch` does not exist.

- [ ] **Step 3: Write the implementation**

Create `OrbitalSIP/Services/ScriptSearch.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>A highlighted span inside a title, in character offsets.</summary>
    public readonly record struct TextRange(int Start, int Length);

    /// <summary>One script that survived the query, with everything the row needs to render.</summary>
    public sealed class ScriptMatch
    {
        public CallScript Script { get; init; } = new CallScript();

        public int Score { get; init; }

        /// <summary>Ancestor titles joined with " › ". Empty for a root script.</summary>
        public string Breadcrumb { get; init; } = "";

        public IReadOnlyList<TextRange> TitleHighlights { get; init; } = Array.Empty<TextRange>();

        /// <summary>
        /// Set when the query hit the body rather than the title, so the row can show
        /// why this script came back. i18n key of the inline section label
        /// ("ScriptStepInline", "ScriptQuestionInline", "ScriptTipInline") or null for
        /// a description hit. Not the "ScriptSteps" family — those are the uppercase
        /// headings on the details panel and read wrong inside a sentence.
        /// </summary>
        public string? SnippetLabelKey { get; init; }

        /// <summary>1-based position inside that section; 0 when there is none.</summary>
        public int SnippetOrdinal { get; init; }

        /// <summary>Excerpt around the hit, ellipsised. Null when the title matched.</summary>
        public string? SnippetText { get; init; }
    }

    public sealed class ScriptSearchResult
    {
        public IReadOnlyList<ScriptMatch> Matches { get; init; } = Array.Empty<ScriptMatch>();

        /// <summary>How many matched before the <see cref="ScriptSearch.MaxResults"/> cut.</summary>
        public int TotalMatches { get; init; }

        public bool Truncated => TotalMatches > Matches.Count;
    }

    /// <summary>
    /// Ranks call scripts for the picker's search box. Pure: no IO, no Avalonia,
    /// no statics — so the weights can be tested directly.
    /// </summary>
    public static class ScriptSearch
    {
        /// <summary>Rows past this are cut; the panel tells the operator to narrow the query.</summary>
        public const int MaxResults = 50;

        public static ScriptSearchResult Run(
            IEnumerable<CallScript> roots,
            string query,
            string? categoryId = null)
        {
            var tokens = Tokenize(query);

            var flat = new List<(CallScript Script, string Breadcrumb)>();
            Flatten(roots, "", flat);

            var matches = new List<ScriptMatch>();
            foreach (var (script, breadcrumb) in flat)
            {
                if (!MatchesCategory(script, categoryId)) continue;
                if (!tokens.All(t => ContainsToken(script, t))) continue;

                matches.Add(new ScriptMatch { Script = script, Breadcrumb = breadcrumb });
            }

            return new ScriptSearchResult { Matches = matches, TotalMatches = matches.Count };
        }

        internal static List<string> Tokenize(string? query) =>
            (query ?? "")
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        private static void Flatten(
            IEnumerable<CallScript>? nodes,
            string parentPath,
            List<(CallScript, string)> into)
        {
            if (nodes == null) return;

            foreach (var node in nodes.Where(n => n.IsActive))
            {
                into.Add((node, parentPath));

                var childPath = string.IsNullOrEmpty(parentPath)
                    ? node.Title ?? ""
                    : $"{parentPath} › {node.Title}";

                Flatten(node.Children, childPath, into);
            }
        }

        private static bool MatchesCategory(CallScript script, string? categoryId) =>
            categoryId == null || script.CategoryId == categoryId || script.Category?.Id == categoryId;

        private static bool ContainsToken(CallScript script, string token) =>
            Has(script.Title, token)
            || Has(script.Description, token)
            || HasAny(script.Steps, token)
            || HasAny(script.Questions, token)
            || HasAny(script.Tips, token);

        private static bool Has(string? text, string token) =>
            text != null && text.ToLowerInvariant().Contains(token);

        private static bool HasAny(IEnumerable<string>? values, string token) =>
            values != null && values.Any(v => Has(v, token));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptSearch.cs OrbitalSIP.Tests/ScriptSearchTests.cs
git commit -m "feat(scripts): match a query token by token across the whole tree"
```

---

## Task 2: ScriptSearch — weights and ordering

Where a token matched decides the weight. Weights are summed across tokens, best-hit-per-token. Equal scores fall back to alphabetical order so the list does not reshuffle as the operator types.

| Where | Weight |
|---|---|
| `Title`, at the start of a word | 100 |
| `Title`, inside a word | 70 |
| `Description` | 40 |
| `Steps` / `Questions` / `Tips` | 20 |

**Files:**
- Modify: `OrbitalSIP/Services/ScriptSearch.cs`
- Test: `OrbitalSIP.Tests/ScriptSearchTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `OrbitalSIP.Tests/ScriptSearchTests.cs` (inside the class):

```csharp
    [Fact]
    public void TitleBeatsDescription_WhichBeatsTheBody()
    {
        var roots = new List<CallScript>
        {
            Script("body", "Гарантия", steps: new List<string> { "Уточните возврат" }),
            Script("desc", "Обмен", description: "Оформляем возврат"),
            Script("title", "Возврат товара")
        };

        var ids = ScriptSearch.Run(roots, "возврат").Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "title", "desc", "body" }, ids);
    }

    [Fact]
    public void MatchAtTheStartOfAWordOutranksAMatchInsideOne()
    {
        var roots = new List<CallScript>
        {
            Script("inner", "Перевозврат груза"),
            Script("start", "Возврат товара")
        };

        var ids = ScriptSearch.Run(roots, "возврат").Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "start", "inner" }, ids);
    }

    [Fact]
    public void EqualScoresAreOrderedAlphabetically()
    {
        var roots = new List<CallScript>
        {
            Script("b", "Возврат: этап Б"),
            Script("a", "Возврат: этап А")
        };

        var ids = ScriptSearch.Run(roots, "возврат").Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "a", "b" }, ids);
    }

    [Fact]
    public void ScoresFromSeveralTokensAddUp()
    {
        var roots = new List<CallScript>
        {
            Script("one", "Возврат", description: "товар"),
            Script("two", "Возврат товара")
        };

        var matches = ScriptSearch.Run(roots, "возврат товар").Matches;

        Assert.Equal("two", matches[0].Script.Id);
        Assert.True(matches[0].Score > matches[1].Score);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: FAIL — order is the input order, `Score` is always 0.

- [ ] **Step 3: Write the implementation**

In `OrbitalSIP/Services/ScriptSearch.cs`, add the weight constants right under `MaxResults`:

```csharp
        private const int WeightTitleWordStart = 100;
        private const int WeightTitleInner     = 70;
        private const int WeightDescription    = 40;
        private const int WeightBody           = 20;
```

Replace the body of `Run` with the scoring version:

```csharp
        public static ScriptSearchResult Run(
            IEnumerable<CallScript> roots,
            string query,
            string? categoryId = null)
        {
            var tokens = Tokenize(query);

            var flat = new List<(CallScript Script, string Breadcrumb)>();
            Flatten(roots, "", flat);

            var matches = new List<ScriptMatch>();
            foreach (var (script, breadcrumb) in flat)
            {
                if (!MatchesCategory(script, categoryId)) continue;

                int score = 0;
                bool everyTokenHit = true;

                foreach (var token in tokens)
                {
                    int weight = BestWeight(script, token);
                    if (weight == 0) { everyTokenHit = false; break; }
                    score += weight;
                }

                if (!everyTokenHit) continue;

                matches.Add(new ScriptMatch
                {
                    Script = script,
                    Breadcrumb = breadcrumb,
                    Score = score
                });
            }

            var ordered = matches
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Script.Title ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new ScriptSearchResult { Matches = ordered, TotalMatches = ordered.Count };
        }
```

Add the weight lookup, and drop the now-unused `ContainsToken`:

```csharp
        /// <summary>Highest weight this token earns anywhere in the script; 0 means no hit.</summary>
        private static int BestWeight(CallScript script, string token)
        {
            int titleIndex = IndexOf(script.Title, token);
            if (titleIndex >= 0)
                return IsWordStart(script.Title!, titleIndex) ? WeightTitleWordStart : WeightTitleInner;

            if (IndexOf(script.Description, token) >= 0)
                return WeightDescription;

            if (HasAny(script.Steps, token) || HasAny(script.Questions, token) || HasAny(script.Tips, token))
                return WeightBody;

            return 0;
        }

        private static int IndexOf(string? text, string token) =>
            text == null ? -1 : text.ToLowerInvariant().IndexOf(token, StringComparison.Ordinal);

        private static bool IsWordStart(string text, int index) =>
            index == 0 || !char.IsLetterOrDigit(text[index - 1]);
```

Keep `Has` and `HasAny` as they are — `HasAny` is still used by `BestWeight`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptSearch.cs OrbitalSIP.Tests/ScriptSearchTests.cs
git commit -m "feat(scripts): rank matches by where the query hit"
```

---

## Task 3: ScriptSearch — highlights, cap, empty query

The row shows which part of the title matched, and the list is cut at 50 with `TotalMatches` left intact so the panel can say "50 of 214". An empty query matches every active script with score 0 — the panel uses tree mode there, but the function must still be defined.

**Files:**
- Modify: `OrbitalSIP/Services/ScriptSearch.cs`
- Test: `OrbitalSIP.Tests/ScriptSearchTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `OrbitalSIP.Tests/ScriptSearchTests.cs`:

```csharp
    [Fact]
    public void TitleHighlightsCoverEveryTokenHit()
    {
        var roots = new List<CallScript> { Script("1", "Возврат товара") };

        var match = ScriptSearch.Run(roots, "возврат товар").Matches[0];

        Assert.Equal(2, match.TitleHighlights.Count);
        Assert.Equal(new TextRange(0, 7), match.TitleHighlights[0]);
        Assert.Equal(new TextRange(8, 5), match.TitleHighlights[1]);
    }

    [Fact]
    public void TokensThatOnlyHitTheBodyProduceNoTitleHighlight()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Гарантия", steps: new List<string> { "Уточните возврат" })
        };

        var match = ScriptSearch.Run(roots, "возврат").Matches[0];

        Assert.Empty(match.TitleHighlights);
    }

    [Fact]
    public void BreadcrumbCarriesTheAncestorTitles()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Продажи", children: new List<CallScript>
            {
                Script("2", "Возвраты", children: new List<CallScript> { Script("3", "Возврат товара") })
            })
        };

        var match = ScriptSearch.Run(roots, "товара").Matches.Single();

        Assert.Equal("Продажи › Возвраты", match.Breadcrumb);
    }

    [Fact]
    public void ResultsAreCutAtFiftyButTheTotalIsKept()
    {
        var roots = Enumerable.Range(0, 60)
            .Select(i => Script($"{i}", $"Возврат {i:D2}"))
            .ToList();

        var result = ScriptSearch.Run(roots, "возврат");

        Assert.Equal(50, result.Matches.Count);
        Assert.Equal(60, result.TotalMatches);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ABodyHitCarriesASnippetSayingWhereItCameFrom()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Гарантия", steps: new List<string> { "Поздороваться", "Уточните возврат товара" })
        };

        var match = ScriptSearch.Run(roots, "возврат").Matches[0];

        Assert.Equal("ScriptStepInline", match.SnippetLabelKey);
        Assert.Equal(2, match.SnippetOrdinal);
        Assert.Contains("возврат", match.SnippetText!);
    }

    [Fact]
    public void ADescriptionHitCarriesASnippetWithNoSectionLabel()
    {
        var roots = new List<CallScript> { Script("1", "Обмен", description: "Оформляем возврат денег") };

        var match = ScriptSearch.Run(roots, "возврат").Matches[0];

        Assert.Null(match.SnippetLabelKey);
        Assert.Equal(0, match.SnippetOrdinal);
        Assert.Contains("возврат", match.SnippetText!);
    }

    [Fact]
    public void ATitleHitCarriesNoSnippet()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Возврат товара", description: "возврат тоже тут")
        };

        Assert.Null(ScriptSearch.Run(roots, "возврат").Matches[0].SnippetText);
    }

    [Fact]
    public void ALongSnippetIsTrimmedAroundTheHit()
    {
        var filler = new string('я', 200);
        var roots = new List<CallScript>
        {
            Script("1", "Гарантия", steps: new List<string> { filler + " возврат " + filler })
        };

        var text = ScriptSearch.Run(roots, "возврат").Matches[0].SnippetText!;

        Assert.True(text.Length <= 92, $"snippet was {text.Length} chars");
        Assert.Contains("возврат", text);
        Assert.StartsWith("…", text);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void EmptyQueryMatchesEveryActiveScript()
    {
        var roots = new List<CallScript>
        {
            Script("1", "Возврат товара"),
            Script("2", "Гарантия", isActive: false)
        };

        var result = ScriptSearch.Run(roots, "   ");

        Assert.Single(result.Matches);
        Assert.Equal(0, result.Matches[0].Score);
        Assert.False(result.Truncated);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: FAIL — `TitleHighlights` is always empty, `SnippetText` does not compile, and 60 rows come back.

- [ ] **Step 3: Write the implementation**

In `Run`, collect highlights while scoring and cap at the end. Replace the per-script loop body and the return with:

```csharp
                int score = 0;
                bool everyTokenHit = true;
                var highlights = new List<TextRange>();

                foreach (var token in tokens)
                {
                    int weight = BestWeight(script, token);
                    if (weight == 0) { everyTokenHit = false; break; }
                    score += weight;

                    int titleIndex = IndexOf(script.Title, token);
                    if (titleIndex >= 0)
                        highlights.Add(new TextRange(titleIndex, token.Length));
                }

                if (!everyTokenHit) continue;

                // Nothing lit up in the title — show the operator what did match instead.
                var snippet = highlights.Count == 0 && tokens.Count > 0
                    ? FindSnippet(script, tokens[0])
                    : (null, 0, null);

                matches.Add(new ScriptMatch
                {
                    Script = script,
                    Breadcrumb = breadcrumb,
                    Score = score,
                    TitleHighlights = highlights
                        .OrderBy(h => h.Start)
                        .ToList(),
                    SnippetLabelKey = snippet.Item1,
                    SnippetOrdinal = snippet.Item2,
                    SnippetText = snippet.Item3
                });
            }

            var ordered = matches
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Script.Title ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new ScriptSearchResult
            {
                Matches = ordered.Take(MaxResults).ToList(),
                TotalMatches = ordered.Count
            };
        }
```

Add the snippet helpers next to `BestWeight`:

```csharp
        private const int SnippetLength = 90;

        /// <summary>
        /// Where this token actually hit, as a section label, a 1-based position and an
        /// excerpt. Description hits carry no label — the text speaks for itself.
        /// </summary>
        private static (string?, int, string?) FindSnippet(CallScript script, string token)
        {
            int descriptionIndex = IndexOf(script.Description, token);
            if (descriptionIndex >= 0)
                return (null, 0, Excerpt(script.Description!, descriptionIndex));

            var fromSteps = FindInList(script.Steps, token, "ScriptStepInline");
            if (fromSteps.Item3 != null) return fromSteps;

            var fromQuestions = FindInList(script.Questions, token, "ScriptQuestionInline");
            if (fromQuestions.Item3 != null) return fromQuestions;

            return FindInList(script.Tips, token, "ScriptTipInline");
        }

        private static (string?, int, string?) FindInList(List<string>? values, string token, string labelKey)
        {
            if (values == null) return (null, 0, null);

            for (int i = 0; i < values.Count; i++)
            {
                int index = IndexOf(values[i], token);
                if (index >= 0) return (labelKey, i + 1, Excerpt(values[i], index));
            }

            return (null, 0, null);
        }

        /// <summary>A window of <see cref="SnippetLength"/> characters centred on the hit.</summary>
        private static string Excerpt(string text, int matchIndex)
        {
            if (text.Length <= SnippetLength) return text;

            int start = Math.Max(0, matchIndex - SnippetLength / 3);
            int length = Math.Min(SnippetLength, text.Length - start);

            var excerpt = text.Substring(start, length);
            if (start > 0) excerpt = "…" + excerpt;
            if (start + length < text.Length) excerpt += "…";

            return excerpt;
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: PASS, 18 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptSearch.cs OrbitalSIP.Tests/ScriptSearchTests.cs
git commit -m "feat(scripts): report highlight spans, breadcrumbs, snippets and a result cap"
```

---

## Task 4: ScriptUsageService — record, recent, frequent

Store: `%AppData%/OrbitalSIP/script-usage.json`, a flat list of entries keyed by script + operator + direction. `Load`/`Save`/`lock` follow `Services/LoggedCallService.cs`, but the path and the clock are injectable so the tests do not touch the real profile.

**Files:**
- Create: `OrbitalSIP/Services/ScriptUsageService.cs`
- Test: `OrbitalSIP.Tests/ScriptUsageServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `OrbitalSIP.Tests/ScriptUsageServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The panel's "Recent" and "Frequent" sections come from this file. It is local
/// and per-operator: the backend keeps no record of which script an operator
/// reaches for most.
/// </summary>
public class ScriptUsageServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"script-usage-{Guid.NewGuid():N}.json");
    private DateTime _now = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    private ScriptUsageService NewService() => new ScriptUsageService(_path, () => _now);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void RecordingTheSameScriptTwiceIncrementsOneEntry()
    {
        var service = NewService();

        service.Record("s1", "operator1", "inbound");
        service.Record("s1", "operator1", "inbound");

        var stat = service.Snapshot("operator1", "inbound")["s1"];
        Assert.Equal(2, stat.Count);
    }

    [Fact]
    public void UsageSurvivesARestart()
    {
        NewService().Record("s1", "operator1", "inbound");

        var stat = NewService().Snapshot("operator1", "inbound")["s1"];

        Assert.Equal(1, stat.Count);
    }

    [Fact]
    public void RecentIsOrderedByLastUse_NewestFirst()
    {
        var service = NewService();

        service.Record("old", "operator1", null);
        _now = _now.AddMinutes(5);
        service.Record("new", "operator1", null);

        Assert.Equal(new[] { "new", "old" }, service.Recent("operator1", 5).ToArray());
    }

    [Fact]
    public void FrequentIsOrderedByCount_AndSkipsWhatRecentAlreadyShows()
    {
        var service = NewService();

        service.Record("often", "operator1", null);
        service.Record("often", "operator1", null);
        _now = _now.AddMinutes(5);
        service.Record("lately", "operator1", null);

        var recent = service.Recent("operator1", 1);
        Assert.Equal(new[] { "lately" }, recent.ToArray());
        Assert.Equal(new[] { "often" }, service.Frequent("operator1", 5, recent).ToArray());
    }

    [Fact]
    public void AnotherOperatorsUsageIsInvisible()
    {
        var service = NewService();

        service.Record("s1", "operator2", null);

        Assert.Empty(service.Recent("operator1", 5));
        Assert.Empty(service.Snapshot("operator1", null));
    }

    [Fact]
    public void SnapshotFlagsScriptsUsedOnTheSameCallDirection()
    {
        var service = NewService();

        service.Record("s1", "operator1", "inbound");

        Assert.True(service.Snapshot("operator1", "inbound")["s1"].MatchesDirection);
        Assert.False(service.Snapshot("operator1", "outbound")["s1"].MatchesDirection);
        Assert.False(service.Snapshot("operator1", null)["s1"].MatchesDirection);
    }

    [Fact]
    public void ACorruptFileIsTreatedAsEmptyAndDoesNotThrow()
    {
        File.WriteAllText(_path, "{ this is not json");

        var service = NewService();

        Assert.Empty(service.Recent("operator1", 5));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptUsageServiceTests"`
Expected: FAIL — build error, `ScriptUsageService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `OrbitalSIP/Services/ScriptUsageService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrbitalSIP.Services
{
    public class ScriptUsageEntry
    {
        public string ScriptId { get; set; } = "";
        public string OperatorId { get; set; } = "";

        /// <summary>"inbound" / "outbound", or null when the panel was opened without a call.</summary>
        public string? Direction { get; set; }

        public int Count { get; set; }
        public DateTime LastUsedAt { get; set; }
    }

    /// <summary>What ranking needs to know about one script's history.</summary>
    public sealed class ScriptUsageStat
    {
        public int Count { get; init; }
        public DateTime LastUsedAt { get; init; }

        /// <summary>True when this script was used on a call going the same way as the current one.</summary>
        public bool MatchesDirection { get; init; }
    }

    /// <summary>
    /// Per-operator script usage, kept locally. Mirrors LoggedCallService's
    /// load/lock/save shape, but takes its path and clock so it can be tested
    /// without writing into a real user profile.
    /// </summary>
    public class ScriptUsageService
    {
        private static readonly string DefaultFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OrbitalSIP", "script-usage.json");

        private readonly string _filePath;
        private readonly Func<DateTime> _clock;
        private readonly object _lock = new();
        private List<ScriptUsageEntry> _entries = new();

        public ScriptUsageService(string? filePath = null, Func<DateTime>? clock = null)
        {
            _filePath = filePath ?? DefaultFilePath;
            _clock = clock ?? (() => DateTime.UtcNow);
            Load();
        }

        public void Record(string scriptId, string operatorId, string? direction)
        {
            if (string.IsNullOrEmpty(scriptId) || string.IsNullOrEmpty(operatorId)) return;

            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e =>
                    e.ScriptId == scriptId && e.OperatorId == operatorId && e.Direction == direction);

                if (entry != null)
                {
                    entry.Count++;
                    entry.LastUsedAt = _clock();
                }
                else
                {
                    _entries.Add(new ScriptUsageEntry
                    {
                        ScriptId = scriptId,
                        OperatorId = operatorId,
                        Direction = direction,
                        Count = 1,
                        LastUsedAt = _clock()
                    });
                }

                Save();
            }
        }

        /// <summary>Per-script totals for one operator, folded across directions.</summary>
        public IReadOnlyDictionary<string, ScriptUsageStat> Snapshot(string operatorId, string? direction)
        {
            lock (_lock)
            {
                return _entries
                    .Where(e => e.OperatorId == operatorId)
                    .GroupBy(e => e.ScriptId)
                    .ToDictionary(g => g.Key, g => new ScriptUsageStat
                    {
                        Count = g.Sum(e => e.Count),
                        LastUsedAt = g.Max(e => e.LastUsedAt),
                        MatchesDirection = direction != null && g.Any(e => e.Direction == direction)
                    });
            }
        }

        public IReadOnlyList<string> Recent(string operatorId, int limit) =>
            Snapshot(operatorId, null)
                .OrderByDescending(kv => kv.Value.LastUsedAt)
                .Take(limit)
                .Select(kv => kv.Key)
                .ToList();

        public IReadOnlyList<string> Frequent(string operatorId, int limit, IEnumerable<string> exclude)
        {
            var skip = new HashSet<string>(exclude);

            return Snapshot(operatorId, null)
                .Where(kv => !skip.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value.Count)
                .ThenByDescending(kv => kv.Value.LastUsedAt)
                .Take(limit)
                .Select(kv => kv.Key)
                .ToList();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var loaded = JsonSerializer.Deserialize<List<ScriptUsageEntry>>(File.ReadAllText(_filePath));
                if (loaded != null) _entries = loaded;
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptUsageService", $"Error loading script usage: {ex.Message}");
                _entries = new List<ScriptUsageEntry>();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Log("ScriptUsageService", $"Error saving script usage: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptUsageServiceTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptUsageService.cs OrbitalSIP.Tests/ScriptUsageServiceTests.cs
git commit -m "feat(scripts): remember which scripts an operator actually uses"
```

---

## Task 5: ScriptUsageService — cleanup

The file must not grow forever, must not carry another operator's history, and must forget stale entries. Cleanup runs on every `Record`, when the current operator is known.

**Files:**
- Modify: `OrbitalSIP/Services/ScriptUsageService.cs`
- Test: `OrbitalSIP.Tests/ScriptUsageServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `OrbitalSIP.Tests/ScriptUsageServiceTests.cs`:

```csharp
    [Fact]
    public void EntriesOlderThanSixtyDaysAreDropped()
    {
        var service = NewService();
        service.Record("stale", "operator1", null);

        _now = _now.AddDays(61);
        service.Record("fresh", "operator1", null);

        Assert.Equal(new[] { "fresh" }, service.Recent("operator1", 5).ToArray());
    }

    [Fact]
    public void RecordingPurgesEntriesLeftBehindByAnotherOperator()
    {
        var service = NewService();
        service.Record("theirs", "operator2", null);

        service.Record("mine", "operator1", null);

        var stored = NewService();
        Assert.Empty(stored.Recent("operator2", 5));
        Assert.Equal(new[] { "mine" }, stored.Recent("operator1", 5).ToArray());
    }

    [Fact]
    public void TheFileIsCappedAtTwoHundredEntries_OldestGoFirst()
    {
        var service = NewService();

        for (int i = 0; i < 205; i++)
        {
            service.Record($"s{i:D3}", "operator1", null);
            _now = _now.AddMinutes(1);
        }

        var all = service.Recent("operator1", 500);

        Assert.Equal(ScriptUsageService.MaxEntries, all.Count);
        Assert.Contains("s204", all);
        Assert.DoesNotContain("s000", all);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptUsageServiceTests"`
Expected: FAIL — nothing is ever removed, `MaxEntries` does not compile.

- [ ] **Step 3: Write the implementation**

In `OrbitalSIP/Services/ScriptUsageService.cs`, add the limits to the class and call `Cleanup` from `Record`:

```csharp
        public const int MaxEntries = 200;

        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);
```

Inside `Record`, replace the trailing `Save();` with:

```csharp
                Cleanup(operatorId);
                Save();
```

Add the method (caller already holds `_lock`, so it does not take it again):

```csharp
        /// <summary>
        /// Drops what the panel will never show: entries left by a previous operator
        /// on this machine, entries older than <see cref="MaxAge"/>, and — once the
        /// file is still over <see cref="MaxEntries"/> — the least recently used.
        /// Must be called with <c>_lock</c> held.
        /// </summary>
        private void Cleanup(string currentOperatorId)
        {
            var cutoff = _clock() - MaxAge;

            _entries.RemoveAll(e => e.OperatorId != currentOperatorId || e.LastUsedAt < cutoff);

            if (_entries.Count > MaxEntries)
            {
                _entries = _entries
                    .OrderByDescending(e => e.LastUsedAt)
                    .Take(MaxEntries)
                    .ToList();
            }
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptUsageServiceTests"`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptUsageService.cs OrbitalSIP.Tests/ScriptUsageServiceTests.cs
git commit -m "feat(scripts): keep the usage file small and single-operator"
```

---

## Task 6: ScriptSearch — usage bonuses

Frequency, recency and matching call direction nudge the order. The bonuses are deliberately small: the largest possible bonus (15) is below the gap between any two weight classes (20), so a body hit can never outrank a description hit, and a description hit can never outrank a title hit. Bonuses reorder scripts **within** a class, nothing more.

| Bonus | Value |
|---|---|
| Frequency | `min(Count, 8)` → up to 8 |
| Used in the last 24h | 5 |
| Used in the last 7 days | 2 |
| Same call direction | 2 |

**Files:**
- Modify: `OrbitalSIP/Services/ScriptSearch.cs`
- Test: `OrbitalSIP.Tests/ScriptSearchTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `OrbitalSIP.Tests/ScriptSearchTests.cs`. Add `using System;` to the file's usings first.

```csharp
    private static readonly DateTime Now = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AFrequentlyUsedScriptWinsATieWithAnUnusedOne()
    {
        var roots = new List<CallScript>
        {
            Script("b", "Возврат Б"),
            Script("a", "Возврат А")
        };

        var usage = new Dictionary<string, ScriptUsageStat>
        {
            ["b"] = new ScriptUsageStat { Count = 5, LastUsedAt = Now.AddDays(-30) }
        };

        var ids = ScriptSearch.Run(roots, "возврат", usage: usage, now: Now)
            .Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "b", "a" }, ids);
    }

    [Fact]
    public void AScriptUsedTodayWinsATieWithOneUsedLastMonth()
    {
        var roots = new List<CallScript>
        {
            Script("a", "Возврат А"),
            Script("b", "Возврат Б")
        };

        var usage = new Dictionary<string, ScriptUsageStat>
        {
            ["a"] = new ScriptUsageStat { Count = 1, LastUsedAt = Now.AddDays(-30) },
            ["b"] = new ScriptUsageStat { Count = 1, LastUsedAt = Now.AddHours(-2) }
        };

        var ids = ScriptSearch.Run(roots, "возврат", usage: usage, now: Now)
            .Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "b", "a" }, ids);
    }

    [Fact]
    public void NoBonusCanLiftABodyHitAboveADescriptionHit()
    {
        var roots = new List<CallScript>
        {
            Script("body", "Гарантия", steps: new List<string> { "Уточните возврат" }),
            Script("desc", "Обмен", description: "Оформляем возврат")
        };

        var usage = new Dictionary<string, ScriptUsageStat>
        {
            ["body"] = new ScriptUsageStat { Count = 500, LastUsedAt = Now, MatchesDirection = true }
        };

        var ids = ScriptSearch.Run(roots, "возврат", usage: usage, now: Now)
            .Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "desc", "body" }, ids);
    }

    [Fact]
    public void MatchingTheCallDirectionAddsASmallBonus()
    {
        var roots = new List<CallScript>
        {
            Script("a", "Возврат А"),
            Script("b", "Возврат Б")
        };

        var usage = new Dictionary<string, ScriptUsageStat>
        {
            ["b"] = new ScriptUsageStat { Count = 0, LastUsedAt = Now.AddYears(-1), MatchesDirection = true }
        };

        var ids = ScriptSearch.Run(roots, "возврат", usage: usage, now: Now)
            .Matches.Select(m => m.Script.Id).ToList();

        Assert.Equal(new[] { "b", "a" }, ids);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: FAIL — `Run` has no `usage` or `now` parameter.

- [ ] **Step 3: Write the implementation**

In `OrbitalSIP/Services/ScriptSearch.cs`, extend the signature:

```csharp
        public static ScriptSearchResult Run(
            IEnumerable<CallScript> roots,
            string query,
            string? categoryId = null,
            IReadOnlyDictionary<string, ScriptUsageStat>? usage = null,
            DateTime? now = null)
```

Right after `if (!everyTokenHit) continue;`, fold the bonus into the score:

```csharp
                score += UsageBonus(script.Id, usage, now ?? DateTime.UtcNow);
```

Add the bonus function and its constants next to the weights:

```csharp
        private const int MaxFrequencyBonus = 8;
        private const int BonusUsedToday    = 5;
        private const int BonusUsedThisWeek = 2;
        private const int BonusDirection    = 2;

        /// <summary>
        /// Personal history nudges the order. Capped at 15 — below the 20-point gap
        /// between weight classes — so history can reshuffle scripts that matched the
        /// same way but can never lift a tip match above a title match.
        /// </summary>
        private static int UsageBonus(
            string? scriptId,
            IReadOnlyDictionary<string, ScriptUsageStat>? usage,
            DateTime now)
        {
            if (scriptId == null || usage == null || !usage.TryGetValue(scriptId, out var stat))
                return 0;

            int bonus = Math.Min(stat.Count, MaxFrequencyBonus);

            var age = now - stat.LastUsedAt;
            if (age <= TimeSpan.FromHours(24)) bonus += BonusUsedToday;
            else if (age <= TimeSpan.FromDays(7)) bonus += BonusUsedThisWeek;

            if (stat.MatchesDirection) bonus += BonusDirection;

            return bonus;
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptSearchTests"`
Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptSearch.cs OrbitalSIP.Tests/ScriptSearchTests.cs
git commit -m "feat(scripts): let personal history break ties in the ranking"
```

---

## Task 7: ScriptCache

Holds the last successfully fetched tree. Keyed by backend URL + username so a stand or operator switch never serves someone else's scripts. Anything unreadable is treated as a miss — a broken cache file must never keep the panel from opening.

**Files:**
- Create: `OrbitalSIP/Services/ScriptCache.cs`
- Test: `OrbitalSIP.Tests/ScriptCacheTests.cs`

- [ ] **Step 1: Write the failing test**

Create `OrbitalSIP.Tests/ScriptCacheTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// The picker used to sit on "Загрузка…" on every open. The cache lets it draw
/// the tree immediately and refresh behind the operator's back.
/// </summary>
public class ScriptCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"scripts-cache-{Guid.NewGuid():N}.json");
    private DateTime _now = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    private ScriptCache NewCache() => new ScriptCache(_path, () => _now);

    private static List<CallScript> Tree() => new()
    {
        new CallScript { Id = "1", Title = "Возврат товара", IsActive = true }
    };

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void WhatWasWrittenComesBack()
    {
        var key = ScriptCache.BuildKey("https://crm.example", "operator1");
        NewCache().Write(key, Tree());

        var entry = NewCache().Read(key);

        Assert.NotNull(entry);
        Assert.Equal("Возврат товара", entry!.Scripts[0].Title);
        Assert.Equal(_now, entry.SavedAt);
    }

    [Fact]
    public void ReadingWithAnotherKeyIsAMiss()
    {
        NewCache().Write(ScriptCache.BuildKey("https://crm.example", "operator1"), Tree());

        Assert.Null(NewCache().Read(ScriptCache.BuildKey("https://crm.example", "operator2")));
        Assert.Null(NewCache().Read(ScriptCache.BuildKey("https://staging.example", "operator1")));
    }

    [Fact]
    public void AMissingFileIsAMiss()
    {
        Assert.Null(NewCache().Read(ScriptCache.BuildKey("https://crm.example", "operator1")));
    }

    [Fact]
    public void AnEntryYoungerThanTheTtlIsFresh()
    {
        var key = ScriptCache.BuildKey("https://crm.example", "operator1");
        NewCache().Write(key, Tree());

        _now = _now.AddHours(23);
        var cache = NewCache();

        Assert.False(cache.IsStale(cache.Read(key)!));
    }

    [Fact]
    public void AnEntryPastTheTtlIsStaleButStillReadable()
    {
        var key = ScriptCache.BuildKey("https://crm.example", "operator1");
        NewCache().Write(key, Tree());

        _now = _now.AddHours(25);
        var cache = NewCache();
        var entry = cache.Read(key);

        Assert.NotNull(entry);
        Assert.True(cache.IsStale(entry!));
    }

    [Fact]
    public void ACorruptFileIsAMissAndDoesNotThrow()
    {
        File.WriteAllText(_path, "}{ not json at all");

        Assert.Null(NewCache().Read(ScriptCache.BuildKey("https://crm.example", "operator1")));
    }

    [Fact]
    public void TheTrailingSlashOnTheBackendUrlDoesNotChangeTheKey()
    {
        Assert.Equal(
            ScriptCache.BuildKey("https://crm.example/", "operator1"),
            ScriptCache.BuildKey("https://crm.example", "operator1"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptCacheTests"`
Expected: FAIL — build error, `ScriptCache` does not exist.

- [ ] **Step 3: Write the implementation**

Create `OrbitalSIP/Services/ScriptCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class ScriptCacheEntry
    {
        /// <summary>Backend + operator the tree was fetched for. See <see cref="ScriptCache.BuildKey"/>.</summary>
        public string Key { get; set; } = "";

        public DateTime SavedAt { get; set; }

        public List<CallScript> Scripts { get; set; } = new();
    }

    /// <summary>
    /// Last known good script tree, on disk. The picker draws from it immediately
    /// and refreshes in the background, so opening the panel mid-call no longer
    /// waits on the network.
    /// </summary>
    public class ScriptCache
    {
        /// <summary>Past this the entry is still shown, but the panel labels it as dated.</summary>
        public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

        private static readonly string DefaultFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OrbitalSIP", "scripts-cache.json");

        private readonly string _filePath;
        private readonly Func<DateTime> _clock;
        private readonly object _lock = new();

        public ScriptCache(string? filePath = null, Func<DateTime>? clock = null)
        {
            _filePath = filePath ?? DefaultFilePath;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// A cache written for one backend or operator must never be served to
        /// another — different tenants have entirely different scripts.
        /// </summary>
        public static string BuildKey(string? backendUrl, string? username) =>
            $"{backendUrl?.TrimEnd('/') ?? ""}|{username ?? ""}";

        /// <summary>The stored tree, or null on a miss: no file, wrong key, or unreadable content.</summary>
        public ScriptCacheEntry? Read(string key)
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath)) return null;

                    var entry = JsonSerializer.Deserialize<ScriptCacheEntry>(File.ReadAllText(_filePath));
                    if (entry == null || entry.Key != key) return null;

                    return entry;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ScriptCache", $"Error reading scripts cache: {ex.Message}");
                    return null;
                }
            }
        }

        public bool IsStale(ScriptCacheEntry entry) => _clock() - entry.SavedAt > Ttl;

        public void Write(string key, List<CallScript> scripts)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

                    var entry = new ScriptCacheEntry { Key = key, SavedAt = _clock(), Scripts = scripts };
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(entry));
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ScriptCache", $"Error writing scripts cache: {ex.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~ScriptCacheTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/ScriptCache.cs OrbitalSIP.Tests/ScriptCacheTests.cs
git commit -m "feat(scripts): cache the script tree between openings"
```

---

## Task 8: Wire the cache into ScriptService

`GetCachedScripts()` is synchronous and instant — the dialog calls it before it awaits anything. `GetScriptsAsync()` keeps its current behaviour and additionally writes through on success.

**Files:**
- Modify: `OrbitalSIP/Models/ScriptModels.cs:69-77`
- Modify: `OrbitalSIP/Services/ScriptService.cs:30-69`

- [ ] **Step 1: Add the result fields**

In `OrbitalSIP/Models/ScriptModels.cs`, replace the `ScriptsResult` class with:

```csharp
    /// <summary>Outcome of a scripts fetch: either a list, or an error message to show with a retry.</summary>
    public class ScriptsResult
    {
        public List<CallScript> Scripts { get; set; } = new List<CallScript>();

        public string? Error { get; set; }

        public bool Failed => Error != null;

        /// <summary>True when these scripts came off disk rather than off the backend.</summary>
        public bool FromCache { get; set; }

        /// <summary>When the cached copy was fetched. Null for a live response.</summary>
        public DateTime? CachedAt { get; set; }
    }
```

- [ ] **Step 2: Add the cache to the service**

In `OrbitalSIP/Services/ScriptService.cs`, add a field next to `_httpClient`:

```csharp
        private readonly ScriptCache _cache = new ScriptCache();
```

Add the synchronous reader above `GetScriptsAsync`:

```csharp
        /// <summary>
        /// The last tree we managed to fetch, straight off disk. Returns an empty
        /// result on a miss. Never touches the network — the picker calls this to
        /// paint itself before the first await.
        /// </summary>
        public ScriptsResult GetCachedScripts()
        {
            var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
            var entry = _cache.Read(ScriptCache.BuildKey(settings.BackendUrl, settings.Username));

            if (entry == null)
                return new ScriptsResult();

            return new ScriptsResult
            {
                Scripts = entry.Scripts,
                FromCache = true,
                CachedAt = entry.SavedAt
            };
        }
```

In `GetScriptsAsync`, inside the `if (response.IsSuccessStatusCode)` branch, write through before returning. Replace those three lines with:

```csharp
                    var content = await response.Content.ReadAsStringAsync();
                    var scripts = JsonSerializer.Deserialize<List<CallScript>>(content) ?? new List<CallScript>();

                    _cache.Write(ScriptCache.BuildKey(backendUrl, settings.Username), scripts);

                    return new ScriptsResult { Scripts = scripts };
```

- [ ] **Step 3: Build**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: PASS — nothing regressed.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Models/ScriptModels.cs OrbitalSIP/Services/ScriptService.cs
git commit -m "feat(scripts): serve the picker from cache before the network answers"
```

---

## Task 9: Register the usage service

**Files:**
- Modify: `OrbitalSIP/App.axaml.cs:17-18`

- [ ] **Step 1: Add the singleton**

In `OrbitalSIP/App.axaml.cs`, next to the existing service fields:

```csharp
        public static readonly ScriptUsageService ScriptUsage = new ScriptUsageService();
```

Place it directly after the `LoggedCallService` line so the script-related services stay together. If `App.axaml.cs` does not already have `using OrbitalSIP.Services;`, qualify the type as `Services.ScriptUsageService` instead — match whatever the neighbouring lines do.

- [ ] **Step 2: Build**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/App.axaml.cs
git commit -m "chore(scripts): register the usage store"
```

---

## Task 10: i18n keys

Eleven new keys in all four languages. `{0}` / `{1}` are filled with `string.Format` — `I18nService.Get` does no formatting of its own.

The `*Inline` keys are the singular, sentence-case labels used inside a result snippet ("Шаг 2: …"). The existing `ScriptSteps` / `ScriptQuestions` / `ScriptTips` keys are the uppercase headings on the details panel and cannot be reused here.

**Files:**
- Modify: `OrbitalSIP/Assets/i18n/ru.json`
- Modify: `OrbitalSIP/Assets/i18n/kk.json`
- Modify: `OrbitalSIP/Assets/i18n/tg.json`
- Modify: `OrbitalSIP/Assets/i18n/uz.json`

- [ ] **Step 1: Add the keys**

Add to each file next to the existing `"AllCategories"` entry (mind the trailing commas — these are plain JSON objects):

`ru.json`:
```json
  "ScriptsRecent": "Недавние",
  "ScriptsFrequent": "Частые",
  "ScriptsAllScripts": "Все скрипты",
  "ScriptsFoundCount": "найдено {0}",
  "ScriptsResultsTruncated": "показаны {0} из {1}, уточните запрос",
  "ScriptsStaleData": "Данные от {0}",
  "Refresh": "Обновить",
  "ClearSearch": "Очистить",
  "ScriptStepInline": "Шаг",
  "ScriptQuestionInline": "Вопрос",
  "ScriptTipInline": "Подсказка",
```

`kk.json`:
```json
  "ScriptsRecent": "Соңғылары",
  "ScriptsFrequent": "Жиі қолданылатын",
  "ScriptsAllScripts": "Барлық скрипттер",
  "ScriptsFoundCount": "{0} табылды",
  "ScriptsResultsTruncated": "{1} ішінен {0} көрсетілді, сұрауды нақтылаңыз",
  "ScriptsStaleData": "{0} деректері",
  "Refresh": "Жаңарту",
  "ClearSearch": "Тазалау",
  "ScriptStepInline": "Қадам",
  "ScriptQuestionInline": "Сұрақ",
  "ScriptTipInline": "Кеңес",
```

`tg.json`:
```json
  "ScriptsRecent": "Охиринҳо",
  "ScriptsFrequent": "Зуд-зуд истифодашаванда",
  "ScriptsAllScripts": "Ҳамаи скриптҳо",
  "ScriptsFoundCount": "{0} ёфт шуд",
  "ScriptsResultsTruncated": "{0} аз {1} нишон дода шуд, дархостро аниқ кунед",
  "ScriptsStaleData": "Маълумот аз {0}",
  "Refresh": "Навсозӣ",
  "ClearSearch": "Тоза кардан",
  "ScriptStepInline": "Қадам",
  "ScriptQuestionInline": "Савол",
  "ScriptTipInline": "Маслиҳат",
```

`uz.json`:
```json
  "ScriptsRecent": "So'nggilar",
  "ScriptsFrequent": "Tez-tez ishlatiladigan",
  "ScriptsAllScripts": "Barcha skriptlar",
  "ScriptsFoundCount": "{0} ta topildi",
  "ScriptsResultsTruncated": "{1} tadan {0} tasi ko'rsatildi, so'rovni aniqlashtiring",
  "ScriptsStaleData": "{0} dagi ma'lumot",
  "Refresh": "Yangilash",
  "ClearSearch": "Tozalash",
  "ScriptStepInline": "Qadam",
  "ScriptQuestionInline": "Savol",
  "ScriptTipInline": "Maslahat",
```

- [ ] **Step 2: Verify all four files are still valid JSON**

Run:
```bash
for f in OrbitalSIP/Assets/i18n/*.json; do python -c "import json,sys; json.load(open(sys.argv[1], encoding='utf-8'))" "$f" && echo "$f ok"; done
```
Expected: four `ok` lines, no traceback.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/Assets/i18n
git commit -m "i18n(scripts): add strings for recents, result counts and stale data"
```

---

## Task 11: ScriptsDialog markup

The left column grows two things the current markup has no place for: a stale-data banner above the search box, and a results `ItemsControl` next to the tree. The tree stays exactly where it is — it is the empty-query mode.

**Files:**
- Modify: `OrbitalSIP/Views/ScriptsDialog.axaml:26-73`

- [ ] **Step 1: Replace the left column**

In `OrbitalSIP/Views/ScriptsDialog.axaml`, replace the whole `<Grid Grid.Column="0" ...>` block (the left panel, currently `RowDefinitions="Auto,Auto,*"`) with:

```xml
        <!-- Left: stale banner, search, category chips, tree or results -->
        <Grid Grid.Column="0" RowDefinitions="Auto,Auto,Auto,*" Margin="14,14,14,14">

          <!-- Stale cache banner: shown only when the refresh failed and we are on disk data -->
          <Border Grid.Row="0" Name="StaleBanner" IsVisible="False"
                  Background="#2A2113" BorderBrush="#4A3A17" BorderThickness="1"
                  CornerRadius="8" Padding="8,5" Margin="0,0,0,8">
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Name="StaleLabel" Text="" FontSize="11" Foreground="#D9B45B"
                         TextWrapping="Wrap" VerticalAlignment="Center" />
              <Button Grid.Column="1" Name="RefreshBtn" Content="{i18n:I18n Refresh}"
                      Background="Transparent" BorderThickness="0" Foreground="#E2E8F0"
                      FontSize="11" Padding="8,2" Margin="8,0,0,0" Cursor="Hand" />
            </Grid>
          </Border>

          <Grid Grid.Row="1" ColumnDefinitions="*,Auto">
            <TextBox Name="SearchBox"
                     Background="#1E293B"
                     BorderBrush="#334155"
                     Foreground="#E2E8F0"
                     Watermark="{i18n:I18n SearchScripts}"
                     CornerRadius="8" />
            <Button Grid.Column="1" Name="ClearSearchBtn" Content="✕" IsVisible="False"
                    ToolTip.Tip="{i18n:I18n ClearSearch}"
                    Width="24" Height="24" Margin="-30,0,0,0"
                    Background="Transparent" Foreground="#6E859D" BorderThickness="0"
                    Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center"
                    VerticalAlignment="Center" Cursor="Hand" />
          </Grid>

          <ScrollViewer Grid.Row="2" Name="CategoryScroller"
                        MaxHeight="96"
                        HorizontalScrollBarVisibility="Disabled"
                        VerticalScrollBarVisibility="Auto"
                        Margin="0,10,0,0" IsVisible="False">
            <WrapPanel Name="CategoryChips" />
          </ScrollViewer>

          <Panel Grid.Row="3" Margin="0,10,0,0">

            <!-- Empty-query mode: recents, frequents, full tree -->
            <ScrollViewer Name="TreeScroller" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
              <StackPanel Spacing="10">
                <StackPanel Name="RecentSection" IsVisible="False" Spacing="4">
                  <TextBlock Text="{i18n:I18n ScriptsRecent}" FontSize="10" FontWeight="Bold"
                             LetterSpacing="1.4" Foreground="#7B92AA" />
                  <StackPanel Name="RecentList" Spacing="2" />
                </StackPanel>

                <StackPanel Name="FrequentSection" IsVisible="False" Spacing="4">
                  <TextBlock Text="{i18n:I18n ScriptsFrequent}" FontSize="10" FontWeight="Bold"
                             LetterSpacing="1.4" Foreground="#7B92AA" />
                  <StackPanel Name="FrequentList" Spacing="2" />
                </StackPanel>

                <StackPanel Name="AllScriptsHeader" IsVisible="False" Spacing="4">
                  <TextBlock Text="{i18n:I18n ScriptsAllScripts}" FontSize="10" FontWeight="Bold"
                             LetterSpacing="1.4" Foreground="#7B92AA" />
                </StackPanel>

                <TreeView Name="ScriptsTreeView" Background="Transparent" Foreground="#E2E8F0" />
              </StackPanel>
            </ScrollViewer>

            <!-- Search mode: flat ranked list -->
            <Grid Name="ResultsPanel" IsVisible="False" RowDefinitions="*,Auto">
              <ScrollViewer Grid.Row="0" Name="ResultsScroller"
                            HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                <StackPanel Name="ResultsList" Spacing="2" />
              </ScrollViewer>
              <TextBlock Grid.Row="1" Name="ResultsCountLabel" Text=""
                         FontSize="11" Foreground="#6E859D" Margin="0,8,0,0" TextWrapping="Wrap" />
            </Grid>

            <TextBlock Name="LoadingLabel" Text="{i18n:I18n ScriptsLoading}"
                       FontSize="13" Foreground="#6E859D"
                       HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,24,0,0" />

            <TextBlock Name="EmptyLabel" IsVisible="False" Text="{i18n:I18n ScriptsEmpty}"
                       FontSize="13" Foreground="#6E859D" TextWrapping="Wrap"
                       TextAlignment="Center"
                       HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,24,0,0" />

            <StackPanel Name="ErrorPanel" IsVisible="False" Spacing="10"
                        HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,24,0,0">
              <TextBlock Name="ErrorLabel" Text="{i18n:I18n ScriptsLoadError}"
                         FontSize="12" Foreground="#EF4444" TextWrapping="Wrap"
                         TextAlignment="Center" />
              <Button Name="RetryBtn" Content="{i18n:I18n Retry}"
                      HorizontalAlignment="Center"
                      Background="#1E293B" Foreground="#E2E8F0" BorderThickness="0"
                      CornerRadius="8" Padding="12,6" FontSize="12" Cursor="Hand" />
            </StackPanel>

          </Panel>
        </Grid>
```

- [ ] **Step 2: Build**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds. The dialog compiles against the old code-behind — the new controls are simply not touched yet.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/Views/ScriptsDialog.axaml
git commit -m "feat(scripts): make room for recents, results and a stale banner"
```

---

## Task 12: ScriptRowFactory

One place that turns a `ScriptMatch` or a `CallScript` into a control. Extracted so `ScriptsDialog.axaml.cs` does not grow another block of layout code — it is already 460 lines.

**Files:**
- Create: `OrbitalSIP/Views/ScriptRowFactory.cs`

- [ ] **Step 1: Write the file**

Create `OrbitalSIP/Views/ScriptRowFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Builds the controls for one script row. Shared by the tree (empty query),
    /// the recents/frequents sections and the flat search results, so all three
    /// look the same.
    /// </summary>
    internal static class ScriptRowFactory
    {
        private static readonly Color Accent      = Color.Parse("#3B82F6");
        private static readonly Color Foreground  = Color.Parse("#E2E8F0");
        private static readonly Color Muted       = Color.Parse("#7B92AA");
        private static readonly Color HighlightBg = Color.Parse("#1E4270");

        public static Color? ParseColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try { return Color.Parse(value); }
            catch { return null; }
        }

        /// <summary>Category dot + title. Used as a TreeViewItem header.</summary>
        public static Control BuildTreeHeader(CallScript script)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var accent = ParseColor(script.Category?.Color);
            if (accent != null) panel.Children.Add(Dot(accent.Value, 6));

            panel.Children.Add(new TextBlock
            {
                Text = script.Title ?? "",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        /// <summary>
        /// A clickable row: highlighted title, greyed breadcrumb, and — when the query
        /// hit the body rather than the title — a snippet line saying why. Used for
        /// search results and for the recents/frequents sections (which pass no
        /// highlights and no snippet). <paramref name="onClick"/> fires on a single
        /// click, <paramref name="onConfirm"/> on a double click.
        /// </summary>
        public static Button BuildRow(
            CallScript script,
            string breadcrumb,
            IReadOnlyList<TextRange> highlights,
            string? snippet,
            Action onClick,
            Action onConfirm)
        {
            var lines = new StackPanel { Spacing = 1 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var accent = ParseColor(script.Category?.Color);
            if (accent != null) titleRow.Children.Add(Dot(accent.Value, 6));
            titleRow.Children.Add(HighlightedTitle(script.Title ?? "", highlights));
            lines.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(breadcrumb))
            {
                lines.Children.Add(new TextBlock
                {
                    Text = breadcrumb,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Muted),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (!string.IsNullOrEmpty(snippet))
            {
                lines.Children.Add(new TextBlock
                {
                    Text = snippet,
                    FontSize = 11,
                    FontStyle = FontStyle.Italic,
                    Foreground = new SolidColorBrush(Muted),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            var row = new Button
            {
                Content = lines,
                Tag = script,
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            row.Click += (_, __) => onClick();
            row.DoubleTapped += (_, e) => { e.Handled = true; onConfirm(); };

            return row;
        }

        /// <summary>Paints the row as the current keyboard selection.</summary>
        public static void SetSelected(Button row, bool selected) =>
            row.Background = selected ? new SolidColorBrush(HighlightBg) : Brushes.Transparent;

        private static Control Dot(Color color, double size) => new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Title with the matched spans in accent colour and bold.</summary>
        private static TextBlock HighlightedTitle(string title, IReadOnlyList<TextRange> highlights)
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Foreground),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (highlights == null || highlights.Count == 0)
            {
                block.Text = title;
                return block;
            }

            int cursor = 0;
            foreach (var range in highlights)
            {
                // Overlapping or stale ranges would throw on Substring — skip them.
                if (range.Start < cursor || range.Start + range.Length > title.Length) continue;

                if (range.Start > cursor)
                    block.Inlines!.Add(new Run(title.Substring(cursor, range.Start - cursor)));

                block.Inlines!.Add(new Run(title.Substring(range.Start, range.Length))
                {
                    Foreground = new SolidColorBrush(Accent),
                    FontWeight = FontWeight.Bold
                });

                cursor = range.Start + range.Length;
            }

            if (cursor < title.Length)
                block.Inlines!.Add(new Run(title.Substring(cursor)));

            return block;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/Views/ScriptRowFactory.cs
git commit -m "feat(scripts): factor out row building for the picker list"
```

---

## Task 13: Two modes in the dialog

The dialog now: paints from cache on open, refreshes in the background, switches between tree mode and results mode as the search box changes, and fills the recents/frequents sections.

**Files:**
- Modify: `OrbitalSIP/Views/ScriptsDialog.axaml.cs`

- [ ] **Step 1: Add the new state fields**

In `OrbitalSIP/Views/ScriptsDialog.axaml.cs`, add below the existing fields:

```csharp
        private readonly List<Button> _rows = new List<Button>();
        private int _highlightedRow = -1;
        private string? _directionHint;
        private IReadOnlyDictionary<string, ScriptUsageStat> _usage =
            new Dictionary<string, ScriptUsageStat>();
```

Add an operator accessor next to them:

```csharp
        private static string OperatorId
        {
            get
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                return settings.DecodedToken?.Operator?.Username ?? settings.Username ?? "";
            }
        }
```

- [ ] **Step 2: Load from cache first**

Replace `LoadScriptsAsync` with:

```csharp
        /// <summary>
        /// Paints whatever the cache holds before touching the network, then refreshes
        /// behind the operator. A refresh that fails leaves the cached tree on screen
        /// with a dated-data banner — replacing working data with a red error panel
        /// would cost the operator the scripts mid-call.
        /// </summary>
        private async Task LoadScriptsAsync()
        {
            var cached = App.ScriptService.GetCachedScripts();
            bool paintedFromCache = cached.Scripts.Count > 0;

            _usage = App.ScriptUsage.Snapshot(OperatorId, _directionHint);

            if (paintedFromCache)
            {
                _loading = false;
                _scripts = cached.Scripts;
                BuildCategoryChips();
                ApplyFilter();
            }
            else
            {
                _loading = true;
                SetState(loading: true);
            }

            var result = await App.ScriptService.GetScriptsAsync();

            Dispatcher.UIThread.Post(() =>
            {
                _loading = false;

                if (result.Failed)
                {
                    if (paintedFromCache)
                    {
                        ShowStaleBanner(cached.CachedAt);
                        return;
                    }

                    _scripts = new List<CallScript>();
                    _treeView.ItemsSource = null;
                    SetState(error: true);
                    return;
                }

                HideStaleBanner();
                _scripts = result.Scripts;
                BuildCategoryChips();
                ApplyFilter();
            });
        }

        private void ShowStaleBanner(DateTime? cachedAt)
        {
            var banner = this.FindControl<Border>("StaleBanner");
            var label = this.FindControl<TextBlock>("StaleLabel");
            if (banner == null || label == null) return;

            var stamp = (cachedAt ?? DateTime.UtcNow).ToLocalTime().ToString("HH:mm");
            label.Text = string.Format(I18nService.Instance.Get("ScriptsStaleData"), stamp);
            banner.IsVisible = true;
        }

        private void HideStaleBanner()
        {
            var banner = this.FindControl<Border>("StaleBanner");
            if (banner != null) banner.IsVisible = false;
        }
```

- [ ] **Step 3: Switch modes in ApplyFilter**

Replace `ApplyFilter`, and delete `FilterNodeList`, `MatchesQuery`, `Contains`, `MatchesCategory` and `CloneWithChildren` — `ScriptSearch` owns all of that now. Keep `BuildTreeItems`, but point its header call at the factory.

```csharp
        private void ApplyFilter()
        {
            if (_loading) return;

            var searchBox = this.FindControl<TextBox>("SearchBox");
            var query = searchBox?.Text?.Trim() ?? "";

            var clearBtn = this.FindControl<Button>("ClearSearchBtn");
            if (clearBtn != null) clearBtn.IsVisible = query.Length > 0;

            if (query.Length > 0)
                ShowResultsMode(query);
            else
                ShowTreeMode();
        }

        private void ShowResultsMode(string query)
        {
            var treeScroller = this.FindControl<ScrollViewer>("TreeScroller");
            var resultsPanel = this.FindControl<Grid>("ResultsPanel");
            var resultsList = this.FindControl<StackPanel>("ResultsList");
            var countLabel = this.FindControl<TextBlock>("ResultsCountLabel");
            if (resultsPanel == null || resultsList == null) return;

            var result = ScriptSearch.Run(_scripts, query, _categoryFilter, _usage);

            _rows.Clear();
            resultsList.Children.Clear();
            _highlightedRow = -1;

            foreach (var match in result.Matches)
            {
                var captured = match.Script;
                var row = ScriptRowFactory.BuildRow(
                    captured,
                    match.Breadcrumb,
                    match.TitleHighlights,
                    ComposeSnippet(match),
                    () => SelectRow(_rows.FindIndex(r => ReferenceEquals(r.Tag, captured))),
                    Confirm);

                _rows.Add(row);
                resultsList.Children.Add(row);
            }

            if (countLabel != null)
            {
                countLabel.Text = result.Truncated
                    ? string.Format(
                        I18nService.Instance.Get("ScriptsResultsTruncated"),
                        result.Matches.Count, result.TotalMatches)
                    : string.Format(I18nService.Instance.Get("ScriptsFoundCount"), result.TotalMatches);
                countLabel.IsVisible = result.TotalMatches > 0;
            }

            // SetState first: it owns the loading/error/empty/tree visibility and would
            // otherwise switch the tree back on underneath the results.
            SetState(empty: result.Matches.Count == 0);
            if (treeScroller != null) treeScroller.IsVisible = false;
            resultsPanel.IsVisible = result.Matches.Count > 0;

            // Keep the operator on the script they were reading if it is still in the
            // list — a background refresh must not yank the details panel out from
            // under them. Otherwise start at the best match.
            int keep = _selected == null
                ? -1
                : _rows.FindIndex(r => r.Tag is CallScript s && s.Id == _selected.Id);

            if (result.Matches.Count > 0) SelectRow(keep >= 0 ? keep : 0);
        }

        /// <summary>"Шаг 2: …вернуть товар…" — the section label lives in i18n, so it is composed here.</summary>
        private static string? ComposeSnippet(ScriptMatch match)
        {
            if (string.IsNullOrEmpty(match.SnippetText)) return null;
            if (match.SnippetLabelKey == null) return match.SnippetText;

            var label = I18nService.Instance.Get(match.SnippetLabelKey);
            return $"{label} {match.SnippetOrdinal}: {match.SnippetText}";
        }

        private void ShowTreeMode()
        {
            var resultsPanel = this.FindControl<Grid>("ResultsPanel");
            if (resultsPanel != null) resultsPanel.IsVisible = false;

            _rows.Clear();
            _highlightedRow = -1;

            BuildQuickSections();

            var items = BuildTreeItems(_scripts, expand: false);
            _treeView.ItemsSource = items;

            var header = this.FindControl<StackPanel>("AllScriptsHeader");
            if (header != null) header.IsVisible = _rows.Count > 0;

            SetState(empty: items.Count == 0 && _rows.Count == 0);
        }
```

- [ ] **Step 4: Fill the recents and frequents sections**

Add below `ShowTreeMode`:

```csharp
        private const int QuickSectionLimit = 5;

        /// <summary>
        /// Fills "Recent" and "Frequent". Both stay hidden until the operator has
        /// three scripts of history — below that they are noise, and on a small
        /// script list they would just duplicate the tree underneath.
        /// </summary>
        private void BuildQuickSections()
        {
            var recentSection = this.FindControl<StackPanel>("RecentSection");
            var frequentSection = this.FindControl<StackPanel>("FrequentSection");
            var recentList = this.FindControl<StackPanel>("RecentList");
            var frequentList = this.FindControl<StackPanel>("FrequentList");
            if (recentList == null || frequentList == null) return;

            recentList.Children.Clear();
            frequentList.Children.Clear();

            var operatorId = OperatorId;
            var byId = new Dictionary<string, (CallScript Script, string Breadcrumb)>();
            IndexScripts(_scripts, "", byId);

            if (byId.Count == 0 || _usage.Count < 3)
            {
                if (recentSection != null) recentSection.IsVisible = false;
                if (frequentSection != null) frequentSection.IsVisible = false;
                return;
            }

            var recentIds = App.ScriptUsage.Recent(operatorId, QuickSectionLimit)
                .Where(byId.ContainsKey).ToList();
            var frequentIds = App.ScriptUsage.Frequent(operatorId, QuickSectionLimit, recentIds)
                .Where(byId.ContainsKey).ToList();

            AddQuickRows(recentList, recentIds, byId);
            AddQuickRows(frequentList, frequentIds, byId);

            if (recentSection != null) recentSection.IsVisible = recentIds.Count > 0;
            if (frequentSection != null) frequentSection.IsVisible = frequentIds.Count > 0;
        }

        private void AddQuickRows(
            StackPanel list,
            IEnumerable<string> ids,
            IReadOnlyDictionary<string, (CallScript Script, string Breadcrumb)> byId)
        {
            foreach (var id in ids)
            {
                var (script, breadcrumb) = byId[id];
                var row = ScriptRowFactory.BuildRow(
                    script,
                    breadcrumb,
                    Array.Empty<TextRange>(),
                    snippet: null,
                    () => SelectRow(_rows.FindIndex(r => ReferenceEquals(r.Tag, script))),
                    Confirm);

                _rows.Add(row);
                list.Children.Add(row);
            }
        }

        /// <summary>Flattens the tree into id → (script, ancestor path) for the quick sections.</summary>
        private static void IndexScripts(
            IEnumerable<CallScript>? nodes,
            string parentPath,
            Dictionary<string, (CallScript, string)> into)
        {
            if (nodes == null) return;

            foreach (var node in nodes.Where(n => n.IsActive))
            {
                if (node.Id != null) into[node.Id] = (node, parentPath);

                var childPath = string.IsNullOrEmpty(parentPath)
                    ? node.Title ?? ""
                    : $"{parentPath} › {node.Title}";

                IndexScripts(node.Children, childPath, into);
            }
        }

        /// <summary>Moves the keyboard highlight and shows that script's details.</summary>
        private void SelectRow(int index)
        {
            if (index < 0 || index >= _rows.Count) return;

            for (int i = 0; i < _rows.Count; i++)
                ScriptRowFactory.SetSelected(_rows[i], i == index);

            _highlightedRow = index;

            if (_rows[index].Tag is CallScript script)
            {
                _selected = script;
                ShowDetails(script);
            }

            _rows[index].BringIntoView();
        }
```

- [ ] **Step 5: Point the tree header at the factory and wire the new buttons**

Replace the body of `BuildNodeHeader` with a delegation (keep the method so `BuildTreeItems` does not change):

```csharp
        private Control BuildNodeHeader(CallScript script) => ScriptRowFactory.BuildTreeHeader(script);
```

Delete the now-duplicated `ParseColor` from the dialog and use `ScriptRowFactory.ParseColor` in `BuildChip` and `ShowDetails` instead.

In the constructor, after the `retryBtn` wiring, add:

```csharp
            var refreshBtn = this.FindControl<Button>("RefreshBtn");
            if (refreshBtn != null) refreshBtn.Click += (_, __) => _ = LoadScriptsAsync();

            var clearSearchBtn = this.FindControl<Button>("ClearSearchBtn");
            if (clearSearchBtn != null)
                clearSearchBtn.Click += (_, __) =>
                {
                    var box = this.FindControl<TextBox>("SearchBox");
                    if (box != null) { box.Text = ""; box.Focus(); }
                };
```

Make sure the file has `using OrbitalSIP.Services;` — it already does — and add `using System.Collections.Generic;` and `using System.Linq;` if the compiler asks.

- [ ] **Step 6: Build and run the suite**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add OrbitalSIP/Views/ScriptsDialog.axaml.cs
git commit -m "feat(scripts): open from cache and swap the tree for ranked results"
```

---

## Task 14: Keyboard navigation

Arrows move the highlight without stealing focus from the search box, `Enter` saves, and `Esc` clears the query before it closes the window.

**Files:**
- Modify: `OrbitalSIP/Views/ScriptsDialog.axaml.cs:78-85`

- [ ] **Step 1: Replace the key handler**

Replace `OnDialogKeyDown` with:

```csharp
        /// <summary>
        /// The operator types with one hand while talking, so the arrows must move the
        /// selection without pulling focus out of the search box. Esc clears a non-empty
        /// query first: closing the whole window over a typo costs the scripts mid-call.
        /// </summary>
        private void OnDialogKeyDown(object? sender, KeyEventArgs e)
        {
            var searchBox = this.FindControl<TextBox>("SearchBox");

            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    if (!string.IsNullOrEmpty(searchBox?.Text))
                    {
                        searchBox!.Text = "";
                        searchBox.Focus();
                    }
                    else Close();
                    return;

                case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    e.Handled = true;
                    if (_selected != null) Confirm();
                    return;

                case Key.Enter:
                    // Ctrl+Enter is the way out of the comment box; a bare Enter there is a newline.
                    if (this.FindControl<TextBox>("CommentBox")?.IsFocused == true) return;
                    e.Handled = true;
                    if (_selected != null) Confirm();
                    return;

                case Key.Down:
                    e.Handled = true;
                    MoveHighlight(+1);
                    return;

                case Key.Up:
                    e.Handled = true;
                    MoveHighlight(-1);
                    return;
            }
        }

        /// <summary>
        /// Steps the highlight through the flat rows. In tree mode the rows are the
        /// recents/frequents sections; the TreeView keeps its own arrow handling for
        /// the nodes below them, including Left/Right to fold.
        /// </summary>
        private void MoveHighlight(int delta)
        {
            if (_rows.Count == 0) return;

            int next = _highlightedRow < 0
                ? (delta > 0 ? 0 : _rows.Count - 1)
                : Math.Clamp(_highlightedRow + delta, 0, _rows.Count - 1);

            SelectRow(next);
        }
```

- [ ] **Step 2: Keep the search box from swallowing the arrows**

`TextBox` handles Up/Down itself, so the window-level handler never sees them. In the constructor, replace the `searchBox` wiring with:

```csharp
            var searchBox = this.FindControl<TextBox>("SearchBox");
            if (searchBox != null)
            {
                searchBox.TextChanged += (s, e) => ApplyFilter();

                // Tunnelling: the TextBox consumes Up/Down for caret movement, so the
                // list would never see them if we waited for the bubbling event.
                searchBox.AddHandler(KeyDownEvent, (s, e) =>
                {
                    if (e.Key == Key.Down) { e.Handled = true; MoveHighlight(+1); }
                    else if (e.Key == Key.Up) { e.Handled = true; MoveHighlight(-1); }
                }, RoutingStrategies.Tunnel);
            }
```

Add `using Avalonia.Interactivity;` to the file for `RoutingStrategies`.

- [ ] **Step 3: Build**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Verify by hand**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

Open the scripts panel and check:
- typing filters, ↓ and ↑ move the highlight, the caret stays in the search box
- `Enter` saves and closes
- `Esc` clears the query, a second `Esc` closes the window
- `Ctrl+Enter` from inside the comment box saves and closes

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Views/ScriptsDialog.axaml.cs
git commit -m "feat(scripts): drive the picker from the keyboard"
```

---

## Task 15: Direction hint and usage recording

The launcher takes an optional direction; the dialog records a use when a script is actually saved.

**Files:**
- Modify: `OrbitalSIP/Views/ScriptsWindowLauncher.cs:27-53`
- Modify: `OrbitalSIP/Views/ScriptsDialog.axaml.cs` (constructor + `Confirm`)
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml.cs:60,1018-1028`
- Modify: `OrbitalSIP/Views/RecentsView.axaml.cs:152-161`

- [ ] **Step 1: Thread the hint through the launcher**

In `OrbitalSIP/Views/ScriptsWindowLauncher.cs`, change the signature and the construction:

```csharp
        /// <param name="directionHint">
        /// "inbound" / "outbound" when the panel is opened for a known call. Ranking
        /// uses it to favour scripts the operator reaches for on calls going the same
        /// way. Null is fine — ranking then uses the whole history.
        /// </param>
        public static void Open(Window owner, Action<ScriptSelection> onSelected, string? directionHint = null)
        {
            if (!App.ScriptWindows.TryBegin())
            {
                _current?.Activate();
                return;
            }

            try
            {
                var window = new ScriptsDialog(directionHint);
```

Leave the rest of the method as it is.

- [ ] **Step 2: Accept it in the dialog**

In `OrbitalSIP/Views/ScriptsDialog.axaml.cs`, change the constructor signature and set the field before the load kicks off:

```csharp
        public ScriptsDialog(string? directionHint = null)
        {
            _directionHint = directionHint;
            InitializeComponent();
```

The rest of the constructor is unchanged; `_ = LoadScriptsAsync();` at its end already reads `_directionHint` when it builds the usage snapshot.

- [ ] **Step 3: Record the use on confirm**

In `Confirm()`, record before raising the event:

```csharp
        private void Confirm()
        {
            if (_selected == null)
            {
                Close();
                return;
            }

            var commentBox = this.FindControl<TextBox>("CommentBox");

            // Recorded here rather than on selection: a script the operator merely
            // read and rejected should not climb the "frequent" list.
            if (_selected.Id != null)
                App.ScriptUsage.Record(_selected.Id, OperatorId, _directionHint);

            // Hand the selection over before closing: the Closed handler is what releases
            // the launcher's slot, so raising afterwards would race a re-open.
            ScriptSelected?.Invoke(this, new ScriptSelection
            {
                Script = _selected,
                Note = commentBox?.Text?.Trim() ?? ""
            });
            Close();
        }
```

- [ ] **Step 4: Pass the direction from both call sites**

In `OrbitalSIP/Views/ActiveCallView.axaml.cs`, store the flag. Add a field next to `_callIdentity`:

```csharp
        private readonly bool _isOutgoing;
```

Set it as the first line of the `(string callerId, bool isOutgoing, TimeSpan?)` constructor:

```csharp
            _isOutgoing = isOutgoing;
```

Then in `ShowScriptsDialog`:

```csharp
            ScriptsWindowLauncher.Open(
                topLevel,
                selection => _ = RegisterScriptAsync(number, selection),
                _isOutgoing ? "outbound" : "inbound");
```

In `OrbitalSIP/Views/RecentsView.axaml.cs`, in `OnCdrScriptClicked`:

```csharp
                ScriptsWindowLauncher.Open(
                    topLevel,
                    selection => _ = RegisterScriptAsync(vm, selection),
                    vm.Entry.Direction);
```

- [ ] **Step 5: Build and run the suite**

Run: `dotnet build vv-phone-widget.sln`
Expected: build succeeds, 0 errors.

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add OrbitalSIP/Views/ScriptsWindowLauncher.cs OrbitalSIP/Views/ScriptsDialog.axaml.cs OrbitalSIP/Views/ActiveCallView.axaml.cs OrbitalSIP/Views/RecentsView.axaml.cs
git commit -m "feat(scripts): record what gets used and which way the call went"
```

---

## Task 16: Manual verification

The UI half of this work has no automated coverage. Walk it once against a real backend before calling it done.

**Files:** none — verification only.

- [ ] **Step 1: Run the full suite**

Run: `dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: PASS, no skipped tests.

- [ ] **Step 2: Launch and walk the panel**

Run: `dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

Check each of these:

- [ ] First open with an empty `%AppData%/OrbitalSIP/scripts-cache.json` shows "Загрузка…", then the tree.
- [ ] Close and reopen: the tree appears with no loading state.
- [ ] Type a word from a script title: the tree is replaced by a flat list, the matched part of each title is highlighted, the breadcrumb sits underneath, and the count line reads "найдено N".
- [ ] Type a word that only appears in a script's steps: that script is found, ranks below title matches, and its row carries a third line reading "Шаг N: …".
- [ ] Type two words: only scripts containing both come back.
- [ ] Clear the search with the ✕ button: the tree comes back.
- [ ] Save a script, reopen the panel four more times saving different scripts: "Недавние" and "Частые" appear above the tree.
- [ ] Kill the backend (or unplug the network), reopen: the cached tree is shown with the amber "Данные от HH:MM" banner and a working "Обновить" button — not the red error panel.
- [ ] Delete `%AppData%/OrbitalSIP/scripts-cache.json`, kill the backend, reopen: the red error panel with "Повторить" still appears.
- [ ] Corrupt `%AppData%/OrbitalSIP/script-usage.json` by hand (write `{`), reopen: the panel works, sections are just empty.
- [ ] Open the panel during an active call: the widget stays usable — hang up and mute still respond.

- [ ] **Step 3: Commit any fixes**

If a check failed, fix it and commit separately:

```bash
git commit -am "fix(scripts): <what was wrong>"
```

---

## Out of scope

Named here so nobody adds them mid-plan: the global-hotkey command palette, binding scripts to a queue or campaign (needs backend work), step check-off during a call, window resizing and size persistence, MVVM conversion, and typo-tolerant (fuzzy) search.
