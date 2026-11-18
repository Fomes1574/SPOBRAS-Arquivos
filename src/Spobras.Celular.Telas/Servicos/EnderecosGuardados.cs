using System.Text.Json;
using Spobras.Core.Relatorio;

namespace Spobras.Celular.Servicos;

/// <summary>Endereco guardado de uma escola (nome + contrato), com o ponto do GPS quando veio dele.</summary>
public sealed record EnderecoGuardado(string Chave, string Unidade, string Contrato, string Endereco, PontoGps? Ponto, DateTimeOffset GuardadoEm, bool Digitado = false);

/// <summary>
/// Enderecos das escolas onde este celular ja tirou foto com carimbo: um arquivo pequeno (enderecos.json, uns 250
/// bytes por escola) na pasta do app. So entra escola visitada; a planilha nao e copiada. Gravado como o rascunho
/// (arquivo novo e depois troca, com copia da versao anterior). Se o arquivo se perder, so some o atalho: o GPS le de novo.
/// </summary>
public sealed class EnderecosGuardados(string pastaDoApp)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly string _arquivo = Path.Combine(pastaDoApp, "enderecos.json");
    private readonly object _trava = new();
    private Dictionary<string, EnderecoGuardado>? _todos;

    private string Copia => _arquivo + ".bak";

    public int Quantos
    {
        get
        {
            lock (_trava)
            {
                return Todos().Count;
            }
        }
    }

    private Dictionary<string, EnderecoGuardado> Todos()
    {
        if (_todos is not null) return _todos;
        foreach (string arquivo in new[] { _arquivo, Copia })
        {
            try
            {
                if (File.Exists(arquivo) && JsonSerializer.Deserialize<List<EnderecoGuardado>>(File.ReadAllText(arquivo), Json) is { } lista)
                {
                    return _todos = lista
                        .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Chave) && !string.IsNullOrWhiteSpace(e.Endereco))
                        .GroupBy(e => e.Chave).ToDictionary(g => g.Key, g => g.Last());
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // estragado: tenta a copia; sem nenhuma, comeca vazio (o GPS le de novo)
            }
        }

        return _todos = [];
    }

    /// <summary>Endereco guardado desta escola com este contrato.</summary>
    public EnderecoGuardado? Buscar(string? chave)
    {
        if (chave is null) return null;
        lock (_trava)
        {
            return Todos().GetValueOrDefault(chave);
        }
    }

    /// <summary>Escolas com o mesmo nome e outro contrato (podem ser outro predio: o app pergunta, nunca usa sozinho).</summary>
    public IReadOnlyList<EnderecoGuardado> MesmoNome(string? chave)
    {
        if (chave is null) return [];
        string nome = EnderecoDasFotos.NomeDaChave(chave);
        lock (_trava)
        {
            return [.. Todos().Values.Where(e => e.Chave != chave && EnderecoDasFotos.NomeDaChave(e.Chave) == nome).OrderByDescending(e => e.GuardadoEm)];
        }
    }

    public void Guardar(EnderecoGuardado endereco)
    {
        lock (_trava)
        {
            var todos = Todos();
            todos[endereco.Chave] = endereco;
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
            string temp = _arquivo + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(todos.Values.ToList(), Json));
            if (File.Exists(_arquivo))
            {
                File.Copy(_arquivo, Copia, overwrite: true);
            }

            File.Move(temp, _arquivo, overwrite: true);
        }
    }
}
