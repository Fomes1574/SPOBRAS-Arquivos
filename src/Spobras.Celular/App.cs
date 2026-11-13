using Microsoft.AspNetCore.Components.WebView.Maui;
using Spobras.Celular.Componentes;
using Spobras.Celular.Servicos;

namespace Spobras.Celular.Android;

/// <summary>Uma pagina so, com as telas em Blazor. Ao abrir e ao voltar para o app, rele a planilha mestra.</summary>
public sealed class App(AppCelular app) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var web = new BlazorWebView { HostPage = "wwwroot/index.html" };
        web.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Rotas) });
        web.BlazorWebViewInitialized += (_, e) => Camera.Preparar(e.WebView);
        var pagina = new ContentPage { Content = web, BackgroundColor = Color.FromArgb("#231F20") };
        var janela = new Window(pagina) { Title = "Arquivos SPOBRAS" };
        janela.Created += (_, _) => _ = app.AoAbrirAsync();
        janela.Resumed += (_, _) => _ = app.AoAbrirAsync();
        return janela;
    }
}
