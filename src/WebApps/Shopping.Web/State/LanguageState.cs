namespace Shopping.Web.State;

public sealed class LanguageState
{
    private Language _current = Language.Ru;

    public event Action? Changed;

    public Language Current => _current;

    public string Code => _current switch
    {
        Language.Ru => "RU",
        Language.En => "EN",
        Language.De => "DE",
        Language.Fr => "FR",
        Language.Es => "ES",
        _ => "EN"
    };

    public string Name => _current switch
    {
        Language.Ru => "Русский",
        Language.En => "English",
        Language.De => "Deutsch",
        Language.Fr => "Français",
        Language.Es => "Español",
        _ => "English"
    };

    public void Set(Language language)
    {
        if (_current == language)
        {
            return;
        }

        _current = language;
        Changed?.Invoke();
    }

    public string NavHome => T("Главная", "Home", "Start", "Accueil", "Inicio");
    public string NavAbout => T("Об игре", "About", "Über das Spiel", "À propos", "Acerca de");
    public string NavDiamonds => T("Алмазы", "Diamonds", "Diamanten", "Diamants", "Diamantes");
    public string NavSubscription => T("Подписка", "Subscription", "Abo", "Abonnement", "Suscripción");
    public string NavHelp => T("Помощь", "Help", "Hilfe", "Aide", "Ayuda");

    public string BuyDiamonds => T("Купить алмазы", "Buy diamonds", "Diamanten kaufen", "Acheter des diamants", "Comprar diamantes");
    public string Premium => "Premium";

    public string T(string ru, string en, string de, string fr, string es) => _current switch
    {
        Language.Ru => ru,
        Language.En => en,
        Language.De => de,
        Language.Fr => fr,
        Language.Es => es,
        _ => en
    };

    public string T(string ru, string en) => T(ru, en, en, en, en);

    public enum Language
    {
        Ru,
        En,
        De,
        Fr,
        Es
    }
}
