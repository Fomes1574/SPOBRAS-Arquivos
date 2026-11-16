using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Spobras.Core;
using Spobras.Core.Base;
using Spobras.Core.Nomes;
using Spobras.Core.Relatorio;
using Spobras.Documentos.Imagens;
using Spobras.Documentos.Pdf;

namespace Spobras.Documentos.Relatorio;

public static class GeradorResumoContratoPdf
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly XColor Vermelho = Rgb(0xC8102E);
    private static readonly XColor Escuro = Rgb(0x211D1E);
    private static readonly XColor Claro = Rgb(0xF2F2EF);
    private static XColor Rgb(int rgb) => XColor.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    private const double Margem = 30;
    private const double Largura = 535;
    public static string Reais(decimal valor) => valor.ToString("C2", Brasil);
    public static string Data(DateOnly? data) => data?.ToString("dd/MM/yyyy", Brasil) ?? "—";
    public static string Percentual(decimal valor, int casas = 1) => valor.ToString(casas == 0 ? "0" : "0.0", Brasil) + "%";
    public static string SemPizza => "Sem pagamento nem medição ainda: o gráfico aparece quando houver.";

    public static string Nome(IReadOnlyList<ResumoContrato> contratos, DateTimeOffset agora) =>
        contratos.Count == 1
            ? NomeArquivo.Corrigir($"Resumo do contrato {Limitar(contratos[0].Identificacao.Contrato.Replace('/', '-'))}" +
                (string.IsNullOrWhiteSpace(contratos[0].Identificacao.Lote) ? "" : $" - {Limitar(OpcaoContratoResumo.NomeLote(contratos[0].Identificacao.Lote))}") +
                $" - {HoraDeBrasilia.De(agora):yyyy-MM-dd}.pdf")
            : $"Resumo dos contratos - {HoraDeBrasilia.De(agora):yyyy-MM-dd}.pdf";

    public static string Nome(EscolaResumo escola, DateTimeOffset agora) =>
        NomeArquivo.Corrigir($"Resumo da obra - {Limitar(escola.Obra.Empreendimento)} - {HoraDeBrasilia.De(agora):yyyy-MM-dd}.pdf");

    private static string Limitar(string texto) => texto.Trim()[..Math.Min(texto.Trim().Length, CabecalhoRelatorio.MaximoDaUnidadeNoNome)].TrimEnd(' ', '.', '-');

    public static void Gerar(string destino, IReadOnlyList<ResumoContrato> contratos, DateTimeOffset agora,
        DateTimeOffset? leitura, string? logo = null) =>
        Escrever(destino, agora, leitura, logo, doc =>
        {
            if (contratos.Count == 0) throw new InvalidOperationException("Selecione um contrato para exportar.");
            foreach (var contrato in contratos) doc.Contrato(contrato);
        });

    public static void Gerar(string destino, EscolaResumo escola, DateTimeOffset agora,
        DateTimeOffset? leitura, string? logo = null) =>
        Escrever(destino, agora, leitura, logo, doc => doc.Escola(escola));

    private static void Escrever(string destino, DateTimeOffset agora, DateTimeOffset? leitura, string? logo, Action<Caderno> desenhar)
    {
        FontesPdf.Garantir();
        using var doc = new PdfDocument();
        using var caderno = new Caderno(doc, agora, logo);
        desenhar(caderno);
        caderno.Rodapes(leitura);
        using var saida = new MemoryStream();
        doc.Save(saida, false);
        using var arquivo = new FileStream(destino, FileMode.CreateNew);
        try
        {
            saida.WriteTo(arquivo);
        }
        catch
        {
            arquivo.Dispose();
            File.Delete(destino);
            throw;
        }
    }

    private sealed class Caderno(PdfDocument doc, DateTimeOffset agora, string? logo) : IDisposable
    {
        private XGraphics? _grafico;
        private double _y;
        private DateOnly Hoje => DateOnly.FromDateTime(HoraDeBrasilia.De(agora).DateTime);
        private XGraphics G => _grafico!;
        private static XFont Fonte(double tamanho, bool forte = false) => new(FontesPdf.Familia, tamanho, forte ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        private static XSolidBrush Pincel(XColor cor) => new(cor);

        public void Dispose() => _grafico?.Dispose();

        private void Pagina(string titulo)
        {
            _grafico?.Dispose();
            var pagina = doc.AddPage();
            pagina.Width = XUnit.FromPoint(595.28);
            pagina.Height = XUnit.FromPoint(841.89);
            _grafico = XGraphics.FromPdfPage(pagina);
            if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo))
            {
                using var imagem = XImage.FromStream(new MemoryStream(Fotos.Preparar(logo, 480, 90)));
                double escala = Math.Min(105d / imagem.PixelWidth, 36d / imagem.PixelHeight);
                G.DrawImage(imagem, Margem, 24, imagem.PixelWidth * escala, imagem.PixelHeight * escala);
            }
            Texto("BASE DE CONTRATOS · SPOBRAS", 235, 25, 330, 9, false, direita: true);
            Texto(titulo, 235, 39, 330, 19, true, direita: true);
            G.DrawLine(new XPen(Vermelho, 1.5), Margem, 66, 565, 66);
            _y = 76;
        }

        private void Texto(string? texto, double x, double y, double largura, double tamanho, bool forte = false,
            XColor? cor = null, bool direita = false)
        {
            G.DrawString(texto ?? "—", Fonte(tamanho, forte), Pincel(cor ?? Escuro), new XRect(x, y, largura, tamanho + 3),
                direita ? XStringFormats.TopRight : XStringFormats.TopLeft);
        }

        private List<string> Quebrar(string? texto, double largura, double tamanho, bool forte)
        {
            var fonte = Fonte(tamanho, forte);
            var linhas = new List<string>();
            string linha = "";
            foreach (string palavra in (texto ?? "—").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidata = linha.Length == 0 ? palavra : linha + " " + palavra;
                if (G.MeasureString(candidata, fonte).Width <= largura) { linha = candidata; continue; }
                if (linha.Length > 0) linhas.Add(linha);
                linha = "";
                foreach (char c in palavra)
                {
                    if (linha.Length > 0 && G.MeasureString(linha + c, fonte).Width > largura)
                    {
                        linhas.Add(linha);
                        linha = "";
                    }
                    linha += c;
                }
            }
            if (linha.Length > 0) linhas.Add(linha);
            return linhas;
        }

        private void Fluxo(string? texto, double tamanho, bool forte = false)
        {
            foreach (string linha in Quebrar(texto, Largura, tamanho, forte))
            {
                Texto(linha, Margem, _y, Largura, tamanho, forte);
                _y += tamanho + 3;
            }
        }

        private void Secao(string titulo)
        {
            _y += 8;
            Texto(titulo, Margem, _y, Largura, 11, true, Vermelho);
            _y += 17;
        }

        private void Contratual(ObraLida obra)
        {
            Secao("Situação contratual");
            double topo = _y;
            _y += 7;
            Texto(obra.Contrato.Trim(), 38, _y, 260, 14, true);
            double x = 557;
            foreach (var selo in new[] { PrazoContrato.Prazo(obra, Hoje), PrazoContrato.Status(obra.StatusContrato) })
            {
                double largura = G.MeasureString(selo.Texto, Fonte(9, true)).Width + 14;
                x -= largura;
                G.DrawRectangle(Pincel(Rgb(Convert.ToInt32(selo.Fundo[1..], 16))), x, _y, largura, 15);
                Texto(selo.Texto, x + 7, _y + 2, largura, 9, true, Rgb(Convert.ToInt32(selo.Cor[1..], 16)));
                x -= 5;
            }
            _y += 20;
            Texto($"{OpcaoContratoResumo.NomeLote(obra.Lote)} · {obra.Modalidade}", 38, _y, 500, 10);
            _y += 17;
            Grade([("Portaria", obra.Portaria), ("Contratada", obra.Contratada), ("Processo SEI", obra.ProcessoSei),
                ("Data da OS", Data(obra.DataOs)), ("Término", Data(obra.Termino)), ("Prazo (planilha)", obra.Situacao)]);
            G.DrawRectangle(new XPen(Rgb(0xCBCBC8), .6), Margem, topo, Largura, _y - topo + 2);
            _y += 4;
        }

        private void Grade((string Rotulo, string? Valor)[] dados)
        {
            foreach (var linha in dados.Chunk(3))
            {
                int altura = 1;
                for (int i = 0; i < linha.Length; i++)
                {
                    double x = 38 + 174 * i;
                    Texto(linha[i].Rotulo, x, _y, 168, 9);
                    var partes = Quebrar(linha[i].Valor, 164, 10, true);
                    for (int j = 0; j < partes.Count; j++) Texto(partes[j], x, _y + 12 + 12 * j, 166, 10, true);
                    altura = Math.Max(altura, partes.Count);
                }
                _y += 16 + 12 * altura;
            }
        }

        private void Financeira(ValoresResumo v)
        {
            Secao("Situação financeira");
            var itens = new[] { ("Contratado c/ aditamento", v.Contratado), ("Pago", v.Pago), ($"Em trâmite ({v.Medicoes} medições)", v.Tramite), ("Saldo a pagar", v.SaldoAPagar) };
            for (int i = 0; i < itens.Length; i++)
            {
                double x = Margem + i * 135;
                G.DrawRectangle(Pincel(i == 3 ? Escuro : Claro), x, _y, 130, 38);
                var cor = i == 3 ? XColors.White : Escuro;
                Texto(itens[i].Item1, x + 6, _y + 5, 120, 9, false, cor);
                Texto(Reais(itens[i].Item2), x + 6, _y + 18, 120, 12, true, cor);
            }
            _y += 46;
            if (!v.TemPizza) { Fluxo(SemPizza, 10); return; }
            var fatias = new[] { ("Pago", v.Pago, 0x1E7B34), ("Em trâmite", v.Tramite, 0xE3A23B),
                ("A pagar, sem medição em trâmite", v.SemMedicao, 0xD9D9D5) };
            decimal soma = fatias.Sum(f => Math.Max(0, f.Item2));
            double angulo = -90;
            Texto($"Do valor contratado com aditamento ({Reais(v.Contratado)})", 115, _y, 450, 10, true);
            for (int i = 0; i < fatias.Length; i++)
            {
                var (nome, valor, cor) = fatias[i];
                var corX = Rgb(cor);
                double parte = soma > 0 ? (double)(Math.Max(0, valor) / soma * 360) : 0;
                if (parte >= 359.999) G.DrawEllipse(Pincel(corX), Margem + 5, _y + 4, 65, 65);
                else if (parte > 0) G.DrawPie(Pincel(corX), Margem + 5, _y + 4, 65, 65, angulo, parte);
                angulo += parte;
                double y = _y + 17 + i * 16;
                G.DrawRectangle(Pincel(corX), 115, y + 2, 9, 9);
                Texto(nome, 130, y, 280, 10);
                Texto(Percentual(v.Percentual(valor)), 405, y, 55, 10, true, direita: true);
                Texto(Reais(valor), 462, y, 103, 10, direita: true);
            }
            _y += 73;
        }

        public void Contrato(ResumoContrato contrato)
        {
            var obra = contrato.Identificacao;
            string titulo = $"{obra.Contrato} · {OpcaoContratoResumo.NomeLote(obra.Lote)}";
            Pagina("Resumo do contrato");
            Fluxo(titulo, 19, true);
            Fluxo($"{obra.Modalidade} · {contrato.Escolas.Count} {(contrato.Escolas.Count == 1 ? "escola" : "escolas")} · Situação em {Data(Hoje)}", 10);
            Contratual(obra);
            Financeira(contrato.Valores);
            double[] larguras = [155, 82, 76, 76, 82, 64];
            string[] colunas = ["Escola", "Contratado c/ adit.", "Pago", "Em trâmite", "Saldo a pagar", "% pago"];
            Secao("Escolas do contrato");
            Linha(colunas, larguras, true, 23);
            int nestaPagina = 0;
            foreach (var escola in contrato.Escolas)
            {
                var v = escola.Valores;
                string[] linhas = Quebrar(escola.Obra.Empreendimento, 153, 10, true).ToArray();
                var tipos = Quebrar(escola.Obra.TipoDeObra ?? escola.Obra.Tipo, 153, 9, false);
                double altura = Math.Max(33, linhas.Length * 12 + tipos.Count * 11 + 7);
                if (nestaPagina == 8 || _y + altura + 50 > 801)
                {
                    Pagina("Resumo do contrato");
                    Fluxo(titulo + " (continuação)", 16, true);
                    Secao("Escolas do contrato");
                    Linha(colunas, larguras, true, 23);
                    nestaPagina = 0;
                }
                Linha(["", Reais(v.Contratado), Reais(v.Pago), Reais(v.Tramite), Reais(v.SaldoAPagar), Percentual(v.Percentual(v.Pago), 0)], larguras, false, altura);
                double y = _y - altura + 4;
                foreach (string nome in linhas) { Texto(nome, 34, y, 153, 10, true); y += 12; }
                foreach (string tipo in tipos) { Texto(tipo, 34, y, 153, 9); y += 11; }
                nestaPagina++;
            }
            var total = contrato.Valores;
            Linha([$"Total do contrato ({contrato.Escolas.Count} {(contrato.Escolas.Count == 1 ? "escola" : "escolas")})",
                Reais(total.Contratado), Reais(total.Pago), Reais(total.Tramite), Reais(total.SaldoAPagar), Percentual(total.Percentual(total.Pago), 0)], larguras, true, 24);
            _y += 4;
            Fluxo("Valores em reais. “Em trâmite”: medições apresentadas e ainda não pagas (sem as de gestão da SIURB).", 9);
        }

        private void Linha(string[] campos, double[] larguras, bool forte, double altura)
        {
            if (forte) G.DrawRectangle(Pincel(Claro), Margem, _y, Largura, altura);
            double x = Margem;
            for (int i = 0; i < campos.Length; i++)
            {
                Texto(campos[i], x + 4, _y + 5, larguras[i] - 8, 9, forte, direita: i > 0);
                x += larguras[i];
            }
            _y += altura;
            G.DrawLine(new XPen(Rgb(0xE0E0DC), .5), Margem, _y, 565, _y);
        }

        public void Escola(EscolaResumo escola)
        {
            var obra = escola.Obra;
            var v = escola.Valores;
            Pagina("Resumo da obra");
            Fluxo(obra.Empreendimento, 19, true);
            Fluxo($"{obra.TipoDeObra ?? obra.Tipo} · Situação em {Data(Hoje)}", 10);
            Contratual(obra);
            Financeira(v);
            Secao("Valores");
            Grade([("Valor original", Reais(v.Original)), ("Aditado", Reais(v.Aditado)), ("Contratado com aditamento", Reais(v.Contratado)),
                ("Medido", Reais(v.Medido)), ("Saldo a medir", Reais(v.SaldoAMedir)), ("Pago", Reais(v.Pago))]);
            if (escola.Medicoes.Count == 0) return;
            string[] colunas = ["Medição", "Situação", "Início", "Considerado", "Reajuste", "Total"];
            double[] larguras = [78, 145, 69, 80, 77, 86];
            Secao("Medições em trâmite");
            Linha(colunas, larguras, true, 23);
            foreach (var m in escola.Medicoes)
            {
                if (_y > 760)
                {
                    Pagina("Resumo da obra");
                    Fluxo(obra.Empreendimento + " (continuação)", 15, true);
                    Secao("Medições em trâmite");
                    Linha(colunas, larguras, true, 23);
                }
                Linha([m.Numero, m.Status, Data(m.Inicio), Reais(m.ValorConsiderado), Reais(m.Reajuste), Reais(m.Valor)], larguras, false, 20);
            }
            Linha(["Total em trâmite", "", "", "", "", Reais(v.Tramite)], larguras, true, 23);
        }

        public void Rodapes(DateTimeOffset? leitura)
        {
            _grafico?.Dispose();
            _grafico = null;
            for (int i = 0; i < doc.PageCount; i++)
            {
                using var g = XGraphics.FromPdfPage(doc.Pages[i], XGraphicsPdfPageOptions.Append);
                _grafico = g;
                g.DrawLine(new XPen(Rgb(0xCBCBC8), .5), Margem, 808, 565, 808);
                Texto("Arquivos SPOBRAS · " + (leitura is { } l ? "planilha mestra lida em " + HoraDeBrasilia.Texto(l) : "leitura da planilha mestra não informada"), Margem, 813, 290, 8);
                Texto($"Gerado em {HoraDeBrasilia.Texto(agora)} · Página {i + 1} de {doc.PageCount}", 300, 813, 265, 8, direita: true);
                _grafico = null;
            }
        }
    }
}
