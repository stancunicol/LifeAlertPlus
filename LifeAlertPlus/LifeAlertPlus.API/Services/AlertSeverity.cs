namespace LifeAlertPlus.API.Services
{
    // Enumerație pentru severitatea alertelor medicale generate de sistemul de monitorizare.
    // Valoarea numerică determină prioritatea: cu cât e mai mare, cu atât e mai urgentă alerta.
    public enum AlertSeverity
    {
        Normal   = 0, // Valorile vitale sunt în limite normale — fără alertă
        Alert    = 1, // Valorile vitale sunt în afara limitelor obișnuite — necesită atenție
        Critical = 2  // Valorile vitale sunt în pericol iminent — intervenție de urgență necesară
    }
}
