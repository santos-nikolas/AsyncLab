using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

const int PBKDF2_ITERATIONS = 50_000;
const int HASH_BYTES = 32;
const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
const string OUT_DIR_NAME = "mun_hash_por_uf";

string FormatTempo(long ms)
{
    var ts = TimeSpan.FromMilliseconds(ms);
    return $"{ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
}

var sw = Stopwatch.StartNew();
string baseDir = Directory.GetCurrentDirectory();
string tempCsvPath = Path.Combine(baseDir, "municipios.csv");
string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);

Console.WriteLine("Baixando CSV de municípios (Receita Federal) ...");
using (var wc = new WebClient())
{
    wc.Encoding = Encoding.UTF8;
    await wc.DownloadFileTaskAsync(CSV_URL, tempCsvPath);
}

Console.WriteLine("Lendo e parseando o CSV ...");
var linhas = await File.ReadAllLinesAsync(tempCsvPath, Encoding.UTF8);

if (linhas.Length == 0)
{
    Console.WriteLine("Arquivo CSV vazio.");
    return;
}

int startIndex = 0;
if (linhas[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
    linhas[0].IndexOf("UF", StringComparison.OrdinalIgnoreCase) >= 0)
{
    startIndex = 1;
}

var municipios = new List<Municipio>(linhas.Length - startIndex);
for (int i = startIndex; i < linhas.Length; i++)
{
    var linha = (linhas[i] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(linha)) continue;
    var parts = linha.Split(';');
    if (parts.Length < 5) continue;

    municipios.Add(new Municipio
    {
        Tom = Util.San(parts[0]),
        Ibge = Util.San(parts[1]),
        NomeTom = Util.San(parts[2]),
        NomeIbge = Util.San(parts[3]),
        Uf = Util.San(parts[4]).ToUpperInvariant()
    });
}

Console.WriteLine($"Registros lidos: {municipios.Count}");

var porUf = municipios.GroupBy(m => m.Uf, StringComparer.OrdinalIgnoreCase)
                      .Where(g => !string.Equals(g.Key, "EX", StringComparison.OrdinalIgnoreCase))
                      .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

var ufsOrdenadas = porUf.Keys.OrderBy(uf => uf, StringComparer.OrdinalIgnoreCase).ToList();

Directory.CreateDirectory(outRoot);
Console.WriteLine("Calculando hash por município e gerando arquivos por UF ...");

foreach (var uf in ufsOrdenadas)
{
    var listaUf = porUf[uf];
    listaUf.Sort((a, b) => string.Compare(a.NomePreferido, b.NomePreferido, StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"Processando UF: {uf} ({listaUf.Count} municípios)");
    var swUf = Stopwatch.StartNew();

    string outPath = Path.Combine(outRoot, $"municipios_hash_{uf}.csv");
    var listaJson = new ConcurrentBag<object>();
    var linhasCsv = new ConcurrentBag<string>();

    linhasCsv.Add("TOM;IBGE;NomeTOM;NomeIBGE;UF;Hash");

    await Parallel.ForEachAsync(listaUf, async (m, ct) =>
    {
        string password = m.ToConcatenatedString();
        byte[] salt = Util.BuildSalt(m.Ibge);
        string hashHex = Util.DeriveHashHex(password, salt, PBKDF2_ITERATIONS, HASH_BYTES);

        linhasCsv.Add($"{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf};{hashHex}");
        listaJson.Add(new { m.Tom, m.Ibge, m.NomeTom, m.NomeIbge, m.Uf, Hash = hashHex });
    });

    await File.WriteAllLinesAsync(outPath, linhasCsv.OrderBy(l => l), Encoding.UTF8);

    string jsonPath = Path.Combine(outRoot, $"municipios_hash_{uf}.json");
    var json = JsonSerializer.Serialize(listaJson.OrderBy(o => o.ToString()), new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8);

    swUf.Stop();
    Console.WriteLine($"UF {uf} concluída. Tempo total UF: {FormatTempo(swUf.ElapsedMilliseconds)}");
}

sw.Stop();
Console.WriteLine();
Console.WriteLine("===== RESUMO =====");
Console.WriteLine($"UFs geradas: {ufsOrdenadas.Count}");
Console.WriteLine($"Pasta de saída: {outRoot}");
Console.WriteLine($"Tempo total: {FormatTempo(sw.ElapsedMilliseconds)} ({sw.Elapsed})");