using System.Windows;
using System.Windows.Input;

namespace AniMan.Controls;

/// <summary>
/// Makes an element's <c>MouseLeftButtonUp</c> count as a click only when the button
/// was also pressed on that element.
/// <para>
/// Cards treat a left-button release as a click. Double-clicking the title bar
/// maximizes the window between the press and the release, so the release lands on
/// whichever card is now under the cursor and opened it. With this guard enabled,
/// a release without a matching press is marked handled before the element's
/// <c>MouseLeftButtonUp</c> handlers run.
/// </para>
/// </summary>
public static class ClickGuard
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ClickGuard),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsPressedProperty =
        DependencyProperty.RegisterAttached(
            "IsPressed", typeof(bool), typeof(ClickGuard),
            new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.PreviewMouseLeftButtonDown -= OnPreviewDown;
        element.PreviewMouseLeftButtonUp -= OnPreviewUp;
        element.MouseLeave -= OnMouseLeave;

        if (e.NewValue is true)
        {
            element.PreviewMouseLeftButtonDown += OnPreviewDown;
            element.PreviewMouseLeftButtonUp += OnPreviewUp;
            element.MouseLeave += OnMouseLeave;
        }
    }

    private static void OnPreviewDown(object sender, MouseButtonEventArgs e) =>
        ((DependencyObject)sender).SetValue(IsPressedProperty, true);

    private static void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        var element = (DependencyObject)sender;
        if (!(bool)element.GetValue(IsPressedProperty))
            e.Handled = true;

        element.ClearValue(IsPressedProperty);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e) =>
        ((DependencyObject)sender).ClearValue(IsPressedProperty);
}
