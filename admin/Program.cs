using System.Diagnostics;
using Admin;
using Microsoft.Extensions.FileProviders;

// ContentRoot = folder sa Admin.csproj, bez obzira odakle se pokreće (dotnet run --project admin, iz admin/, ...)
var contentRoot = AppContext.BaseDirectory;
for (var d = new DirectoryInfo(contentRoot); d != null; d = d.Parent)
    if (File.Exists(Path.Combine(d.FullName, "Admin.csproj"))) { contentRoot = d.FullName; break; }

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
builder.WebHost.UseUrls(builder.Configuration["Url"] ?? "http://localhost:5080");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = Importer.Json.PropertyNamingPolicy;
    o.SerializerOptions.Encoder = Importer.Json.Encoder;
});

var app = builder.Build();

// admin/ je ContentRoot → repo je jedan nivo iznad, javni sajt je u public/
var repoDir   = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, ".."));
var publicDir = Path.Combine(repoDir, "public");
var dataDir   = Path.Combine(publicDir, "data");
var view      = app.Configuration["View"] ?? "Ombre_Kombinacije";
var importer  = new Importer(app.Configuration.GetConnectionString("JIIS")!, view, dataDir);

// Admin UI (wwwroot) + pregled javnog sajta na /site — bez keša, da se odmah vide novi podaci
app.UseDefaultFiles();
app.UseStaticFiles();
var publicFiles = new PhysicalFileProvider(publicDir);
// /site/ → odmah servira index.html (bez preusmjeravanja), kao GitHub Pages; /site → /site/ radi sam middleware
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = publicFiles, RequestPath = "/site" });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = publicFiles,
    RequestPath = "/site",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-store"
});

/* =========================================================
   API
   ========================================================= */
app.MapGet("/api/status", async () =>
{
    var db = await importer.PingAsync();
    var index = importer.ReadIndex();
    return Results.Ok(new
    {
        baza = new { ok = db.ok, redova = db.redova, poruka = db.poruka, view },
        podaci = index == null ? null : new
        {
            index.Generisano,
            index.Redova,
            opcina = index.Opcine.Count,
            opcine = index.Opcine
        }
    });
});

// Postavke javnog sajta (npr. osnovni URL za QR plakate) — public/data/config.json, objavljuje se sa podacima
var configPath = Path.Combine(dataDir, "config.json");
app.MapGet("/api/config", () =>
    Results.Text(File.Exists(configPath) ? File.ReadAllText(configPath) : "{\"baseUrl\": \"\"}", "application/json"));

app.MapPost("/api/config", async (SiteConfig cfg) =>
{
    var url = (cfg.BaseUrl ?? "").Trim();
    if (url != "" && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))
        return Results.BadRequest(new { greska = "Adresa mora počinjati sa https:// ili http://" });
    if (url != "" && !url.EndsWith('/')) url += "/";
    await File.WriteAllTextAsync(configPath,
        System.Text.Json.JsonSerializer.Serialize(new SiteConfig(url), Importer.Json), new System.Text.UTF8Encoding(false));
    return Results.Ok(new SiteConfig(url));
});

app.MapPost("/api/import", async (bool? dryRun) =>
{
    try { return Results.Ok(await importer.RunAsync(dryRun ?? false)); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { greska = ex.Message }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Objava: cijeli public/ → grana gh-pages (GitHub Pages: https://adnanp78.github.io/Vote-Simulator/)
// Radi preko zasebnog git worktree-a u admin/.deploy, pa ne dira radni folder ni main granu.
var deployDir = Path.Combine(app.Environment.ContentRootPath, ".deploy");
const string PagesBranch = "gh-pages";

app.MapGet("/api/publish/status", async () =>
{
    await Git(repoDir, "fetch", "-q", "origin", PagesBranch);
    var (_, when) = await Git(repoDir, "log", "-1", "--format=%cI", "origin/" + PagesBranch);
    return Results.Ok(new { zadnjaObjava = when.Trim() });
});

app.MapPost("/api/publish", async () =>
{
    var log = new List<string>();
    async Task<bool> Run(string cwd, params string[] a)
    {
        var (code, output) = await Git(cwd, a);
        log.Add("$ git " + string.Join(' ', a) + (output.Length > 0 ? "\n" + output.TrimEnd() : ""));
        return code == 0;
    }
    try
    {
        if (!await Run(repoDir, "fetch", "origin", PagesBranch)) return Results.Ok(new { ok = false, poruka = "Ne mogu dohvatiti granu " + PagesBranch, log });

        if (!Directory.Exists(Path.Combine(deployDir, ".git")) && !File.Exists(Path.Combine(deployDir, ".git")))
        {
            await Git(repoDir, "worktree", "prune");
            if (!await Run(repoDir, "worktree", "add", "-f", deployDir, PagesBranch))
                return Results.Ok(new { ok = false, poruka = "Ne mogu napraviti worktree", log });
        }
        if (!await Run(deployDir, "reset", "--hard", "origin/" + PagesBranch))
            return Results.Ok(new { ok = false, poruka = "Ne mogu osvježiti " + PagesBranch, log });

        // zamijeni sadržaj: obriši sve osim .git, pa kopiraj public/
        foreach (var e in new DirectoryInfo(deployDir).EnumerateFileSystemInfos())
        {
            if (e.Name == ".git") continue;
            if (e is DirectoryInfo d) d.Delete(true); else e.Delete();
        }
        foreach (var f in Directory.EnumerateFiles(publicDir, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f).StartsWith('_')) continue;          // privremeni test fajlovi
            var target = Path.Combine(deployDir, Path.GetRelativePath(publicDir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
        }
        File.WriteAllText(Path.Combine(deployDir, ".nojekyll"), "");   // GitHub Pages: serviraj fajlove kako jesu

        await Run(deployDir, "add", "-A");
        var (_, staged) = await Git(deployDir, "diff", "--cached", "--name-only");
        var files = staged.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        if (files == 0)
            return Results.Ok(new { ok = true, poruka = "Sajt je već ažuran — nema promjena za objavu.", log });

        var msg = $"Objava sajta ({DateTime.Now:yyyy-MM-dd HH:mm}), {files} fajlova";
        var ok = await Run(deployDir, "commit", "-m", msg) && await Run(deployDir, "push", "origin", PagesBranch);
        return Results.Ok(new { ok, poruka = ok ? $"Objavljeno ({files} fajlova). Sajt se osvježi za 1–2 minute." : "Greška pri objavi — vidi log.", log });
    }
    catch (Exception ex) { return Results.Ok(new { ok = false, poruka = ex.Message, log }); }
});

app.Run();

static async Task<(int code, string output)> Git(string cwd, params string[] args)
{
    var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    return (p.ExitCode, (await stdout) + (await stderr));
}

record SiteConfig(string? BaseUrl);
