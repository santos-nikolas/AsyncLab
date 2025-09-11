// Arquivo: MunicipioService.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

public class MunicipioService
{
    private const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
    private const string OUT_DIR_NAME = "dados_binarios_por_uf";
    private readonly string _baseDir = Directory.GetCurrentDirectory();
    private readonly string _outRootDir;
    private readonly string _localCsvPath;

    public MunicipioService()
    {
        _localCsvPath = Path.Combine(_baseDir, "municipios.csv");
        _outRootDir = Path.Combine(_baseDir, OUT_DIR_NAME);
    }

    public async Task ProcessarMunicipiosAsync()
    {
        var sw = Stopwatch.StartNew();
        Console.WriteLine("Iniciando processamento de municípios...");

        // Atividade 1 e 2: Verificar, baixar e comparar arquivos
        var linhas = await ObterDadosCsvAsync();
        if (linhas.Length == 0)
        {
            Console.WriteLine("Nenhum dado para processar.");
            return;
        }

        // Parsear os dados do CSV
        var municipios = ParseCsv(linhas);
        Console.WriteLine($"Total de {municipios.Count} municípios lidos e parseados.");

        var porUf = municipios
            .GroupBy(m => m.Uf, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.Equals(g.Key, "EX", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Atividade 3: Salvar arquivos por UF em formato binário
        await SalvarDadosBinariosPorUfAsync(porUf);

        sw.Stop();
        Console.WriteLine("\n===== PROCESSAMENTO CONCLUÍDO =====");
        Console.WriteLine($"Pasta de saída: {_outRootDir}");
        Console.WriteLine($"Tempo total de processamento: {FormatTempo(sw.ElapsedMilliseconds)}");
    }

    private async Task<string[]> ObterDadosCsvAsync()
    {
        Console.WriteLine("Verificando arquivo de municípios...");
        string tempDownloadPath = Path.Combine(_baseDir, "municipios_temp.csv");

        try
        {
            // 1. Baixar a versão mais recente para um arquivo temporário
            using (var httpClient = new HttpClient())
            {
                Console.WriteLine("Baixando versão mais recente do CSV...");
                // O 'await using' garante que o stream de rede e o de arquivo sejam fechados ao final do bloco
                await using var networkStream = await httpClient.GetStreamAsync(CSV_URL);
                await using var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await networkStream.CopyToAsync(fileStream);
            } // <--- NESTE PONTO, o arquivo municipios_temp.csv é fechado e liberado pelo sistema.

            // 2. AGORA é seguro ler o conteúdo do arquivo temporário para a memória
            string[] novasLinhas = await File.ReadAllLinesAsync(tempDownloadPath, Encoding.UTF8);

            // 3. Comparar com a versão local, se existir
            if (File.Exists(_localCsvPath))
            {
                Console.WriteLine("Arquivo local encontrado. Comparando com a nova versão...");
                var linhasAntigas = await File.ReadAllLinesAsync(_localCsvPath, Encoding.UTF8);

                var setLinhasAntigas = new HashSet<string>(linhasAntigas);
                var diferencas = novasLinhas.Where(nova => !setLinhasAntigas.Contains(nova)).ToList();

                if (diferencas.Any())
                {
                    string diffPath = Path.Combine(_baseDir, "diferencas.csv");
                    await File.WriteAllLinesAsync(diffPath, diferencas, Encoding.UTF8);
                    Console.WriteLine($"Foram encontradas {diferencas.Count} diferenças. Salvas em '{diffPath}'.");
                }
                else
                {
                    Console.WriteLine("Nenhuma diferença encontrada. O arquivo local está atualizado.");
                }

                File.Delete(_localCsvPath);
            }
            else
            {
                Console.WriteLine("Arquivo local não encontrado. Apenas salvando a nova versão.");
            }

            File.Move(tempDownloadPath, _localCsvPath);
            Console.WriteLine($"Arquivo '{Path.GetFileName(_localCsvPath)}' atualizado.");

            // Retorna os dados que já estão em memória, evitando outra leitura do disco
            return novasLinhas;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ocorreu um erro crítico durante o download/comparação: {ex.Message}");
            return Array.Empty<string>();
        }
        finally
        {
            // Bloco de limpeza: garante que o arquivo temporário seja deletado, mesmo em caso de erro
            if (File.Exists(tempDownloadPath))
            {
                File.Delete(tempDownloadPath);
            }
        }
    }

    private List<Municipio> ParseCsv(string[] linhas)
    {
        int startIndex = linhas[0].Contains("IBGE", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var municipios = new List<Municipio>(linhas.Length - startIndex);

        for (int i = startIndex; i < linhas.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(linhas[i])) continue;
            var parts = linhas[i].Split(';');
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
        return municipios;
    }

    private async Task SalvarDadosBinariosPorUfAsync(Dictionary<string, List<Municipio>> porUf)
    {
        Directory.CreateDirectory(_outRootDir);
        Console.WriteLine("\nCalculando hashes e salvando arquivos binários por UF...");

        var ufsOrdenadas = porUf.Keys.OrderBy(uf => uf).ToList();

        foreach (var uf in ufsOrdenadas)
        {
            var swUf = Stopwatch.StartNew();
            var listaUf = porUf[uf];
            listaUf.Sort((a, b) => string.Compare(a.NomePreferido, b.NomePreferido, StringComparison.OrdinalIgnoreCase));

            Console.WriteLine($"  -> Processando UF: {uf} ({listaUf.Count} municípios)");

            // Calcula os hashes em paralelo para aproveitar múltiplos núcleos
            var bagComHash = new ConcurrentBag<Municipio>();
            await Parallel.ForEachAsync(listaUf, (m, ct) =>
            {
                byte[] salt = Util.BuildSalt(m.Ibge);
                m.Hash = Util.DeriveHashHex(m.ToConcatenatedString(), salt, 10000, 16); // Reduzido para agilidade
                bagComHash.Add(m);
                return ValueTask.CompletedTask;
            });

            var municipiosCompletos = bagComHash.OrderBy(m => m.NomePreferido).ToList();

            // Escreve os dados em um arquivo binário
            string outPath = Path.Combine(_outRootDir, $"municipios_{uf}.bin");
            await using var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await using var writer = new BinaryWriter(fileStream, Encoding.UTF8, false);

            writer.Write(municipiosCompletos.Count); // Escreve o número de registros no início
            foreach (var m in municipiosCompletos)
            {
                writer.Write(m.Tom);
                writer.Write(m.Ibge);
                writer.Write(m.NomeTom);
                writer.Write(m.NomeIbge);
                writer.Write(m.Uf);
                writer.Write(m.Hash);
            }

            swUf.Stop();
            Console.WriteLine($"     UF {uf} concluída em {FormatTempo(swUf.ElapsedMilliseconds)}");
        }
    }

    // Atividade 4: Métodos de busca
    public async Task IniciarBuscaInterativaAsync()
    {
        Console.WriteLine("\n===== INICIANDO MODO DE BUSCA INTERATIVA =====");

        while (true)
        {
            Console.WriteLine("\nEscolha o tipo de busca:");
            Console.WriteLine("1. Por UF (ex: SP)");
            Console.WriteLine("2. Por parte do nome (ex: campinas)");
            Console.WriteLine("3. Por código IBGE (ex: 3509502)");
            Console.WriteLine("4. Sair");
            Console.Write("> ");

            string? escolha = Console.ReadLine();

            switch (escolha)
            {
                case "1":
                    await BuscarPorUfAsync();
                    break;
                case "2":
                    await BuscarPorNomeAsync();
                    break;
                case "3":
                    await BuscarPorIbgeAsync();
                    break;
                case "4":
                    return;
                default:
                    Console.WriteLine("Opção inválida. Tente novamente.");
                    break;
            }
        }
    }

    private async Task<List<Municipio>> CarregarMunicipiosDeArquivoBinarioAsync(string path)
    {
        var municipios = new List<Municipio>();
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var reader = new BinaryReader(fs, Encoding.UTF8, false);

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                municipios.Add(new Municipio
                {
                    Tom = reader.ReadString(),
                    Ibge = reader.ReadString(),
                    NomeTom = reader.ReadString(),
                    NomeIbge = reader.ReadString(),
                    Uf = reader.ReadString(),
                    Hash = reader.ReadString()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao ler arquivo {path}: {ex.Message}");
        }
        return municipios;
    }

    private async Task BuscarPorUfAsync()
    {
        Console.Write("Digite a UF: ");
        string? uf = Console.ReadLine()?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(uf)) return;

        string path = Path.Combine(_outRootDir, $"municipios_{uf}.bin");
        if (!File.Exists(path))
        {
            Console.WriteLine($"Nenhum dado encontrado para a UF '{uf}'.");
            return;
        }

        var resultados = await CarregarMunicipiosDeArquivoBinarioAsync(path);
        ExibirResultados(resultados, $"Resultados para a UF '{uf}'");
    }

    private async Task BuscarPorNomeAsync()
    {
        Console.Write("Digite parte do nome do município: ");
        string? termo = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(termo)) return;

        var todosArquivos = Directory.GetFiles(_outRootDir, "municipios_*.bin");
        var tarefasBusca = todosArquivos.Select(async path =>
        {
            var municipios = await CarregarMunicipiosDeArquivoBinarioAsync(path);
            return municipios.Where(m => m.NomePreferido.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
        });

        var resultadosPorArquivo = await Task.WhenAll(tarefasBusca);
        var resultadosFinais = resultadosPorArquivo.SelectMany(r => r).OrderBy(m => m.NomePreferido).ToList();

        ExibirResultados(resultadosFinais, $"Resultados para a busca por nome '{termo}'");
    }

    private async Task BuscarPorIbgeAsync()
    {
        Console.Write("Digite o código IBGE: ");
        string? codigo = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(codigo)) return;

        var todosArquivos = Directory.GetFiles(_outRootDir, "municipios_*.bin");
        var tarefasBusca = todosArquivos.Select(async path =>
        {
            var municipios = await CarregarMunicipiosDeArquivoBinarioAsync(path);
            return municipios.FirstOrDefault(m => m.Ibge == codigo);
        });

        var resultadosPorArquivo = await Task.WhenAll(tarefasBusca);
        var resultadosFinais = resultadosPorArquivo.Where(r => r != null).ToList();

        ExibirResultados(resultadosFinais!, $"Resultados para a busca por IBGE '{codigo}'");
    }

    private void ExibirResultados<T>(List<T> resultados, string titulo)
    {
        Console.WriteLine($"\n--- {titulo} ({resultados.Count} encontrados) ---");
        if (resultados.Any())
        {
            foreach (var item in resultados)
            {
                Console.WriteLine(item);
            }
        }
        else
        {
            Console.WriteLine("Nenhum município corresponde aos critérios de busca.");
        }
        Console.WriteLine("------------------------------------------");
    }

    private string FormatTempo(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
    }
}