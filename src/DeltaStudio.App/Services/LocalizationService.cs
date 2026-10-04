using Microsoft.UI.Xaml;

namespace DeltaStudio.App.Services;

/// <summary>Supported UI cultures.</summary>
public enum UiLanguage
{
    /// <summary>English (LTR).</summary>
    English,

    /// <summary>Persian (RTL).</summary>
    Persian,
}

/// <summary>
/// Resource-dictionary based localization. Shell/navigation surfaces follow the UI culture
/// (with RTL for Persian); technical surfaces like the ladder editor are pinned LTR by the views
/// themselves, because left-to-right is the engineering reading order for ladder logic.
/// </summary>
public sealed class LocalizationService
{
    private ResourceDictionary? _current;

    /// <summary>Raised after the active catalog changed.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Active language.</summary>
    public UiLanguage Language { get; private set; } = UiLanguage.English;

    /// <summary>True when the shell should render right-to-left.</summary>
    public bool IsRtl => Language == UiLanguage.Persian;

    /// <summary>Switch the merged catalog (call from the UI thread).</summary>
    public void SetLanguage(UiLanguage language)
    {
        Language = language;
        var dicts = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            dicts.Remove(_current);
        }

        _current = new ResourceDictionary
        {
            Source = new Uri(language == UiLanguage.Persian
                ? "ms-appx:///Resources/Strings.fa.xaml"
                : "ms-appx:///Resources/Strings.en.xaml"),
        };
        dicts.Add(_current);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Look up a localized string (falls back to the key).</summary>
    public string this[string key]
    {
        get
        {
            if (_current is not null && _current.TryGetValue(key, out object? v) && v is string s)
            {
                return s;
            }

            return Application.Current.Resources.TryGetValue(key, out object? f) && f is string fs ? fs : key;
        }
    }
}
