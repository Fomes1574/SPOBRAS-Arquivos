using Android.Webkit;
using AWebView = Android.Webkit.WebView;

namespace Spobras.Celular.Android;

/// <summary>
/// A camera do app roda dentro da WebView (getUserMedia). O Android so libera se a WebView aceitar o pedido;
/// a permissao do sistema e pedida antes, pela tela da camera (PermitirCameraAsync).
/// </summary>
public static class Camera
{
    public static void Preparar(AWebView web)
    {
        web.Settings.MediaPlaybackRequiresUserGesture = false;
        web.SetWebChromeClient(new ClienteComCamera(web.WebChromeClient));
    }

    private sealed class ClienteComCamera(WebChromeClient? original) : WebChromeClient
    {
        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request is null)
            {
                return;
            }

            var pedidos = request.GetResources() ?? [];
            bool soCamera = pedidos.All(r => r == PermissionRequest.ResourceVideoCapture);
            bool liberada = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(Platform.AppContext, global::Android.Manifest.Permission.Camera)
                            == global::Android.Content.PM.Permission.Granted;
            if (soCamera && liberada)
            {
                request.Grant(pedidos);
            }
            else
            {
                request.Deny();
            }
        }

        public override bool OnShowFileChooser(AWebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams) =>
            original?.OnShowFileChooser(webView, filePathCallback, fileChooserParams) ?? base.OnShowFileChooser(webView, filePathCallback, fileChooserParams);

        public override bool OnCreateWindow(AWebView? view, bool isDialog, bool isUserGesture, global::Android.OS.Message? resultMsg) =>
            original?.OnCreateWindow(view, isDialog, isUserGesture, resultMsg) ?? base.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);

        public override void OnCloseWindow(AWebView? window)
        {
            if (original is not null)
            {
                original.OnCloseWindow(window);
            }
            else
            {
                base.OnCloseWindow(window);
            }
        }
    }
}
