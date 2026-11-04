using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Dapper;
using Gridline.Calc;
using Gridline.Infrastructure;
using Gridline.Live;
using Microsoft.AspNetCore.SignalR;
using UglyToad.PdfPig;

namespace Gridline.Proposal;

/// <summary>Queue of proposal readings. Uploads add to it; the reader works through it in the background.</summary>
public sealed class ProposalQueue
{
    readonly Channel<long> _ch = Channel.CreateUnbounded<long>();
    public ValueTask Add(long readingId) => _ch.Writer.WriteAsync(readingId);
    public IAsyncEnumerable<long> Read(CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}

public sealed class Suggestion
{
    public long? LineId { get; set; } public string Kind { get; set; } = "text"; public decimal? Amount { get; set; } public string? Text { get; set; }
    public string? Evidence { get; set; } public int? Page { get; set; } public decimal? Confidence { get; set; }
}
public sealed class ReadingResult { public decimal? BaseAmount { get; set; } public string? Summary { get; set; } public List<Suggestion> Suggestions { get; set; } = []; public string Model { get; set; } = ""; }

/// <summary>
/// Reads a sub's proposal PDF and suggests scope answers with the quoted words and page as evidence.
/// Uses Azure OpenAI when AzureOpenAI:Endpoint/Key/Deployment are set; otherwise a rules-based reader
/// (base amount + included/excluded keywords) so the feature still works offline.
/// </summary>
public sealed class ProposalReader(Db db, Storage storage, ProposalQueue queue, IHttpClientFactory http, IConfiguration cfg,
                                   IHubContext<ProjectHub> hub, ILogger<ProposalReader> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // pick up anything left queued by a restart
        try { await using var c = await db.Open(stop); foreach (var id in await c.QueryAsync<long>("SELECT ReadingId FROM ProposalReading WHERE Status IN ('queued','running')")) await queue.Add(id); }
        catch (Exception ex) { log.LogWarning(ex, "Could not reload queued proposal readings"); }
        await foreach (var id in queue.Read(stop))
        {
            try { await Run(id, stop); }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogError(ex, "Proposal reading {Id} failed", id);
                await using var c = await db.Open(stop);
                await c.ExecuteAsync("UPDATE ProposalReading SET Status = 'failed', ErrorText = @e, FinishedAt = now() WHERE ReadingId = @id", new { id, e = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message });
                await Notify(c, id, "proposal.failed");
            }
        }
    }

    sealed class Job { public long ReadingId { get; set; } public long BidId { get; set; } public long TradeId { get; set; } public string StoragePath { get; set; } = ""; public string BidderName { get; set; } = ""; public string TradeName { get; set; } = ""; }
    sealed class LineRef { public long LineId { get; set; } public string Section { get; set; } = ""; public string LineText { get; set; } = ""; public string? Desired { get; set; } }

    async Task Run(long readingId, CancellationToken stop)
    {
        await using var c = await db.Open(stop);
        var job = await c.QueryFirstOrDefaultAsync<Job>("""
            SELECT r.ReadingId, r.BidId, b.TradeId, d.StoragePath, b.BidderName, t.TradeName
            FROM ProposalReading r JOIN Bid b ON b.BidId = r.BidId JOIN Trade t ON t.TradeId = b.TradeId JOIN Document d ON d.DocumentId = r.DocumentId
            WHERE r.ReadingId = @readingId AND r.Status IN ('queued','running')
            """, new { readingId });
        if (job is null) return;
        await c.ExecuteAsync("UPDATE ProposalReading SET Status = 'running' WHERE ReadingId = @readingId", new { readingId });

        var pages = new List<string>();
        await using (var f = storage.Open(job.StoragePath))
        using (var pdf = PdfDocument.Open(f))
            foreach (var p in pdf.GetPages())
                // rebuild text lines from word positions (page.Text has no line breaks)
                pages.Add(string.Join("\n", p.GetWords().GroupBy(x => Math.Round(x.BoundingBox.Bottom / 2))
                    .OrderByDescending(g => g.Key).Select(g => string.Join(" ", g.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text)))));
        if (pages.All(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("No text found in the PDF (it may be a scan). Enter the answers by hand.");
        var lines = (await c.QueryAsync<LineRef>("SELECT LineId, Section::text AS Section, LineText, Desired::text AS Desired FROM ScopeLine WHERE TradeId = @t AND ArchivedAt IS NULL AND NOT IsHeading AND Section <> 'zone' ORDER BY Section, SortKey", new { t = job.TradeId })).ToList();

        var result = AiConfigured ? await Ai(job, pages, lines, stop) : Rules(pages, lines);
        var valid = lines.Select(l => l.LineId).ToHashSet();
        await using var t = await c.BeginTransactionAsync(stop);
        await c.ExecuteAsync("DELETE FROM ProposalSuggestion WHERE ReadingId = @readingId AND Decision = 'pending'", new { readingId }, t);
        foreach (var s in result.Suggestions)
        {
            var kind = s.Kind is "included" or "excluded" or "amount" or "price" or "text" ? s.Kind : "text";
            await c.ExecuteAsync("""
                INSERT INTO ProposalSuggestion (ReadingId, LineId, Kind, Amount, AnswerText, Evidence, PageNo, Confidence)
                VALUES (@readingId, @line, @kind::AnswerKind, @Amount, @Text, @Evidence, @Page, @conf)
                """, new { readingId, line = s.LineId is long l && valid.Contains(l) ? l : (long?)null, kind, s.Amount, s.Text,
                           Evidence = s.Evidence is { Length: > 600 } e ? e[..600] : s.Evidence, s.Page, conf = s.Confidence is decimal cf ? Math.Clamp(cf, 0, 1) : (decimal?)null }, t);
        }
        await c.ExecuteAsync("UPDATE ProposalReading SET Status = 'done', ModelName = @m, BaseAmountFound = @b, Summary = @s, FinishedAt = now(), ErrorText = NULL WHERE ReadingId = @readingId",
            new { readingId, m = result.Model, b = result.BaseAmount, s = result.Summary }, t);
        await t.CommitAsync(stop);
        await Notify(c, readingId, "proposal.read");
    }

    async Task Notify(Npgsql.NpgsqlConnection c, long readingId, string verb)
    {
        var x = await c.QueryFirstOrDefaultAsync<(long pid, long vid, string bidder)>("SELECT t.ProjectId, t.VersionId, b.BidderName FROM ProposalReading r JOIN Bid b ON b.BidId = r.BidId JOIN Trade t ON t.TradeId = b.TradeId WHERE r.ReadingId = @readingId", new { readingId });
        if (x.pid == 0) return;
        var ev = await c.ExecuteScalarAsync<long>("INSERT INTO ActivityEvent (ProjectId, VersionId, Verb, SubjectType, SubjectKey) VALUES (@p, @v, @verb, 'reading', @k) RETURNING EventId", new { p = x.pid, v = x.vid, verb, k = readingId.ToString() });
        await hub.Clients.Group(ProjectHub.Group(x.pid)).SendAsync("changed", new ChangeEvent(ev, verb, "reading", readingId.ToString(), 0, "Proposal reader", x.vid, x.pid));
    }

    bool AiConfigured => !string.IsNullOrWhiteSpace(cfg["AzureOpenAI:Endpoint"]) && !string.IsNullOrWhiteSpace(cfg["AzureOpenAI:Key"]) && !string.IsNullOrWhiteSpace(cfg["AzureOpenAI:Deployment"]);

    async Task<ReadingResult> Ai(Job job, List<string> pages, List<LineRef> lines, CancellationToken stop)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pages.Count && sb.Length < 60000; i++) sb.Append($"\n--- PAGE {i + 1} ---\n").Append(pages[i]);
        var scope = string.Join("\n", lines.Select(l => $"{l.LineId} | {(l.Section == "baserequirement" ? "base requirement" : "trade item")} | requested: {l.Desired ?? "-"} | {l.LineText}"));
        var system = """
            You read subcontractor bid proposals for a general contractor's estimator. Answer only from the proposal text.
            Return JSON: {"baseAmount": number|null, "summary": string, "suggestions": [{"lineId": number|null, "kind": "included"|"excluded"|"amount"|"price"|"text",
            "amount": number|null, "text": string|null, "evidence": string, "page": number, "confidence": number}]}.
            One suggestion per scope line the proposal clearly addresses; lineId must be one of the given ids. kind=amount when the proposal prices
            the line as an add or deduct (deducts are negative). Exclusions or qualifications that match no line: lineId null, kind excluded or text.
            evidence = the exact words from the proposal (max 200 chars); page = the PAGE number. Never invent numbers.
            """;
        var user = $"Trade: {job.TradeName}\nBidder: {job.BidderName}\n\nSCOPE LINES (id | section | requested | text):\n{scope}\n\nPROPOSAL TEXT:{sb}";
        var endpoint = cfg["AzureOpenAI:Endpoint"]!.TrimEnd('/');
        var deployment = cfg["AzureOpenAI:Deployment"]!;
        var version = cfg["AzureOpenAI:ApiVersion"] ?? "2024-10-21";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={version}")
        {
            Content = JsonContent.Create(new { messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } }, response_format = new { type = "json_object" } }),
        };
        req.Headers.Add("api-key", cfg["AzureOpenAI:Key"]);
        var client = http.CreateClient("ai"); client.Timeout = TimeSpan.FromMinutes(3);
        using var res = await client.SendAsync(req, stop);
        var body = await res.Content.ReadAsStringAsync(stop);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The AI service returned {(int)res.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");
        var content = JsonDocument.Parse(body).RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var r = JsonSerializer.Deserialize<ReadingResult>(content, JsonSerializerOptions.Web) ?? new ReadingResult();
        r.Model = $"azure-openai:{deployment}";
        return r;
    }

    static readonly Regex MoneyRx = new(@"\$\s?([0-9]{1,3}(?:,[0-9]{3})+|[0-9]{4,})(?:\.\d{2})?", RegexOptions.Compiled);
    static readonly Regex BaseRx = new(@"(base\s*bid|lump\s*sum|total\s*(?:bid|price|proposal)|our\s*price|for\s*the\s*sum\s*of|grand\s*total)[^$]{0,80}\$\s?([0-9][0-9,]*(?:\.\d{2})?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase) { "the", "and", "for", "with", "all", "per", "any", "are", "to", "of", "by", "in", "on", "at", "or", "be", "is", "as", "a", "an", "trade", "partner", "include", "includes", "including", "work", "provide" };

    /// <summary>No AI key: find the base bid and match included/excluded sentences to scope lines by shared keywords.</summary>
    static ReadingResult Rules(List<string> pages, List<LineRef> lines)
    {
        var r = new ReadingResult { Model = "rules (no AI key configured)" };
        for (var p = 0; p < pages.Count && r.BaseAmount is null; p++)
            if (BaseRx.Match(pages[p]) is { Success: true } m && decimal.TryParse(m.Groups[2].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            { r.BaseAmount = v; r.Suggestions.Add(new Suggestion { Kind = "text", Text = $"Base bid ${v:N0}", Evidence = m.Value.Trim(), Page = p + 1, Confidence = 0.7m }); }
        if (r.BaseAmount is null)
        {
            var all = pages.SelectMany((t, i) => MoneyRx.Matches(t).Select(m => (v: decimal.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture), p: i + 1, s: m.Value))).ToList();
            if (all.Count > 0) { var max = all.MaxBy(x => x.v); r.BaseAmount = max.v; r.Suggestions.Add(new Suggestion { Kind = "text", Text = $"Largest amount ${max.v:N0} (check it's the base bid)", Evidence = max.s, Page = max.p, Confidence = 0.35m }); }
        }
        var sentences = pages.SelectMany((t, i) => Regex.Split(t, @"(?<=[;\n])\s*|\s(?=\d{1,3}[.)]\s)|(?<=(?:inclusions|exclusions|clarifications|qualifications):)\s", RegexOptions.IgnoreCase).Where(s => s.Trim().Length > 12).Select(s => (s: s.Trim(), p: i + 1))).ToList();
        foreach (var l in lines)
        {
            var words = Regex.Matches(l.LineText.ToLowerInvariant(), @"[a-z]{3,}").Select(m => m.Value).Where(w => !Stop.Contains(w)).Distinct().ToList();
            if (words.Count == 0) continue;
            var best = sentences.Select(x => (x.s, x.p, hits: words.Count(w => x.s.Contains(w, StringComparison.OrdinalIgnoreCase))))
                                .Where(x => x.hits >= Math.Max(2, (int)Math.Ceiling(words.Count * 0.5))).OrderByDescending(x => x.hits).FirstOrDefault();
            if (best.s is null) continue;
            var low = best.s.ToLowerInvariant();
            var kind = Regex.IsMatch(low, @"\b(exclud|not included|by others|n\.i\.c|nic\b|no allowance)") ? "excluded"
                     : Regex.IsMatch(low, @"\b(includ|we will|provide|furnish)") ? "included" : null;
            decimal? amount = null;
            if (MoneyRx.Match(best.s) is { Success: true } mm && Regex.IsMatch(low, @"\b(add|deduct|alternate|extra)\b"))
            { amount = decimal.Parse(mm.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) * (low.Contains("deduct") ? -1 : 1); kind = "amount"; }
            if (kind is null) continue;
            r.Suggestions.Add(new Suggestion { LineId = l.LineId, Kind = kind, Amount = amount, Evidence = best.s.Length > 200 ? best.s[..200] : best.s, Page = best.p, Confidence = Math.Round(0.4m + 0.4m * best.hits / words.Count, 2) });
        }
        foreach (var (s, p) in sentences.Where(x => Regex.IsMatch(x.s, @"^\W*(exclusions?|we exclude|excluded)\b", RegexOptions.IgnoreCase)).Take(10))
            if (!r.Suggestions.Any(x => x.Evidence == s)) r.Suggestions.Add(new Suggestion { Kind = "excluded", Text = s.Length > 200 ? s[..200] : s, Evidence = s.Length > 200 ? s[..200] : s, Page = p, Confidence = 0.5m });
        r.Summary = $"Read {pages.Count} page(s) without AI: found {(r.BaseAmount is null ? "no base amount" : $"base ${r.BaseAmount:N0}")} and {r.Suggestions.Count(x => x.LineId is not null)} matching scope lines.";
        return r;
    }
}
