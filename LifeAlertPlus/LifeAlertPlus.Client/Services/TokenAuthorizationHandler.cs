using Microsoft.JSInterop;
using System.Net.Http.Headers;

namespace LifeAlertPlus.Client.Services
{
    // DelegatingHandler atașat la HttpClient-urile tipizate — injectează automat token-ul JWT
    // (citit din sessionStorage prin JS interop) ca header Authorization Bearer pe fiecare request
    public class TokenAuthorizationHandler : DelegatingHandler
    {
        private readonly IJSRuntime _jsRuntime;

        public TokenAuthorizationHandler(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        // Interceptează fiecare cerere HTTP înainte de a fi trimisă: citește token-ul din sessionStorage
        // și îl atașează ca Bearer token; dacă JS interop nu e disponibil (ex: prerendering pe server),
        // cererea continuă fără token în loc să eșueze
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "authToken");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
            catch
            {
                // JS interop might not be available (prerendering), continue without token
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
