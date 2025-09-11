# Laboratório C# - Manipulação de Arquivos e Programação Assíncrona

Este projeto é uma evolução do laboratório anterior (`AsyncLab`), focado em aprimorar o uso de programação assíncrona com `async/await` e adicionar funcionalidades avançadas de manipulação de arquivos, incluindo comparação de dados (diff), armazenamento em formato binário e um sistema de busca interativo.

## 👥 Membros do Grupo

*   Nikolas Rodrigues Moura dos Santos - RM: 551566
*   Thiago Jardim de Oliveira - RM: 551624
*   Rodrigo Brasileiro - RM: 98952
*   Guilherme Rocha Bianchini - RM: 97974
*   Pedro Pedrosa Tavares - RM: 97877

## 🛠️ Descrição das Modificações Realizadas

O projeto foi extensivamente refatorado para seguir melhores práticas de arquitetura e performance. As principais alterações são:

### 1. Arquitetura e Refatoração

*   **Separação de Responsabilidades:** Foi criada a classe `MunicipioService` para encapsular toda a lógica de negócio (download, processamento, salvamento, busca). O arquivo `Program.cs` agora atua apenas como um "orquestrador", tornando o código mais limpo, organizado e fácil de manter, de acordo com o Princípio da Responsabilidade Única (SRP).

### 2. Download e Comparação de Arquivos (Diff)

*   **Download Assíncrono com `HttpClient`:** Substituímos o obsoleto `WebClient` por `HttpClient`, a abordagem moderna e mais performática para operações de rede em .NET. O download é feito de forma totalmente assíncrona para não bloquear a aplicação.
*   **Comparação Eficiente:** Se um arquivo local existe, o novo é baixado e comparado com o antigo. Para otimizar a comparação, as linhas do arquivo antigo são carregadas em um `HashSet<string>`, permitindo uma verificação de diferenças com complexidade de tempo média O(1), o que é extremamente rápido.
*   **Geração de Arquivo de Diferenças:** Caso existam diferenças, um novo arquivo `diferencas.csv` é gerado contendo apenas as linhas novas ou modificadas.
*   **Gerenciamento Robusto de Arquivos:** O processo de download, leitura e movimentação de arquivos foi reestruturado para evitar conflitos de acesso (`IOException`). **O fluxo agora garante que os handles de arquivo sejam liberados corretamente antes de operações subsequentes e utiliza um bloco `try...finally` para garantir a limpeza de arquivos temporários, mesmo em caso de falha.**

### 3. Armazenamento em Formato Binário

*   **Eficiência de Espaço e Velocidade:** Em vez de salvar os dados processados em formato de texto (CSV/JSON), implementamos o salvamento em formato binário (`.bin`) usando `BinaryWriter` sobre um `FileStream` assíncrono.
*   **Vantagens do Binário:**
    *   **Arquivos Menores:** Ocupam menos espaço em disco.
    *   **Leitura/Escrita Rápidas:** A serialização/desserialização é muito mais rápida, pois não envolve o *parsing* (interpretação) de texto.
*   **Estrutura do Arquivo:** Cada arquivo `.bin` (um por UF) inicia com um `int` que representa o número total de registros, seguido pelos dados de cada município escritos sequencialmente.

### 4. Sistema de Busca Interativo e Assíncrono

*   **Menu Interativo:** Após o processamento, o programa entra em um loop interativo que permite ao usuário realizar buscas nos dados gerados.
*   **Tipos de Busca:** É possível buscar municípios por:
    1.  **UF:** Carrega o arquivo binário específico da UF.
    2.  **Parte do Nome:** Realiza uma busca em todos os arquivos `.bin` de forma paralela.
    3.  **Código IBGE:** Também busca em todos os arquivos em paralelo.
*   **Leitura Assíncrona:** A leitura dos arquivos `.bin` para a busca é feita com `BinaryReader` e `FileStream` configurado para operações assíncronas, garantindo que a interface do console permaneça responsiva.
*   **Busca Paralela com `Task.WhenAll`:** Para as buscas por nome e IBGE, que podem envolver múltiplos arquivos, as tarefas de leitura e filtragem são disparadas em paralelo e aguardadas com `Task.WhenAll`. Isso acelera significativamente a busca, aproveitando a natureza assíncrona do I/O de disco.

## 📊 Observações sobre os Impactos no Tempo de Execução

O uso intensivo de `async/await` e paralelismo tem impactos distintos e significativos na performance do programa:

1.  **Operações I/O-Bound (Download e Leitura/Escrita de Arquivos):**
    *   O `async/await` aqui não necessariamente torna a operação individual *mais rápida* (o disco ou a rede têm sua velocidade limite), mas melhora drasticamente a **escalabilidade e a responsividade** do programa.
    *   Enquanto o programa aguarda o término de uma operação de I/O, a thread de execução é liberada de volta para o *ThreadPool* e pode ser usada para outras tarefas. Isso evita o desperdício de recursos e o bloqueio da aplicação, o que seria crítico em uma aplicação com interface de usuário ou em um servidor web.

2.  **Operações CPU-Bound (Cálculo de Hashes):**
    *   A tarefa de calcular os hashes PBKDF2 é computacionalmente intensiva. Para esta parte, o uso de `Parallel.ForEachAsync` distribui o trabalho entre os múltiplos núcleos da CPU.
    *   Isso resulta em **paralelismo real**, diminuindo drasticamente o "tempo de relógio" (wall-clock time) necessário para completar todos os cálculos. Em uma máquina com 8 núcleos, essa etapa pode ser teoricamente até 8 vezes mais rápida do que uma execução sequencial.

3.  **Busca Interativa:**
    *   A combinação de leitura assíncrona de arquivos e o uso de `Task.WhenAll` para buscar em múltiplos arquivos simultaneamente torna a experiência do usuário muito mais rápida. Em vez de ler e processar cada arquivo de UF um após o outro (sequencialmente), o programa dispara todas as operações de leitura/busca ao mesmo tempo, e o tempo total da busca se aproxima do tempo da operação mais demorada, em vez da soma de todas.

**Conclusão:** A refatoração para um modelo assíncrono e paralelo tornou o programa mais robusto, eficiente no uso de recursos e significativamente mais rápido em tarefas que podem ser paralelizadas, como o cálculo de hashes e as buscas em múltiplos arquivos.

## 🚀 Como Executar

1.  Clone o repositório `forkado`.
2.  Abra o projeto em sua IDE de preferência (Visual Studio, VS Code, Rider).
3.  Execute o projeto (geralmente pressionando F5 ou usando o comando `dotnet run`).
4.  O programa irá baixar os dados, processá-los e, em seguida, apresentar o menu de busca interativa no console.
