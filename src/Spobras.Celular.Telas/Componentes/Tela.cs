using Microsoft.AspNetCore.Components;
using Spobras.Celular.Servicos;

namespace Spobras.Celular.Componentes;

/// <summary>Base das telas: acesso ao app, navegacao e execucao de tarefas com "aguarde" e erro amigavel.</summary>
public abstract class Tela : ComponentBase, IDisposable
{
    [Inject] protected AppCelular App { get; set; } = default!;
    [Inject] protected NavigationManager Navegacao { get; set; } = default!;

    protected string? Trabalhando { get; private set; }
    protected string? Erro { get; set; }

    protected override void OnInitialized()
    {
        App.Base.Mudou += AoMudarBase;
        Iniciar();
    }

    /// <summary>Inicializacao de cada tela (no lugar do OnInitialized).</summary>
    protected virtual void Iniciar()
    {
    }

    /// <summary>A base foi relida ou mudou de estado.</summary>
    protected virtual void BaseMudou()
    {
    }

    private void AoMudarBase() => InvokeAsync(() =>
    {
        BaseMudou();
        StateHasChanged();
    });

    public virtual void Dispose()
    {
        App.Base.Mudou -= AoMudarBase;
        GC.SuppressFinalize(this);
    }

    protected void Ir(string rota) => Navegacao.NavigateTo(rota);

    protected async Task Executar(string texto, Func<Task> tarefa)
    {
        Erro = null;
        Trabalhando = texto;
        StateHasChanged();
        try
        {
            await Task.Yield();
            await tarefa();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
                                       or Spobras.Documentos.Pdf.PdfInvalidoException or Spobras.Documentos.Imagens.FotoInvalidaException)
        {
            Erro = ex.Message;
        }
        finally
        {
            Trabalhando = null;
            StateHasChanged();
        }
    }
}
