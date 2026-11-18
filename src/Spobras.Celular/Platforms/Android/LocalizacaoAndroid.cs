using Android;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using AndroidX.Core.Content;
using Spobras.Celular.Servicos;
using Spobras.Core.Relatorio;
using ALocation = Android.Locations.Location;

namespace Spobras.Celular.Android;

/// <summary>
/// Localizacao pelo proprio Android (LocationManager), sem biblioteca a mais. Ouve ao mesmo tempo todas as fontes
/// ligadas: GPS (ceu aberto), rede (Wi-Fi e antenas, que funciona dentro de predio) e, no Android 12+, a combinada do
/// sistema. Nenhuma leitura e descartada pela margem de erro: fica a melhor, e a escuta para assim que chega a
/// precisao desejada ou acaba o tempo. Nada fica rodando depois.
/// </summary>
public sealed class LocalizacaoAndroid
{
    private const string Fused = "fused";

    private static LocationManager? Gerente => Platform.AppContext.GetSystemService(global::Android.Content.Context.LocationService) as LocationManager;

    private static bool Tem(string permissao) =>
        ContextCompat.CheckSelfPermission(Platform.AppContext, permissao) == Permission.Granted;

    public SituacaoLocalizacao Situacao()
    {
        try
        {
            bool precisa = Tem(Manifest.Permission.AccessFineLocation), aproximada = Tem(Manifest.Permission.AccessCoarseLocation);
            if (Gerente is not { } lm)
            {
                return new SituacaoLocalizacao(precisa || aproximada, precisa, false, false);
            }

            bool rede = lm.IsProviderEnabled(LocationManager.NetworkProvider);
            bool ligada = OperatingSystem.IsAndroidVersionAtLeast(28) ? lm.IsLocationEnabled : rede || lm.IsProviderEnabled(LocationManager.GpsProvider);
            return new SituacaoLocalizacao(precisa || aproximada, precisa, ligada, rede);
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            return SituacaoLocalizacao.Indisponivel;
        }
    }

    private static string NomeDaFonte(string? provedor) => provedor switch
    {
        LocationManager.GpsProvider => "GPS",
        LocationManager.NetworkProvider => "Wi-Fi e rede",
        Fused => "combinada",
        _ => provedor ?? "—",
    };

    /// <summary>Idade da leitura pelo relogio do proprio Android (nao muda com o ajuste de hora do celular).</summary>
    private static TimeSpan Idade(ALocation l) =>
        TimeSpan.FromMilliseconds(Math.Max(0, (SystemClock.ElapsedRealtimeNanos() - l.ElapsedRealtimeNanos) / 1_000_000.0));

    private static PontoGps Ponto(ALocation l, string fonte) =>
        new(l.Latitude, l.Longitude, l.HasAccuracy ? l.Accuracy : 9999, DateTimeOffset.Now - Idade(l)) { Fonte = fonte };

    /// <summary>A melhor posicao que o celular ja conhece, mais nova que a idade maxima (na hora, sem ligar nada).</summary>
    public PontoGps? Ultima(TimeSpan idadeMaxima)
    {
        if (Gerente is not { } lm || !Situacao().Permitida)
        {
            return null;
        }

        PontoGps? melhor = null;
        foreach (string provedor in lm.GetProviders(false) ?? [])
        {
            try
            {
                if (lm.GetLastKnownLocation(provedor) is not { } l || Idade(l) > idadeMaxima) continue;
                var p = Ponto(l, "última conhecida (" + NomeDaFonte(provedor) + ")");
                if (p.Valido && (melhor is null || p.Precisao < melhor.Precisao)) melhor = p;
            }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
            {
                // fonte sem permissao ou que sumiu: segue com as outras
            }
        }

        return melhor;
    }

    /// <summary>Ouve todas as fontes ligadas ate a precisao desejada ou o limite, e devolve a melhor leitura.</summary>
    public async Task<PontoGps?> LerAsync(TimeSpan limite, double precisaoDesejada, Action<PontoGps>? aoMelhorar, CancellationToken cancelar)
    {
        if (Gerente is not { } lm || !Situacao().Permitida)
        {
            return null;
        }

        var ouvinte = new Ouvinte(precisaoDesejada, aoMelhorar);
        var fontes = new List<string>();
        foreach (string provedor in new[] { Fused, LocationManager.GpsProvider, LocationManager.NetworkProvider })
        {
            try
            {
                if (provedor == Fused && !OperatingSystem.IsAndroidVersionAtLeast(31)) continue;
                if ((lm.AllProviders ?? []).Contains(provedor) && lm.IsProviderEnabled(provedor)) fontes.Add(provedor);
            }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
            {
            }
        }

        if (fontes.Count == 0)
        {
            return null;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (string provedor in fontes)
            {
                try
                {
                    lm.RequestLocationUpdates(provedor, 500, 0f, ouvinte, Looper.MainLooper);
                }
                catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
                {
                    // uma fonte que falha nao impede as outras
                }
            }
        });

        try
        {
            await Task.WhenAny(ouvinte.Pronto, Task.Delay(limite, cancelar));
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    lm.RemoveUpdates(ouvinte); // nada fica rodando
                }
                catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
                {
                }
            });
        }

        return ouvinte.Melhor;
    }

    private sealed class Ouvinte(double precisaoDesejada, Action<PontoGps>? aoMelhorar) : Java.Lang.Object, ILocationListener
    {
        private readonly object _trava = new();
        private readonly TaskCompletionSource _pronto = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PontoGps? Melhor { get; private set; }

        public Task Pronto => _pronto.Task;

        public void OnLocationChanged(ALocation location)
        {
            var p = Ponto(location, NomeDaFonte(location.Provider));
            if (!p.Valido) return;
            lock (_trava)
            {
                if (Melhor is { } m && m.Precisao <= p.Precisao) return; // so troca por uma melhor
                Melhor = p;
            }

            aoMelhorar?.Invoke(p);
            if (p.Precisao <= precisaoDesejada) _pronto.TrySetResult();
        }

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
    }

    /// <summary>
    /// Endereco do ponto pelo Geocoder do Android (gratuito, com internet): so o primeiro, o mais proximo, ja no texto
    /// curto. Falha de passagem volta como <see cref="FalhaEndereco.Falhou"/> para a tela tentar de novo.
    /// </summary>
    public static Task<ResultadoEndereco> EnderecoAsync(double latitude, double longitude) => Task.Run(() =>
    {
        if (!Geocoder.IsPresent)
        {
            return new ResultadoEndereco(null, FalhaEndereco.SemServico);
        }

        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return new ResultadoEndereco(null, FalhaEndereco.SemInternet);
        }

        try
        {
            var geocoder = new Geocoder(Platform.AppContext, Java.Util.Locale.ForLanguageTag("pt-BR")!);
#pragma warning disable CA1422, CS0618 // a versao com retorno direto funciona em todos os Android do app (8 em diante), fora do fio da tela
            var lista = geocoder.GetFromLocation(latitude, longitude, 1);
#pragma warning restore CA1422, CS0618
            if (lista is not { Count: > 0 } || lista[0] is not { } a)
            {
                return new ResultadoEndereco(null, FalhaEndereco.NaoAchou);
            }

            string? texto = EnderecoDasFotos.Formatar(a.Thoroughfare, a.SubThoroughfare, a.SubLocality, a.Locality)
                            ?? (a.MaxAddressLineIndex >= 0 ? EnderecoDasFotos.EncurtarLinha(a.GetAddressLine(0)) : null);
            return texto is null ? new ResultadoEndereco(null, FalhaEndereco.NaoAchou) : new ResultadoEndereco(texto);
        }
        catch (Exception)
        {
            // O Geocoder falha com erros de rede variados (Java.IO.IOException, "grpc failed") mesmo com internet
            return new ResultadoEndereco(null, Connectivity.Current.NetworkAccess == NetworkAccess.Internet ? FalhaEndereco.Falhou : FalhaEndereco.SemInternet);
        }
    });
}
