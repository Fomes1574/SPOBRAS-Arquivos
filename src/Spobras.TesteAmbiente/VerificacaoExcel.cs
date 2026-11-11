using System.Diagnostics;
using Spobras.Core.Ambiente;
using Spobras.Documentos.Ambiente;
using Spobras.Excel;

namespace Spobras.TesteAmbiente;

internal static class VerificacaoExcel
{
    public const string TituloLocalizar = "Excel encontrado: versão e 32/64 bits";
    public const string TituloAutomacao = "Excel invisível: abrir, escrever, recalcular, ler total, exportar PDF, salvar e fechar";

    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(3);

    public static ResultadoVerificacao Localizar()
    {
        var instalacao = LocalizadorExcel.Localizar();
        if (instalacao is null)
        {
            return SessaoExcel.ExcelInstalado()
                ? ResultadoVerificacao.Passou(3, TituloLocalizar,
                    "Excel registrado para automação, mas o caminho do EXCEL.EXE não foi localizado no registro.",
                    "Versão e 32/64 bits não identificados aqui; veja a versão no item 4.")
                : ResultadoVerificacao.Falhou(3, TituloLocalizar, "Excel não encontrado neste PC.");
        }

        return ResultadoVerificacao.Passou(3, TituloLocalizar,
            $"Caminho: {instalacao.Caminho}",
            $"Versão do arquivo: {instalacao.Versao ?? "não identificada"}",
            $"Arquitetura: {instalacao.Arquitetura ?? "não identificada"}",
            $"Registrado para automação: {(SessaoExcel.ExcelInstalado() ? "sim" : "NÃO")}");
    }

    public static async Task<ResultadoVerificacao> AutomatizarAsync(string pastaTrabalho, string? modeloCronograma)
    {
        var detalhes = new List<string>();
        var pidsAntes = SessaoExcel.PidsExcelAtuais();
        int pidCriado = 0;

        var tarefa = Task.Run(() => Automatizar(pastaTrabalho, modeloCronograma, detalhes, pid => pidCriado = pid));
        if (await Task.WhenAny(tarefa, Task.Delay(Limite)) != tarefa)
        {
            var linhas = new List<string>
            {
                $"O Excel não respondeu em {Limite.TotalMinutes:0} minutos (pode haver uma janela do Excel pedindo algo, como ativação ou login).",
            };
            if (pidCriado != 0 && !pidsAntes.Contains(pidCriado))
            {
                SessaoExcel.EncerrarProcessoCriado(pidCriado, pidsAntes);
                linhas.Add("O Excel invisível criado pelo teste foi encerrado. O seu Excel não foi tocado.");
            }

            lock (detalhes)
            {
                linhas.AddRange(detalhes);
            }

            return ResultadoVerificacao.Falhou(4, TituloAutomacao, [.. linhas]);
        }

        return await tarefa;
    }

    private static ResultadoVerificacao Automatizar(
        string pastaTrabalho, string? modeloCronograma, List<string> detalhes, Action<int> aoCriar)
    {
        void Nota(string texto)
        {
            lock (detalhes)
            {
                detalhes.Add(texto);
            }
        }

        bool falhou = false;
        string etapa = "preparar a pasta de teste";
        string pasta = Path.Combine(pastaTrabalho, "ArquivosSPOBRAS-teste-excel-" + Guid.NewGuid().ToString("N")[..8]);
        var pidsAntes = SessaoExcel.PidsExcelAtuais();
        Nota($"Excel do usuário aberto antes do teste: {pidsAntes.Count} processo(s).");

        SessaoExcel? sessao = null;
        try
        {
            Directory.CreateDirectory(pasta);
            string xlsx = Path.Combine(pasta, "planilha de teste.xlsx");
            string pdf = Path.Combine(pasta, "planilha de teste.pdf");

            etapa = "criar a planilha de teste (sem o Excel)";
            PlanilhaTeste.Criar(xlsx);

            etapa = "iniciar o Excel invisível";
            var relogio = Stopwatch.StartNew();
            sessao = SessaoExcel.Criar();
            aoCriar(sessao.Pid);
            Nota($"Excel invisível iniciado em {relogio.ElapsedMilliseconds} ms (versão {sessao.Versao}).");

            etapa = "abrir a planilha de teste";
            sessao.Abrir(xlsx);

            etapa = "escrever na célula de entrada";
            const decimal entrada = 700m;
            sessao.EscreverNumero(PlanilhaTeste.NomeAba, PlanilhaTeste.CelulaEntrada, (double)entrada);

            etapa = "recalcular";
            sessao.Recalcular();

            etapa = "ler o total";
            double total = sessao.LerNumero(PlanilhaTeste.NomeAba, PlanilhaTeste.CelulaTotal);
            decimal esperado = PlanilhaTeste.TotalEsperado(entrada);
            if (Math.Abs(total - (double)esperado) > 0.000001)
            {
                falhou = true;
                Nota($"Total errado depois de recalcular: esperado {esperado}, lido {total}.");
            }
            else
            {
                Nota($"Escrever, recalcular e ler o total ({total}): ok");
            }

            etapa = "exportar PDF";
            sessao.ExportarPdf(pdf);
            if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0)
            {
                falhou = true;
                Nota("O PDF não foi gerado.");
            }
            else
            {
                Nota($"Exportar PDF: ok ({new FileInfo(pdf).Length / 1024.0:0.0} KB)");
            }

            etapa = "salvar e fechar a planilha";
            sessao.Salvar();
            sessao.FecharPasta();

            etapa = "reler a planilha salva (sem o Excel)";
            decimal? salvo = PlanilhaTeste.LerNumero(xlsx, PlanilhaTeste.CelulaTotal);
            if (salvo != esperado)
            {
                falhou = true;
                Nota($"O arquivo salvo não guardou o total: esperado {esperado}, lido {salvo?.ToString() ?? "vazio"}.");
            }
            else
            {
                Nota("Salvar, fechar e reler o arquivo salvo: ok");
            }

            if (modeloCronograma is not null)
            {
                falhou |= !TestarModelo(sessao, modeloCronograma, pasta, Nota);
            }
        }
        catch (Exception ex)
        {
            falhou = true;
            Nota($"Falha ao {etapa}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (sessao is not null)
            {
                sessao.Dispose();
                if (sessao.ProcessoEncerrado)
                {
                    Nota("Excel invisível encerrado: ok");
                }
                else
                {
                    falhou = true;
                    Nota("O Excel invisível criado pelo teste continuou aberto.");
                }
            }

            var pidsDepois = SessaoExcel.PidsExcelAtuais();
            int sumiram = pidsAntes.Count(p => !pidsDepois.Contains(p));
            if (sumiram > 0)
            {
                falhou = true;
                Nota($"{sumiram} processo(s) do Excel do usuário não estão mais abertos (se você fechou o Excel durante o teste, rode de novo).");
            }
            else
            {
                Nota("Excel do usuário não foi afetado: ok");
            }

            VerificacaoArquivos.LimparPastaDeTeste(pasta);
        }

        string[] linhas;
        lock (detalhes)
        {
            linhas = [.. detalhes];
        }

        return falhou
            ? ResultadoVerificacao.Falhou(4, TituloAutomacao, linhas)
            : ResultadoVerificacao.Passou(4, TituloAutomacao, linhas);
    }

    /// <summary>Abre uma copia do modelo do cronograma, recalcula e exporta PDF. O original so e lido.</summary>
    private static bool TestarModelo(SessaoExcel sessao, string modelo, string pasta, Action<string> nota)
    {
        string etapa = "copiar o modelo";
        try
        {
            string extensao = Path.GetExtension(modelo);
            string copia = Path.Combine(pasta, "copia do modelo" + extensao);
            File.Copy(modelo, copia);
            File.SetAttributes(copia, FileAttributes.Normal);

            etapa = "abrir a cópia do modelo";
            var relogio = Stopwatch.StartNew();
            sessao.Abrir(copia);
            int abas = sessao.QuantidadeDeAbas;

            etapa = "recalcular a cópia do modelo";
            sessao.Recalcular();

            etapa = "exportar a cópia do modelo em PDF";
            string pdf = Path.Combine(pasta, "copia do modelo.pdf");
            sessao.ExportarPdf(pdf);
            long tamanho = File.Exists(pdf) ? new FileInfo(pdf).Length : 0;

            etapa = "fechar a cópia do modelo";
            sessao.FecharPasta();

            if (tamanho == 0)
            {
                nota("Modelo do cronograma: o PDF não foi gerado.");
                return false;
            }

            nota($"Modelo do cronograma ({extensao}, {abas} aba(s)): abrir cópia, recalcular e exportar PDF: ok " +
                 $"({relogio.ElapsedMilliseconds} ms, PDF de {tamanho / 1024.0:0.0} KB). Macros ficaram desligadas; o original não foi alterado.");
            return true;
        }
        catch (Exception ex)
        {
            nota($"Modelo do cronograma: falha ao {etapa}: {ex.GetType().Name}: {ex.Message}");
            try
            {
                sessao.FecharPasta();
            }
            catch (Exception)
            {
                // A sessao e encerrada logo depois.
            }

            return false;
        }
    }
}
