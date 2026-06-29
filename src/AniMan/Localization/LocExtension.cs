using System;
using System.Windows.Markup;

namespace AniMan.Localization;

/// <summary>
/// XAML markup extension that resolves a localized string by key:
/// <c>Text="{loc:Loc Stats_Title}"</c>. Resolved once at load time, so a language
/// change requires an application restart (see <see cref="LocalizationManager"/>).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationManager.Get(Key);
}
