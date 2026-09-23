using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Admin;

/* =========================================================
   MODEL — isti format koji čita public/lista.html
   ========================================================= */
public record Kandidat(int Redni, string Ime, string? Stranka = null);
public record Stranka(int Redni, string Naziv, List<Kandidat> Kandidati);
public record Kolona(string Id, string Naslov, List<Kandidat> Kandidati);
public record Trka(string Id, string Naslov, string Podnaslov, string Tip,
                   List<Kolona>? Kolone = null, List<Stranka>? Stranke = null, List<Kandidat>? Kandidati = null);
public record IzbornaJedinica(string Kod, string Naziv, string Tip, string Entitet, string? Kanton, string? Azurirano);
public record Listic(IzbornaJedinica IzbornaJedinica, List<Trka> Trke);

public record OpcinaInfo(string Kod, string Naziv, string Entitet, string? Kanton, int Trke, int Redova, string Hash, string Azurirano);
public record DataIndex(string Generisano, string Izvor, int Redova, List<OpcinaInfo> Opcine);

public record Promjena(string Kod, string Naziv, string Status, List<string> Trke);
public record ImportResult(bool DryRun, int Redova, int Opcina, long TrajanjeMs,
                           List<Promjena> Promjene, int Nepromijenjeno, List<string> Upozorenja);

record Row(string Mun, string MunName, string Level, string LevelName, string CR,
           string Entity, string Party, int Pos, string First, string Last, int Order);

public class Importer
{
    readonly string _conn;
    readonly string _view;
    readonly string _dataDir;
    readonly SemaphoreSlim _lock = new(1, 1);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // čćžšđ ostaju čitljivi
        WriteIndented = true
    };

    public Importer(string conn, string view, string dataDir)
    {
        if (!Regex.IsMatch(view, @"^[A-Za-z0-9_.\[\]]+$"))
            throw new ArgumentException("Neispravno ime view-a: " + view);
        _conn = conn;
        _view = view;
        _dataDir = dataDir;
        Directory.CreateDirectory(_dataDir);
    }

    public string IndexPath => Path.Combine(_dataDir, "index.json");

    public DataIndex? ReadIndex()
    {
        if (!File.Exists(IndexPath)) return null;
        return JsonSerializer.Deserialize<DataIndex>(File.ReadAllText(IndexPath), Json);
    }

    public async Task<(bool ok, int? redova, string poruka)> PingAsync()
    {
        try
        {
            await using var c = new SqlConnection(_conn);
            await c.OpenAsync();
            await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {_view}", c);
            var n = (int)(await cmd.ExecuteScalarAsync())!;
            return (true, n, $"{c.DataSource} / {c.Database}");
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    /* =========================================================
       IMPORT — povuci view, složi listiće po općini, upiši promjene
       ========================================================= */
    public async Task<ImportResult> RunAsync(bool dryRun)
    {
        if (!await _lock.WaitAsync(0)) throw new InvalidOperationException("Import je već u toku.");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _cyrillicFixed.Clear();
            var rows = await LoadRowsAsync();
            var warnings = new List<string>();
            var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

            var oldIndex = ReadIndex();
            var oldByKod = oldIndex?.Opcine.ToDictionary(o => o.Kod) ?? new();

            var infos = new List<OpcinaInfo>();
            var changes = new List<Promjena>();
            int unchanged = 0;
            var unknownLevels = new SortedSet<string>();
            var noPresidency = new List<string>();

            foreach (var g in rows.GroupBy(r => r.Mun).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var listic = BuildListic(g.Key, g.ToList(), unknownLevels);
                if (!listic.Trke.Any(t => t.Tip == "predsjednistvo")) noPresidency.Add(g.Key);

                // hash bez datuma — da isti podaci ne proizvode "promjenu"
                var hash = Hash(JsonSerializer.Serialize(listic, Json));
                var path = Path.Combine(_dataDir, g.Key + ".json");
                oldByKod.TryGetValue(g.Key, out var old);

                string azurirano;
                if (old != null && old.Hash == hash && File.Exists(path))
                {
                    unchanged++;
                    azurirano = old.Azurirano;
                }
                else
                {
                    azurirano = now;
                    changes.Add(new Promjena(g.Key, listic.IzbornaJedinica.Naziv,
                        old == null ? "novo" : "izmijenjeno",
                        old == null ? new() : ChangedRaces(path, listic)));
                    if (!dryRun)
                    {
                        var final = listic with { IzbornaJedinica = listic.IzbornaJedinica with { Azurirano = azurirano } };
                        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(final, Json), new UTF8Encoding(false));
                    }
                }

                var ij = listic.IzbornaJedinica;
                infos.Add(new OpcinaInfo(ij.Kod, ij.Naziv, ij.Entitet, ij.Kanton, listic.Trke.Count, g.Count(), hash, azurirano));
            }

            // općine kojih više nema u view-u
            var newKods = infos.Select(i => i.Kod).ToHashSet();
            foreach (var old in oldByKod.Values.Where(o => !newKods.Contains(o.Kod)))
            {
                changes.Add(new Promjena(old.Kod, old.Naziv, "obrisano", new()));
                if (!dryRun) File.Delete(Path.Combine(_dataDir, old.Kod + ".json"));
            }

            if (unknownLevels.Count > 0)
                warnings.Add("Nepoznati nivoi (prikazani kao otvorena lista): " + string.Join(", ", unknownLevels));
            if (noPresidency.Count > 0)
                warnings.Add($"{noPresidency.Count} općina nema trku za Predsjedništvo BiH u view-u: " +
                             string.Join(", ", noPresidency.Take(12)) + (noPresidency.Count > 12 ? " …" : ""));
            if (_cyrillicFixed.Count > 0)
                warnings.Add($"Ispravljeno {_cyrillicFixed.Count} naziva sa ćiriličnim slovima unutar latinice: " +
                             string.Join(", ", _cyrillicFixed.Take(5)) + (_cyrillicFixed.Count > 5 ? " …" : ""));

            if (!dryRun)
            {
                var index = new DataIndex(now, _view, rows.Count, infos);
                await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(index, Json), new UTF8Encoding(false));
            }

            return new ImportResult(dryRun, rows.Count, infos.Count, sw.ElapsedMilliseconds,
                                    changes, unchanged, warnings);
        }
        finally { _lock.Release(); }
    }

    async Task<List<Row>> LoadRowsAsync()
    {
        const string cols = "MunicipalityCode, MunicipalityName, LevelCode, LevelName, CRName, EntityCode, " +
                            "NameONBallot, ListPosition, FirstName, Surname, BallotOrder";
        var list = new List<Row>(70000);
        await using var c = new SqlConnection(_conn);
        await c.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT {cols} FROM {_view}", c) { CommandTimeout = 300 };
        await using var r = await cmd.ExecuteReaderAsync();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i).Trim();
        int I(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
        while (await r.ReadAsync())
            list.Add(new Row(S(0), S(1), S(2), S(3), S(4), S(5), FixCyrillic(S(6)), I(7),
                             FixCyrillic(S(8)), FixCyrillic(S(9)), I(10)));
        return list;
    }

    /* =========================================================
       TRANSFORMACIJA
       ========================================================= */
    static Listic BuildListic(string kod, List<Row> rows, ISet<string> unknownLevels)
    {
        var levels = rows.Select(r => r.Level).Distinct().ToList();
        var munName = rows[0].MunName;

        string entitet =
            munName.Contains("BRČKO") ? "Brčko distrikt" :
            levels.Any(l => l.StartsWith('2') || l.StartsWith('4') || l.StartsWith("51")) ? "Federacija BiH" :
            levels.Any(l => l.StartsWith('3') || l == "600" || l.StartsWith("52")) ? "Republika Srpska" : "";

        var kantonLevel = levels.FirstOrDefault(l => l.StartsWith('2') && l.Length == 3);
        string? kanton = kantonLevel != null ? KantonName(int.Parse(kantonLevel[1..])) : null;

        var trke = new List<(int rank, Trka trka)>();

        // Predsjedništvo: svi 7xx nivoi u jednu trku sa kolonama
        var pres = rows.Where(r => r.Level.StartsWith('7')).GroupBy(r => r.Level).OrderBy(g => g.Key).ToList();
        if (pres.Count > 0)
        {
            var kolone = pres.Select(g =>
            {
                var (id, naslov) = g.Key switch
                {
                    "701" => ("bos", "Bošnjački član"),
                    "702" => ("hrv", "Hrvatski član"),
                    "703" => ("srp", "Srpski član"),
                    _ => ("c" + g.Key, Title(g.First().LevelName))
                };
                return new Kolona(id, naslov, Majoritarian(g));
            }).ToList();
            trke.Add((1, new Trka("predsjednistvo", "Predsjedništvo Bosne i Hercegovine",
                string.Join(" · ", kolone.Select(k => k.Naslov)), "predsjednistvo", Kolone: kolone)));
        }

        foreach (var g in rows.Where(r => !r.Level.StartsWith('7')).GroupBy(r => r.Level))
        {
            var lv = g.Key;
            var first = g.First();
            // "IZBORNA JEDINICA 1A" → "Izborna jedinica 1A"
            var ij = Regex.Replace(Sentence(first.LevelName), @"(\d)([a-z])", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
            Trka? t = null;
            int rank;

            if (lv.StartsWith('5'))
            {
                rank = 2;
                var ent = lv.StartsWith("51") ? "Federacija BiH" : lv.StartsWith("52") ? "Republika Srpska" : "";
                t = new Trka("parlament-bih", "Zastupnički/Predstavnički dom Parlamentarne skupštine BiH",
                    ent == "" ? ij : $"{ent} – {ij}", "otvorena-lista", Stranke: Parties(g));
            }
            else if (lv == "600")
            {
                rank = 3;
                t = new Trka("predsjednik-rs", "Predsjednik i potpredsjednici Republike Srpske",
                    "Izaberite jednog kandidata", "vecinski", Kandidati: Majoritarian(g));
            }
            else if (lv.StartsWith('4'))
            {
                rank = 3;
                t = new Trka("parlament-fbih", "Zastupnički/Predstavnički dom Parlamenta Federacije BiH",
                    ij, "otvorena-lista", Stranke: Parties(g));
            }
            else if (lv.StartsWith('3'))
            {
                rank = 4;
                t = new Trka("nsrs", "Narodna skupština Republike Srpske", ij, "otvorena-lista", Stranke: Parties(g));
            }
            else if (lv.StartsWith('2'))
            {
                rank = 5;
                t = new Trka("kanton", "Skupština kantona", KantonName(int.Parse(lv[1..])),
                    "otvorena-lista", Stranke: Parties(g));
            }
            else
            {
                rank = 9;
                unknownLevels.Add($"{lv} ({first.LevelName})");
                t = new Trka("trka-" + lv, Sentence(first.CR), ij, "otvorena-lista", Stranke: Parties(g));
            }
            trke.Add((rank, t));
        }

        // jedinstveni id-evi (ako ikad dođu dva nivoa iste grupe)
        var used = new HashSet<string>();
        var ordered = trke.OrderBy(x => x.rank).ThenBy(x => x.trka.Id).Select(x =>
        {
            var t = x.trka;
            return used.Add(t.Id) ? t : t with { Id = t.Id + "-" + used.Count };
        }).ToList();

        return new Listic(new IzbornaJedinica(kod, Title(munName), "opsti", entitet, kanton, null), ordered);
    }

    // Otvorena lista: stranke po redoslijedu na listiću, kandidati po poziciji na listi
    static List<Stranka> Parties(IEnumerable<Row> rows) =>
        rows.GroupBy(r => (r.Order, r.Entity))
            .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Entity)
            .Select((g, i) => new Stranka(i + 1, g.First().Party,
                g.OrderBy(r => r.Pos)
                 .Select(r => new Kandidat(r.Pos, PersonName(r)))
                 .ToList()))
            .ToList();

    // Većinski / Predsjedništvo: jedan kandidat po subjektu, redom sa listića
    static List<Kandidat> Majoritarian(IEnumerable<Row> rows) =>
        rows.OrderBy(r => r.Order).ThenBy(r => r.Entity).ThenBy(r => r.Pos)
            .Select((r, i) => new Kandidat(i + 1, PersonName(r), r.Party))
            .ToList();

    static string PersonName(Row r) => Title($"{r.First} {r.Last}", keepAbbrev: false);

    static string KantonName(int n) => n switch
    {
        1 => "Unsko-sanski kanton",
        2 => "Posavski kanton",
        3 => "Tuzlanski kanton",
        4 => "Zeničko-dobojski kanton",
        5 => "Bosansko-podrinjski kanton Goražde",
        6 => "Srednjobosanski kanton",
        7 => "Hercegovačko-neretvanski kanton",
        8 => "Zapadnohercegovački kanton",
        9 => "Kanton Sarajevo",
        10 => "Kanton 10",
        _ => "Kanton " + n
    };

    /* =========================================================
       POMOĆNE — naslovi, ćirilica, hash, diff
       ========================================================= */
    static readonly Dictionary<string, string> Abbrev = new()
    {
        ["BIH"] = "BiH", ["FBIH"] = "FBiH", ["RS"] = "RS", ["OPCIJA"] = "opcija", ["DISTRIKT"] = "distrikt", ["I"] = "i", ["NA"] = "na"
    };

    // "VELIKA KLADUŠA" → "Velika Kladuša", "BRČKO DISTRIKT BIH (OPCIJA FBIH)" → "Brčko Distrikt BiH (opcija FBiH)"
    static string Title(string s, bool keepAbbrev = true)
    {
        int idx = 0;
        return Regex.Replace(s.Trim(), @"\p{L}+", m =>
        {
            var w = m.Value.ToUpperInvariant();
            var firstWord = idx++ == 0;
            if (keepAbbrev && Abbrev.TryGetValue(w, out var a) && !(firstWord && a == w.ToLowerInvariant())) return a;
            return char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
        });
    }

    static string Sentence(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
    }

    // U bazi ima latiničnih naziva sa ćiriličnim "Ј" itd. — izgledaju isto, ali lome pretragu/sortiranje
    static readonly Dictionary<char, char> Lookalike = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['Ј'] = 'J', ['К'] = 'K', ['М'] = 'M', ['Н'] = 'H',
        ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T', ['Х'] = 'X',
        ['а'] = 'a', ['е'] = 'e', ['ј'] = 'j', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['х'] = 'x'
    };
    readonly SortedSet<string> _cyrillicFixed = new();

    string FixCyrillic(string s)
    {
        if (!s.Any(ch => ch is >= 'A' and <= 'z')) return s;          // čisto ćirilični tekst ne diramo
        if (!s.Any(ch => ch is >= 'Ѐ' and <= 'ӿ')) return s;
        var fixedS = new string(s.Select(ch => Lookalike.TryGetValue(ch, out var l) ? l : ch).ToArray());
        _cyrillicFixed.Add(fixedS);
        return fixedS;
    }

    static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();

    // Koje trke su se promijenile u odnosu na postojeći fajl
    static List<string> ChangedRaces(string oldPath, Listic nw)
    {
        try
        {
            if (!File.Exists(oldPath)) return new();
            var old = JsonNode.Parse(File.ReadAllText(oldPath))?["trke"]?.AsArray();
            var oldById = old?.ToDictionary(t => t!["id"]!.GetValue<string>(), t => t!.ToJsonString()) ?? new();
            var result = new List<string>();
            foreach (var t in nw.Trke)
            {
                var s = JsonSerializer.SerializeToNode(t, Json)!.ToJsonString();
                if (!oldById.TryGetValue(t.Id, out var o) || o != s) result.Add(t.Naslov);
                oldById.Remove(t.Id);
            }
            result.AddRange(oldById.Keys.Select(k => k + " (uklonjeno)"));
            if (result.Count == 0) result.Add("Podaci općine (naziv, entitet, kanton)");
            return result;
        }
        catch { return new(); }
    }
}
