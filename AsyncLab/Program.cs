// Arquivo: Program.cs

public class Program
{
    public static async Task Main(string[] args)
    {
        var service = new MunicipioService();

        // 1. Processa os dados (baixa, compara, calcula hash e salva em binário)
        await service.ProcessarMunicipiosAsync();

        // 2. Inicia o modo de busca interativa
        await service.IniciarBuscaInterativaAsync();

        Console.WriteLine("\nPrograma finalizado. Pressione qualquer tecla para sair.");
        Console.ReadKey();
    }
}