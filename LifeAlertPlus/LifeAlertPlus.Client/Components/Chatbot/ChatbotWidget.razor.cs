using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;
using System.Net;
using System.Text.RegularExpressions;

namespace LifeAlertPlus.Client.Components.Chatbot
{
    // Code-behind pentru widget-ul de chatbot flotant — gestionează istoricul conversației,
    // trimiterea mesajelor către API și vizibilitatea pe pagini (ascuns pe login/register/verify)
    public partial class ChatbotWidget : ComponentBase, IDisposable
    {
        [Inject] private LanguageService Lang { get; set; } = null!;
        [Inject] private ChatbotClientService ChatService { get; set; } = null!;
        [Inject] private NavigationManager Nav { get; set; } = null!;
        [Inject] private IJSRuntime JS { get; set; } = null!;

        // Reprezentare locală (doar pentru afișare) a unui mesaj din chat
        private record ChatEntry(string Role, string Text);

        private readonly List<ChatEntry> _messages = new();
        // Istoricul mesajelor trimis efectiv către API, pentru a păstra contextul conversației
        private readonly List<ChatMessageDTO> _apiMessages = new();
        private string _input = "";
        private bool _isOpen;
        private bool _isLoading;
        private bool _welcomeShown;
        private bool _show;

        protected override void OnInitialized()
        {
            // Se abonează la schimbarea limbii (re-randare texte) și la navigare (arată/ascunde widget-ul)
            Lang.OnLanguageChanged += OnLangChanged;
            Nav.LocationChanged += OnLocationChanged;
            UpdateVisibility(Nav.Uri);
        }

        private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
            UpdateVisibility(e.Location);
            _ = InvokeAsync(StateHasChanged);
        }

        // Widget-ul nu trebuie afișat pe paginile de autentificare (login/register/verify)
        private void UpdateVisibility(string url)
        {
            var lower = url.ToLowerInvariant();
            _show = !lower.Contains("/login")
                 && !lower.Contains("/register")
                 && !lower.Contains("/verify");
        }

        private void OnLangChanged() => _ = InvokeAsync(StateHasChanged);

        // Deschide/închide fereastra de chat; la prima deschidere afișează mesajul de bun-venit
        private async Task Toggle()
        {
            _isOpen = !_isOpen;
            if (_isOpen && !_welcomeShown)
            {
                _welcomeShown = true;
                _messages.Add(new ChatEntry("assistant", Lang.T("chatbot.welcome")));
            }
            if (_isOpen)
                await ScrollToBottom();
        }

        private async Task Close()
        {
            _isOpen = false;
            await Task.CompletedTask;
        }

        // Trimite mesajul utilizatorului către serviciul de chat și adaugă răspunsul (sau eroarea) în conversație
        private async Task SendMessage()
        {
            var text = _input.Trim();
            if (string.IsNullOrEmpty(text) || _isLoading) return;

            _input = "";
            _messages.Add(new ChatEntry("user", text));
            _apiMessages.Add(new ChatMessageDTO { Role = "user", Content = text });

            _isLoading = true;
            StateHasChanged();
            await ScrollToBottom();

            // Trimite întregul istoric (_apiMessages) ca să se păstreze contextul conversației
            var reply = await ChatService.SendAsync(_apiMessages, Lang.CurrentLanguage);
            _isLoading = false;

            if (reply != null)
            {
                _messages.Add(new ChatEntry("assistant", reply));
                _apiMessages.Add(new ChatMessageDTO { Role = "assistant", Content = reply });
            }
            else
            {
                _messages.Add(new ChatEntry("assistant", Lang.T("chatbot.error")));
            }

            StateHasChanged();
            await ScrollToBottom();
        }

        // Enter trimite mesajul; Shift+Enter permite linie nouă în câmpul de input
        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !e.ShiftKey)
                await SendMessage();
        }

        // Mică întârziere ca să dea timp DOM-ului să se actualizeze înainte de a derula la ultimul mesaj
        private async Task ScrollToBottom()
        {
            await Task.Delay(60);
            try { await JS.InvokeVoidAsync("eval", "var el=document.getElementById('chatbot-msgs');if(el)el.scrollTop=el.scrollHeight;"); }
            catch { }
        }

        // Codifică HTML-ul (anti-XSS) și apoi aplică formatare minimală: linii noi, **bold**, `code`
        private static string FormatMessage(string text)
        {
            var html = WebUtility.HtmlEncode(text);
            html = html.Replace("\n", "<br>");
            html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            html = Regex.Replace(html, @"`(.+?)`", "<code>$1</code>");
            return html;
        }

        public void Dispose()
        {
            Lang.OnLanguageChanged -= OnLangChanged;
            Nav.LocationChanged -= OnLocationChanged;
        }
    }
}
