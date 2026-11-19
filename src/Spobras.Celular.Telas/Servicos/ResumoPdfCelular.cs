using Spobras.Core.Base;
using Spobras.Documentos.Relatorio;

namespace Spobras.Celular.Servicos;

public sealed record ResumoPdfSalvo(string Titulo, Salvo Arquivo, string Local)
{
    /// <summary>Planilha (exportacao das bases sem contrato) em vez de PDF.</summary>
    public bool Planilha => Local.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public void Limpar() => Directory.Delete(Path.GetDirectoryName(Local)!, true);
}

public sealed class ResumoPdfCelular(AppCelular app)
{
    private int _gerando;

    public async Task<ResumoPdfSalvo?> SalvarAsync(IReadOnlyList<ResumoContrato> contratos, EscolaResumo? escola = null,
        CancellationToken cancelar = default)
    {
        if (Interlocked.Exchange(ref _gerando, 1) != 0) return null;
        string? pastaTemporaria = null;
        bool entregue = false;
        try
        {
            if (escola is null && contratos.Count == 0) return null;
            var pasta = app.Config.Pasta;
            if (pasta is null)
            {
                pasta = await app.Plataforma.EscolherPastaAsync();
                if (pasta is null || cancelar.IsCancellationRequested) return null;
                app.Configurar(p => p with { Pasta = pasta });
            }

            var hora = app.Agora();
            var leitura = app.Base.UltimaLeitura;
            var logo = app.Config.Logo;
            var nome = escola is null ? GeradorResumoContratoPdf.Nome(contratos, hora) : GeradorResumoContratoPdf.Nome(escola, hora);
            pastaTemporaria = Path.Combine(app.Plataforma.PastaDoApp, "resumos", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pastaTemporaria);
            string local = Path.Combine(pastaTemporaria, nome);
            await Task.Run(() =>
            {
                if (escola is null) GeradorResumoContratoPdf.Gerar(local, contratos, hora, leitura, logo);
                else GeradorResumoContratoPdf.Gerar(local, escola, hora, leitura, logo);
            }, cancelar);
            cancelar.ThrowIfCancellationRequested();
            var salvo = await app.Plataforma.SalvarAsync(pasta.Id, null, local);
            app.Registrar([salvo]);
            cancelar.ThrowIfCancellationRequested();
            entregue = true;
            return new(escola is not null ? "Resumo da obra" : contratos.Count == 1 ? "Resumo do contrato" : "Resumo dos contratos", salvo, local);
        }
        finally
        {
            Volatile.Write(ref _gerando, 0);
            if (!entregue && pastaTemporaria is not null) Directory.Delete(pastaTemporaria, true);
        }
    }

    /// <summary>Planilha das obras Em licitação ou A licitar do filtro, no modelo da base (o mesmo do computador).</summary>
    public async Task<ResumoPdfSalvo?> ExportarPrevistasAsync(BaseDeObras qual, IReadOnlyList<ObraPrevista> obras, FiltroPrevistas filtro,
        CancellationToken cancelar = default)
    {
        if (obras.Count == 0 || Interlocked.Exchange(ref _gerando, 1) != 0) return null;
        string? pastaTemporaria = null;
        bool entregue = false;
        try
        {
            var pasta = app.Config.Pasta;
            if (pasta is null)
            {
                pasta = await app.Plataforma.EscolherPastaAsync();
                if (pasta is null || cancelar.IsCancellationRequested) return null;
                app.Configurar(p => p with { Pasta = pasta });
            }

            var hora = app.Agora();
            var logo = app.Config.Logo;
            pastaTemporaria = Path.Combine(app.Plataforma.PastaDoApp, "resumos", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pastaTemporaria);
            string local = Path.Combine(pastaTemporaria, Spobras.Documentos.Planilhas.ExportacaoPrevistas.Nome(qual, hora));
            await Task.Run(() => Spobras.Documentos.Planilhas.ExportacaoPrevistas.Gravar(local, qual, obras, filtro, hora, logo), cancelar);
            cancelar.ThrowIfCancellationRequested();
            var salvo = await app.Plataforma.SalvarAsync(pasta.Id, null, local);
            app.Registrar([salvo]);
            cancelar.ThrowIfCancellationRequested();
            entregue = true;
            return new($"Consulta · {BasesDeObras.Nome(qual)}", salvo, local);
        }
        finally
        {
            Volatile.Write(ref _gerando, 0);
            if (!entregue && pastaTemporaria is not null) Directory.Delete(pastaTemporaria, true);
        }
    }
}
