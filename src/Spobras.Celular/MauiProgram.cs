using Spobras.Celular.Servicos;

namespace Spobras.Celular.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        builder.Services.AddSingleton<IPlataforma, PlataformaAndroid>();
        builder.Services.AddSingleton(sp => new AppCelular(sp.GetRequiredService<IPlataforma>()));
        return builder.Build();
    }
}
