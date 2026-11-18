using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;

namespace Spobras.Celular.Servicos;

/// <summary>Tempos de uma foto da camera (milissegundos), para achar o passo lento num celular de verdade.</summary>
public sealed record TempoDaFoto(long Id, DateTimeOffset Quando, int Quadro, int Codificar, int Gravar, int Kb, int Largura, int Altura)
{
    /// <summary>Do envio pela camera ate o rascunho responder (passar a foto para o app e gravar).</summary>
    public int Enviar { get; init; }
}

/// <summary>
/// Recebe as fotos da camera (a pagina da camera chama estes metodos). Fica no app inteiro, nao na tela: uma foto que
/// termina de salvar depois de fechar a camera continua chegando ao rascunho e a galeria.
/// </summary>
public sealed class FotosDaCamera(AppCelular app)
{
    private readonly List<TempoDaFoto> _tempos = [];

    /// <summary>Ultimas 20 fotos (diagnostico nas Configuracoes), da mais antiga para a mais nova.</summary>
    public IReadOnlyList<TempoDaFoto> Tempos
    {
        get
        {
            lock (_tempos)
            {
                return [.. _tempos];
            }
        }
    }

    /// <summary>Quantas fotos podem estar salvando ao mesmo tempo antes de a camera pedir para esperar.</summary>
    public int MaximoNaFila => app.Plataforma.FotosAoMesmoTempo <= 1 ? 3 : 6;

    [JSInvokable]
    public void FotoPega(long id, string previa) => app.Rascunho.ComecarASalvar(id, previa);

    [JSInvokable]
    public async Task FotoTirada(long id, byte[] jpeg, int quadro, int codificar)
    {
        var (foto, gravar) = await Task.Run(() =>
        {
            var inicio = System.Diagnostics.Stopwatch.StartNew();
            var f = app.Rascunho.AdicionarFoto(jpeg, id);
            return (f, (int)inicio.ElapsedMilliseconds);
        });
        lock (_tempos)
        {
            _tempos.Add(new TempoDaFoto(id, app.Agora(), quadro, codificar, gravar, jpeg.Length / 1024, foto.Largura ?? 0, foto.Altura ?? 0));
            if (_tempos.Count > 20) _tempos.RemoveAt(0);
        }

        if (app.Config.FotosNaGaleria)
        {
            app.Galeria.Enfileirar(foto, app.Config.Carimbo);
        }
    }

    /// <summary>A camera mediu o tempo de ida e volta da foto ate o app.</summary>
    [JSInvokable]
    public void Enviado(long id, int ms)
    {
        lock (_tempos)
        {
            int i = _tempos.FindIndex(t => t.Id == id);
            if (i >= 0) _tempos[i] = _tempos[i] with { Enviar = ms };
        }
    }

    [JSInvokable]
    public void FalhaAoTirar(long id, string motivo) => app.Rascunho.FalhouAoSalvar(id, motivo);

    private readonly ConditionalWeakTable<IJSRuntime, DotNetObjectReference<FotosDaCamera>> _referencias = new();

    /// <summary>Uma referencia so por pagina do app (nunca descartada com a tela da camera).</summary>
    public DotNetObjectReference<FotosDaCamera> Referencia(IJSRuntime js) => _referencias.GetValue(js, _ => DotNetObjectReference.Create(this));
}

/// <summary>
/// Guarda na galeria do celular as fotos tiradas pelo app (quando a opcao esta ligada), uma por vez, em segundo plano,
/// sem atrasar a camera. Com o carimbo ligado no momento da foto, vai a foto carimbada em tamanho cheio; a original sem
/// carimbo fica no rascunho. Se o endereco ainda estiver sendo lido, espera ate 30 s por ele.
/// </summary>
public sealed class FilaDaGaleria(AppCelular app)
{
    public static readonly TimeSpan EsperaDoEndereco = TimeSpan.FromSeconds(30);

    private sealed record Item(FotoRascunho Foto, string Caminho, AjusteCarimbo? Ajuste, DateTimeOffset Entrou);

    private readonly ConcurrentQueue<Item> _fila = new();
    private int _trabalhando;

    public int Pendentes => _fila.Count + Volatile.Read(ref _trabalhando);

    public void Enfileirar(FotoRascunho foto, AjusteCarimbo? ajuste)
    {
        _fila.Enqueue(new Item(foto, app.Rascunho.Caminho(foto), ajuste is { Ligado: true } ? ajuste : null, app.Agora()));
        if (Interlocked.CompareExchange(ref _trabalhando, 1, 0) == 0)
        {
            _ = Task.Run(ProcessarAsync);
        }
    }

    /// <summary>Espera a fila esvaziar (testes).</summary>
    public async Task VaziaAsync(TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while (Pendentes > 0 && DateTime.UtcNow < fim)
        {
            await Task.Delay(20);
        }
    }

    private async Task ProcessarAsync()
    {
        try
        {
            while (_fila.TryDequeue(out var item))
            {
                await GuardarAsync(item);
            }
        }
        finally
        {
            Volatile.Write(ref _trabalhando, 0);
            if (!_fila.IsEmpty && Interlocked.CompareExchange(ref _trabalhando, 1, 0) == 0)
            {
                _ = Task.Run(ProcessarAsync);
            }
        }
    }

    private async Task GuardarAsync(Item item)
    {
        string? temp = null;
        try
        {
            if (!File.Exists(item.Caminho))
            {
                return; // saiu do rascunho antes de chegar a vez (Novo relatorio)
            }

            string arquivo = item.Caminho;
            if (item.Ajuste is { } ajuste)
            {
                if (EnderecoCelular.Usa(ajuste))
                {
                    // o endereco ainda esta sendo lido: espera um pouco por ele
                    while (app.Endereco.Lendo && app.Agora() - item.Entrou < EsperaDoEndereco)
                    {
                        await Task.Delay(300);
                    }
                }

                var d = app.Rascunho.Dados;
                if (CarimboFoto.Montar(ajuste, d.Unidade, d.Endereco, item.Foto.CapturadaEm) is { } carimbo)
                {
                    string pasta = Path.Combine(app.Plataforma.PastaDoApp, "galeria-temp");
                    Directory.CreateDirectory(pasta);
                    temp = Path.Combine(pasta, Path.GetFileName(item.Caminho));
                    byte[] carimbada = Fotos.Preparar(item.Caminho, 20000, 92, 0, carimbo); // tamanho cheio
                    await File.WriteAllBytesAsync(temp, carimbada);
                    arquivo = temp;
                }
            }

            await app.Plataforma.GuardarNaGaleriaAsync(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FotoInvalidaException or InvalidOperationException)
        {
            // a galeria e uma copia a mais: a foto continua segura no rascunho
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp); // copia temporaria do proprio app
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
