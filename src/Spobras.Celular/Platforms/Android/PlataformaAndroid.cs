using Android.Content;
using Android.Database;
using Android.Provider;
using Spobras.Celular.Servicos;
using Spobras.Core.Nomes;
using AUri = Android.Net.Uri;

namespace Spobras.Celular.Android;

/// <summary>
/// O que so existe no Android: seletor de arquivos (SAF) com permissao guardada, salvar na pasta escolhida,
/// galeria, compartilhar e abrir. A planilha mestra e lida so por copia; o app nunca escreve nela.
/// </summary>
public sealed class PlataformaAndroid : IPlataforma
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static ContentResolver Resolver => Platform.AppContext.ContentResolver!;

    public string PastaDoApp { get; } = FileSystem.AppDataDirectory;

    public async Task<Escolhido?> EscolherPlanilhaAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { Xlsx, "application/octet-stream" });
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        var r = await MainActivity.EscolherAsync(intent);
        if (r?.Data is not { } uri)
        {
            return null;
        }

        // Permissao de leitura que continua valendo depois de fechar o app (so leitura).
        Resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        return new Escolhido(uri.ToString()!, Nome(uri) ?? "planilha.xlsx");
    }

    public Task<Copia> CopiarAsync(string id, string destino) => Task.Run(() =>
    {
        try
        {
            using var entrada = Resolver.OpenInputStream(AUri.Parse(id)!) ?? throw new FileNotFoundException();
            string temp = destino + ".tmp";
            using (var saida = File.Create(temp))
            {
                entrada.CopyTo(saida);
            }

            File.Move(temp, destino, overwrite: true);
            return Copia.Ok;
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.IO.FileNotFoundException or FileNotFoundException
                                       or Java.IO.IOException or IOException or Java.Lang.IllegalArgumentException)
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet ? Copia.Inacessivel : Copia.SemConexao;
        }
    });

    public async Task<IReadOnlyList<Escolhido>> EscolherArquivosAsync(string tipo, bool varios, string pastaDestino)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(tipo);
        intent.PutExtra(Intent.ExtraAllowMultiple, varios);
        var r = await MainActivity.EscolherAsync(intent);
        if (r is null)
        {
            return [];
        }

        var uris = new List<AUri>();
        if (r.ClipData is { } clip)
        {
            for (int i = 0; i < clip.ItemCount; i++)
            {
                if (clip.GetItemAt(i)?.Uri is { } u)
                {
                    uris.Add(u);
                }
            }
        }
        else if (r.Data is { } u)
        {
            uris.Add(u);
        }

        Directory.CreateDirectory(pastaDestino);
        return await Task.Run(() =>
        {
            var lista = new List<Escolhido>();
            foreach (var uri in uris)
            {
                string nome = Nome(uri) ?? "arquivo";
                string livre = NomeArquivo.Livre(NomeArquivo.Corrigir(nome), n => File.Exists(Path.Combine(pastaDestino, n)));
                string copia = Path.Combine(pastaDestino, livre);
                using (var entrada = Resolver.OpenInputStream(uri)!)
                using (var saida = File.Create(copia))
                {
                    entrada.CopyTo(saida);
                }

                lista.Add(new Escolhido(uri.ToString()!, nome, copia));
            }

            return (IReadOnlyList<Escolhido>)lista;
        });
    }

    public async Task<Escolhido?> EscolherPastaAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
        var r = await MainActivity.EscolherAsync(intent);
        if (r?.Data is not { } arvore)
        {
            return null;
        }

        Resolver.TakePersistableUriPermission(arvore, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        var raiz = DocumentsContract.BuildDocumentUriUsingTree(arvore, DocumentsContract.GetTreeDocumentId(arvore))!;
        return new Escolhido(arvore.ToString()!, Nome(raiz) ?? "Pasta escolhida");
    }

    public Task<Salvo> SalvarAsync(string pastaId, string? subpasta, string arquivo) => Task.Run(() =>
    {
        var arvore = AUri.Parse(pastaId)!;
        var pasta = DocumentsContract.BuildDocumentUriUsingTree(arvore, DocumentsContract.GetTreeDocumentId(arvore))!;
        string onde = Nome(pasta) ?? "Pasta escolhida";
        if (subpasta is not null)
        {
            pasta = Filhos(arvore, pasta).FirstOrDefault(f => f.Pasta && string.Equals(f.Nome, subpasta, StringComparison.OrdinalIgnoreCase)).Uri
                    ?? DocumentsContract.CreateDocument(Resolver, pasta, DocumentsContract.Document.MimeTypeDir, subpasta)
                    ?? throw new IOException($"Não foi possível criar a pasta \"{subpasta}\". Escolha outra pasta nas Configurações.");
            onde += " › " + subpasta;
        }

        var existentes = Filhos(arvore, pasta).Select(f => f.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string nome = NomeArquivo.Livre(Path.GetFileName(arquivo), existentes.Contains);
        var novo = DocumentsContract.CreateDocument(Resolver, pasta, Mime(nome), nome)
                   ?? throw new IOException("Não foi possível salvar na pasta escolhida. Escolha outra pasta nas Configurações.");
        using (var saida = Resolver.OpenOutputStream(novo, "w")!)
        using (var entrada = File.OpenRead(arquivo))
        {
            entrada.CopyTo(saida);
        }

        return new Salvo(novo.ToString()!, Nome(novo) ?? nome, onde, new FileInfo(arquivo).Length);
    });

    public Task GuardarNaGaleriaAsync(string foto) => Task.Run(() =>
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return; // antes do Android 10 precisaria de permissao de armazenamento: a foto fica so no rascunho
        }

        var valores = new ContentValues();
        valores.Put(MediaStore.IMediaColumns.DisplayName, Path.GetFileName(foto));
        valores.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
        valores.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/Arquivos SPOBRAS");
        var uri = Resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, valores);
        if (uri is null)
        {
            return;
        }

        using var saida = Resolver.OpenOutputStream(uri)!;
        using var entrada = File.OpenRead(foto);
        entrada.CopyTo(saida);
    });

    public Task CompartilharAsync(IReadOnlyList<string> arquivos) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = "Compartilhar",
            Files = [.. arquivos.Select(a => new ShareFile(a))],
        }));

    public Task AbrirAsync(Salvo arquivo) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(AUri.Parse(arquivo.Id), Mime(arquivo.Nome));
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        try
        {
            Platform.AppContext.StartActivity(Intent.CreateChooser(intent, "Abrir com")!.AddFlags(ActivityFlags.NewTask));
        }
        catch (ActivityNotFoundException)
        {
        }
    });

    public async Task<bool> PermitirCameraAsync()
    {
        var estado = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<Permissions.Camera>);
        if (estado != PermissionStatus.Granted)
        {
            estado = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Camera>);
        }

        return estado == PermissionStatus.Granted;
    }

    // ---- Interno ----

    private static string Mime(string nome) => Path.GetExtension(nome).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => Xlsx,
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    private static string? Nome(AUri uri)
    {
        try
        {
            using ICursor? c = Resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            return c is not null && c.MoveToFirst() ? c.GetString(0) : null;
        }
        catch (Java.Lang.Exception)
        {
            return null;
        }
    }

    private static IEnumerable<(string Nome, bool Pasta, AUri Uri)> Filhos(AUri arvore, AUri pasta)
    {
        var filhos = DocumentsContract.BuildChildDocumentsUriUsingTree(arvore, DocumentsContract.GetDocumentId(pasta))!;
        var lista = new List<(string, bool, AUri)>();
        using ICursor? c = Resolver.Query(filhos,
            [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType], null, null, null);
        while (c is not null && c.MoveToNext())
        {
            string id = c.GetString(0)!;
            lista.Add((c.GetString(1) ?? "", c.GetString(2) == DocumentsContract.Document.MimeTypeDir, DocumentsContract.BuildDocumentUriUsingTree(arvore, id)!));
        }

        return lista;
    }
}
