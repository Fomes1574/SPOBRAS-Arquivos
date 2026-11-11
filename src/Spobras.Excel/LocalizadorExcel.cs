using System.Diagnostics;
using Microsoft.Win32;
using Spobras.Core.Ambiente;

namespace Spobras.Excel;

/// <summary>Descobre onde esta o Excel instalado, sem abri-lo.</summary>
public static class LocalizadorExcel
{
    public sealed record Instalacao(string Caminho, string? Versao, string? Arquitetura);

    public static Instalacao? Localizar()
    {
        string? caminho = CaminhoPeloRegistro(RegistryView.Registry64) ?? CaminhoPeloRegistro(RegistryView.Registry32);
        if (caminho is null || !File.Exists(caminho))
        {
            return null;
        }

        string? versao = FileVersionInfo.GetVersionInfo(caminho).FileVersion;
        return new Instalacao(caminho, versao, LeitorPE.LerArquitetura(caminho));
    }

    private static string? CaminhoPeloRegistro(RegistryView visao)
    {
        foreach (var raiz in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using var hive = RegistryKey.OpenBaseKey(raiz, visao);
            using var chave = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\excel.exe");
            if (chave?.GetValue(null) is string caminho && !string.IsNullOrWhiteSpace(caminho))
            {
                return caminho.Trim('"');
            }
        }

        return null;
    }
}
