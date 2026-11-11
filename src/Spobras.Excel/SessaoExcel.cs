using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spobras.Excel;

/// <summary>
/// Uma instancia do Excel criada pelo app, invisivel, usada por uma chamada de cada vez.
/// Nunca toca na instancia do usuario e so encerra o processo EXCEL.EXE que ela mesma criou.
/// </summary>
public sealed class SessaoExcel : IDisposable
{
    private const int XlTypePdf = 0;
    private const int MsoAutomationSecurityForceDisable = 3;

    private dynamic? _excel;
    private dynamic? _pasta;
    private bool _encerrada;

    private SessaoExcel(dynamic excel, int pid, IReadOnlySet<int> pidsAnteriores)
    {
        _excel = excel;
        Pid = pid;
        PidsDoUsuario = pidsAnteriores;
    }

    /// <summary>Processo EXCEL.EXE criado por esta sessao.</summary>
    public int Pid { get; }

    /// <summary>Processos EXCEL.EXE que ja existiam antes da sessao (do usuario). Nunca sao tocados.</summary>
    public IReadOnlySet<int> PidsDoUsuario { get; }

    public string Versao => Tentar(() => (string)_excel!.Version);

    /// <summary>True se o processo criado por esta sessao ja terminou (so faz sentido depois do Dispose).</summary>
    public bool ProcessoEncerrado => !ProcessoExiste(Pid);

    public static bool ExcelInstalado() => Type.GetTypeFromProgID("Excel.Application") is not null;

    public static IReadOnlySet<int> PidsExcelAtuais()
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("EXCEL"))
        {
            pids.Add(p.Id);
            p.Dispose();
        }

        return pids;
    }

    public static SessaoExcel Criar()
    {
        var tipo = Type.GetTypeFromProgID("Excel.Application")
                   ?? throw new InvalidOperationException("O Excel não está instalado neste PC (Excel.Application não registrado).");

        var anteriores = PidsExcelAtuais();
        dynamic excel = Activator.CreateInstance(tipo)
                        ?? throw new InvalidOperationException("Não foi possível iniciar o Excel.");

        int pid;
        try
        {
            nint janela = new nint((long)excel.Hwnd);
            _ = GetWindowThreadProcessId(janela, out uint pidJanela);
            pid = (int)pidJanela;
        }
        catch
        {
            Marshal.FinalReleaseComObject(excel);
            throw;
        }

        if (pid == 0 || anteriores.Contains(pid))
        {
            // O Windows entregou uma instancia que ja existia: e do usuario, nao mexer nem fechar.
            Marshal.FinalReleaseComObject(excel);
            throw new InvalidOperationException(
                "O Excel devolveu uma instância que já estava aberta em vez de criar uma nova. Nada foi alterado nela.");
        }

        var sessao = new SessaoExcel(excel, pid, anteriores);
        try
        {
            Tentar(() =>
            {
                excel.Visible = false;
                excel.DisplayAlerts = false;
                excel.ScreenUpdating = false;
                excel.AskToUpdateLinks = false;
                excel.AutomationSecurity = MsoAutomationSecurityForceDisable;
            });
        }
        catch
        {
            sessao.Dispose();
            throw;
        }

        return sessao;
    }

    /// <summary>Abre uma pasta de trabalho nesta sessao (uma por vez).</summary>
    public void Abrir(string caminho, bool somenteLeitura = false)
    {
        if (_pasta is not null)
        {
            throw new InvalidOperationException("Já existe uma pasta de trabalho aberta nesta sessão.");
        }

        _pasta = Tentar(() => _excel!.Workbooks.Open(caminho, UpdateLinks: 0, ReadOnly: somenteLeitura, AddToMru: false));
    }

    public int QuantidadeDeAbas => Tentar(() => (int)_pasta!.Worksheets.Count);

    public void EscreverNumero(string aba, string celula, double valor) =>
        Tentar(() => _pasta!.Worksheets[aba].Range[celula].Value2 = valor);

    public double LerNumero(string aba, string celula) =>
        Tentar(() => Convert.ToDouble(_pasta!.Worksheets[aba].Range[celula].Value2));

    public void Recalcular() => Tentar(() => _excel!.CalculateFull());

    public void ExportarPdf(string caminhoPdf) => Tentar(() => _pasta!.ExportAsFixedFormat(XlTypePdf, caminhoPdf));

    public void Salvar() => Tentar(() => _pasta!.Save());

    public void FecharPasta(bool salvar = false)
    {
        if (_pasta is null)
        {
            return;
        }

        try
        {
            Tentar(() => _pasta.Close(salvar));
        }
        finally
        {
            Marshal.FinalReleaseComObject(_pasta);
            _pasta = null;
        }
    }

    public void Dispose()
    {
        if (_encerrada)
        {
            return;
        }

        _encerrada = true;
        try
        {
            FecharPasta(salvar: false);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Segue para encerrar o processo que criamos.
        }

        try
        {
            _excel?.Quit();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Idem.
        }

        if (_excel is not null)
        {
            Marshal.FinalReleaseComObject(_excel);
            _excel = null;
        }

        // Solta as referencias COM intermediarias criadas pelas chamadas dinamicas.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        EncerrarProcessoProprio();
    }

    private void EncerrarProcessoProprio() => EncerrarProcessoCriado(Pid, PidsDoUsuario, TimeSpan.FromSeconds(10));

    /// <summary>
    /// Espera um EXCEL.EXE criado pelo app sair; se nao sair, encerra so ele.
    /// Recusa qualquer processo que ja existia antes (do usuario) ou que nao seja o Excel.
    /// </summary>
    public static void EncerrarProcessoCriado(int pid, IReadOnlySet<int> pidsDoUsuario, TimeSpan? espera = null)
    {
        if (pid == 0 || pidsDoUsuario.Contains(pid))
        {
            return;
        }

        try
        {
            using var processo = Process.GetProcessById(pid);
            if (!string.Equals(processo.ProcessName, "EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!processo.WaitForExit(espera ?? TimeSpan.Zero))
            {
                processo.Kill();
                processo.WaitForExit(5_000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            // Processo ja terminou.
        }
    }

    /// <summary>
    /// O Excel recusa chamadas enquanto esta ocupado (abrindo, calculando). Repete por ate 30 s.
    /// </summary>
    private static T Tentar<T>(Func<T> chamada)
    {
        const int RpcChamadaRejeitada = unchecked((int)0x80010001);
        const int RpcTenteMaisTarde = unchecked((int)0x8001010A);
        var limite = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return chamada();
            }
            catch (COMException ex) when ((ex.HResult == RpcChamadaRejeitada || ex.HResult == RpcTenteMaisTarde)
                                          && limite.Elapsed < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void Tentar(Action chamada) => Tentar<object?>(() =>
    {
        chamada();
        return null;
    });

    private static bool ProcessoExiste(int pid)
    {
        try
        {
            using var processo = Process.GetProcessById(pid);
            return !processo.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
}
