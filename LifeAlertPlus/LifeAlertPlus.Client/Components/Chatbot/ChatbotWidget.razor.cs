using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using LifeAlertPlus.Client.Services;
using LifeAlertPlus.Shared.DTOs.Requests.Chat;
using System.Net;
using System.Text.RegularExpressions;

namespace LifeAlertPlus.Client.Components.Chatbot
{
    public partial class ChatbotWidget : ComponentBase, IDisposable
    {
        [Inject] private LanguageService Lang { get; set; } = null!;
        [Inject] private ChatbotClientService ChatService { get; set; } = null!;
        [Inject] private NavigationManager Nav { get; set; } = null!;
        [Inject] private IJSRuntime JS { get; set; } = null!;

        private record ChatEntry(string Role, string Text);

        private readonly List<ChatEntry> _messages = new();
        private readonly List<ChatMessageDTO> _apiMessages = new();
        private string _input = "";
        private bool _isOpen;
        private bool _isLoading;
        private bool _welcomeShown;
        private bool _show;

        protected override void OnInitialized()
        {
            Lang.OnLanguageChanged += OnLangChanged;
            Nav.LocationChanged += OnLocationChanged;
            UpdateVisibility(Nav.Uri);
        }

        private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
            UpdateVisibility(e.Location);
            _ = InvokeAsync(StateHasChanged);
        }

        private void UpdateVisibility(string url)
        {
            var lower = url.ToLowerInvariant();
            _show = !lower.Contains("/login")
                 && !lower.Contains("/register")
                 && !lower.Contains("/verify");
        }

        private void OnLangChanged() => _ = InvokeAsync(StateHasChanged);

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

        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !e.ShiftKey)
                await SendMessage();
        }

        private async Task ScrollToBottom()
        {
            await Task.Delay(60);
            try { await JS.InvokeVoidAsync("eval", "var el=document.getElementById('chatbot-msgs');if(el)el.scrollTop=el.scrollHeight;"); }
            catch { }
        }

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
