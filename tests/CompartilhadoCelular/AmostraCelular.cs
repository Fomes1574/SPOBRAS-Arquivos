using SkiaSharp;

namespace Spobras.Testes;

/// <summary>
/// Dados inventados para os testes do celular: uma mestra com varios lotes, contratos em andamento e concluidos,
/// medicoes pagas e em tramite, fotos e PDFs de exemplo. Nada vem das planilhas reais.
/// </summary>
public static class AmostraCelular
{
    private static object?[] Obra(string contrato, string escola, string lote, string portaria, string statusContrato, DateTime? os, DateTime? termino,
        decimal original, decimal aditado, decimal pago, string contratada = "Construtora Exemplo Ltda", string escopo = "Reforma da cobertura, pintura e acessibilidade.")
    {
        decimal contratado = original + aditado;
        return
        [
            contratada, contrato, new DateTime(2025, 11, 5), os, termino, escola, original,
            aditado, pago, pago, "ANDAMENTO", "3360", "16.10.12.365.3010.3360.4490.5100.00", lote, "Manutenção", "EMEF", "0000.2026/0000000-0",
            statusContrato, "no prazo", escopo, "CONSERVAÇÃO E MANUTENÇÃO", contratado, contratado - pago, contratado - pago, portaria,
        ];
    }

    private static object?[] Medicao(string contrato, string numero, string escola, string status, decimal considerado, decimal? reajuste = null, string statusContrato = "VIGENTE")
    {
        bool paga = status == "PAGAMENTO EFETUADO";
        return [contrato, numero, escola, new DateTime(2026, 1, 1).AddMonths(int.Parse(numero) - 1), new DateTime(2026, 1, 28).AddMonths(int.Parse(numero) - 1),
            considerado, considerado, considerado, paga ? considerado : 0m, status, "015/EX/2025", paga ? new DateTime(2026, 2, 20) : null, reajuste, statusContrato];
    }

    public const string P3 = "PORTARIA EXEMPLO Nº 03 (2021)";
    public const string P6 = "PORTARIA EXEMPLO Nº 06 (2022)";

    public static PlanilhaFalsa Mestra() => new PlanilhaFalsa()
        .Tabela("contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS", BaseSintetica.ColunasObras,
            Obra("044/EX/2026", "EMEI Exemplo Alfa", "LOTE 216", P3, "VIGENTE", new DateTime(2026, 5, 13), new DateTime(2027, 2, 13), 3_500_000m, 296_523.69m, 1_177_040.10m),
            Obra("007/EX/2022", "EMEI Exemplo Alfa", "LOTE 112", P3, "ENCERRADO / TRD", new DateTime(2022, 2, 24), new DateTime(2022, 12, 24), 480_000m, 0m, 477_292.97m),
            Obra("051/EX/2026", "CEI Exemplo Beta do Jardim das Flores", "LOTE 216", P6, "SUSPENSO", new DateTime(2026, 3, 2), new DateTime(2026, 12, 2), 2_100_000m, 145_904.55m, 1_302_480.12m),
            Obra("052/EX/2026", "EMEF Exemplo Delta", "LOTE 216", P6, "VIGENTE", new DateTime(2026, 4, 1), new DateTime(2027, 4, 1), 2_040_000m, 0m, 0m),
            Obra("060/EX/2026", "EMEF Exemplo Gama", "LOTE 239", P6, "NÃO INICIADO", null, null, 3_500_000m, 296_523.69m, 0m),
            Obra("061/EX/2026", "CEU Exemplo Épsilon", "LOTE 239", P3, "EM TRÂMITE - ADITIVO", new DateTime(2026, 1, 20), new DateTime(2026, 10, 20), 1_250_000m, 312_500m, 900_000m),
            Obra("070/EX/2025", "EMEI Exemplo Zeta", "LOTE 07", P3, "SOB GESTÃO DA SIURB", new DateTime(2025, 8, 1), new DateTime(2026, 8, 1), 800_000m, 0m, 100_000m))
        .Tabela("preCadastro_VW_RELATORIO_MEDICOES", BaseSintetica.ColunasMedicoes,
            Medicao("044/EX/2026", "1", "EMEI Exemplo Alfa", "PAGAMENTO EFETUADO", 600_000m),
            Medicao("044/EX/2026", "2", "EMEI Exemplo Alfa", "PAGAMENTO EFETUADO", 577_040.10m),
            Medicao("044/EX/2026", "5", "EMEI Exemplo Alfa", "LIBERADO PARA FATURAMENTO", 33_000m, 920.40m),
            Medicao("044/EX/2026", "6", "EMEI Exemplo Alfa", "LIBERADO PARA PAGAMENTO", 410_000m, 8_300.12m),
            Medicao("044/EX/2026", "7", "EMEI Exemplo Alfa", "ANÁLISE GEC", 395_010m),
            Medicao("051/EX/2026", "3", "CEI Exemplo Beta do Jardim das Flores", "DEVOLUÇÃO FISCAL", 120_000m),
            Medicao("061/EX/2026", "4", "CEU Exemplo Épsilon", "CADASTRADO PARA FATURAMENTO", 88_000m, 1_200m),
            Medicao("007/EX/2022", "9", "EMEI Exemplo Alfa", "ANÁLISE GEC", 2_707.03m, statusContrato: "ENCERRADO / TRD"),
            Medicao("070/EX/2025", "2", "EMEI Exemplo Zeta", "ANÁLISE GEC", 50_000m, statusContrato: "SOB GESTÃO DA SIURB"))
        .Tabela("contratos_VW_EMPENHOS_SOF", BaseSintetica.ColunasEmpenhos,
            BaseSintetica.Empenho("015/EX/2025", "100001", 5000m));

    public static void GravarMestra(string caminho) => XlsxSintetico.Gravar(Mestra(), caminho);

    /// <summary>Foto de exemplo: ceu, parede e piso, com um numero grande (da para ver a ordem e a orientacao).</summary>
    public static string Foto(string pasta, string nome, int largura, int altura, int numero)
    {
        using var bmp = new SKBitmap(largura, altura);
        using (var c = new SKCanvas(bmp))
        {
            using var ceu = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, altura * 0.5f), [new SKColor(0x8F, 0xB0, 0xC9), new SKColor(0xC9, 0xD3, 0xD9)], SKShaderTileMode.Clamp) };
            c.DrawRect(0, 0, largura, altura * 0.55f, ceu);
            using var piso = new SKPaint { Color = new SKColor(0xA3, 0x92, 0x78) };
            c.DrawRect(0, altura * 0.55f, largura, altura * 0.45f, piso);
            using var parede = new SKPaint { Color = new SKColor(0xE8, 0xE2, 0xD6) };
            c.DrawRect(largura * 0.15f, altura * 0.25f, largura * 0.5f, altura * 0.4f, parede);
            using var fonte = new SKFont(SKTypeface.Default, altura * 0.25f);
            using var tinta = new SKPaint { Color = new SKColor(0xC8, 0x10, 0x2E), IsAntialias = true };
            c.DrawText(numero.ToString(System.Globalization.CultureInfo.InvariantCulture), largura * 0.72f, altura * 0.5f, fonte, tinta);
        }

        Directory.CreateDirectory(pasta);
        string caminho = Path.Combine(pasta, nome);
        using var dados = bmp.Encode(SKEncodedImageFormat.Jpeg, 88);
        File.WriteAllBytes(caminho, dados.ToArray());
        return caminho;
    }

    /// <summary>Logo de exemplo (generico, nao e o logo real).</summary>
    public static string Logo(string pasta)
    {
        using var bmp = new SKBitmap(300, 200);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            using var p = new SKPaint { Color = new SKColor(0x23, 0x1F, 0x20), IsAntialias = true };
            c.DrawRoundRect(20, 40, 260, 120, 18, 18, p);
            using var fonte = new SKFont(SKTypeface.Default, 64);
            using var branco = new SKPaint { Color = SKColors.White, IsAntialias = true };
            c.DrawText("LOGO", 70, 125, fonte, branco);
        }

        Directory.CreateDirectory(pasta);
        string caminho = Path.Combine(pasta, "logo-exemplo.png");
        using var dados = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(caminho, dados.ToArray());
        return caminho;
    }
}
