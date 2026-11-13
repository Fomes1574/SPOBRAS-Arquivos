using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Spobras.Celular.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static int _proximo = 4100;
    private static readonly Dictionary<int, TaskCompletionSource<Intent?>> Pendentes = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Barras do sistema escuras (como o topo do app): icones claros para aparecerem.
        if (Window is { } janela)
        {
            var controle = WindowCompat.GetInsetsController(janela, janela.DecorView);
            controle.AppearanceLightStatusBars = false;
            controle.AppearanceLightNavigationBars = false;
        }
    }

    /// <summary>Abre um seletor do Android (arquivo, pasta) e espera a resposta. Nulo se o usuario cancelar.</summary>
    public static Task<Intent?> EscolherAsync(Intent intent)
    {
        var atividade = Platform.CurrentActivity ?? throw new InvalidOperationException("O app não está na tela.");
        var espera = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int codigo;
        lock (Pendentes)
        {
            codigo = _proximo++;
            Pendentes[codigo] = espera;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                atividade.StartActivityForResult(intent, codigo);
            }
            catch (ActivityNotFoundException)
            {
                lock (Pendentes)
                {
                    Pendentes.Remove(codigo);
                }

                espera.TrySetException(new InvalidOperationException("Este celular não tem um seletor de arquivos para isso."));
            }
        });
        return espera.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        TaskCompletionSource<Intent?>? espera;
        lock (Pendentes)
        {
            if (!Pendentes.Remove(requestCode, out espera))
            {
                return;
            }
        }

        espera.TrySetResult(resultCode == Result.Ok ? data : null);
    }
}
