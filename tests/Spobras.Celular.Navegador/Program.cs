using Spobras.Celular.Componentes;
using Spobras.Celular.Navegador.Componentes;
using Spobras.Celular.Servicos;
using Spobras.Celular.Navegador;
using Spobras.Testes;

// Host de teste: SPOBRAS_CELULAR_RAIZ (pasta de trabalho), SPOBRAS_CELULAR_CENARIO (novo, pronto, movido, semrede)
// e SPOBRAS_CELULAR_HOJE (data e hora fixas para as imagens sairem sempre iguais).
string raiz = Environment.GetEnvironmentVariable("SPOBRAS_CELULAR_RAIZ") ?? Path.Combine(Path.GetTempPath(), "spobras-celular");
string cenario = Environment.GetEnvironmentVariable("SPOBRAS_CELULAR_CENARIO") ?? "pronto";
Func<DateTimeOffset> agora = DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("SPOBRAS_CELULAR_HOJE"), out var hoje) ? () => hoje : () => DateTimeOffset.Now;

var plataforma = new PlataformaDeTeste(raiz);
await Cenarios.PrepararAsync(plataforma, cenario, agora);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets(); // arquivos das telas (css, js) tambem fora do ambiente de desenvolvimento
builder.Services.AddSingleton<IPlataforma>(plataforma);
builder.Services.AddSingleton(_ => Cenarios.App!);
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 64 * 1024 * 1024); // fotos da camera

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<Pagina>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Rotas).Assembly);
app.Run();
