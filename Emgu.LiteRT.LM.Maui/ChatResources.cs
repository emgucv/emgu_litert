//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Finds the templates, styles, colours and fonts of the chat controls by key: the app's resources first (App.xaml,
    /// including merged dictionaries), so an app replaces one by defining a resource with its key, else the built-in
    /// <see cref="ChatStyles"/> and <see cref="ChatTheme"/>. The theme (colours and fonts) is merged into the app's
    /// resources the first time it is needed, so the built-in templates' <c>StaticResource</c> references find it there -
    /// and an app's own definition of a key, which comes first, wins.
    /// </summary>
    public static class ChatResources
    {
        private static ChatStyles _defaults;
        private static bool _themeMerged;

        private static ChatStyles Defaults
        {
            get
            {
                EnsureTheme();
                if (_defaults == null)
                    _defaults = new ChatStyles();
                return _defaults;
            }
        }

        /// <summary>
        /// Merge the built-in <see cref="ChatTheme"/> into the app's resources (once, under the app's own dictionaries, so
        /// what the app defines wins). The controls do it themselves; an app calls it only to read the theme's keys before
        /// any control is shown.
        /// </summary>
        public static void EnsureTheme()
        {
            if (_themeMerged)
                return;
            ResourceDictionary appResources = Application.Current?.Resources;
            if (appResources == null)
                return;
            _themeMerged = true;
            if (appResources.MergedDictionaries.OfType<ChatTheme>().Any())
                return;
            ChatTheme theme = new ChatTheme();
            // Checked after the app's merged dictionaries (the last added is looked up first).
            if (appResources.MergedDictionaries is IList<ResourceDictionary> list)
                list.Insert(0, theme);
            else
                appResources.MergedDictionaries.Add(theme);
        }

        /// <summary>A resource by key: the app's, else the built-in; null if neither has it.</summary>
        /// <typeparam name="T">The resource's type</typeparam>
        /// <param name="key">The key, e.g. "ChatSendButtonStyle"</param>
        public static T Find<T>(string key) where T : class
        {
            ResourceDictionary appResources = Application.Current?.Resources;
            if (appResources != null && appResources.TryGetValue(key, out object value) && value is T found)
                return found;
            return Defaults.TryGetValue(key, out object builtIn) ? builtIn as T : null;
        }

        /// <summary>A colour of the theme by key, e.g. "ChatAccent"; transparent if there is none.</summary>
        /// <param name="key">The key</param>
        public static Color FindColor(string key)
        {
            EnsureTheme();
            ResourceDictionary appResources = Application.Current?.Resources;
            if (appResources != null && appResources.TryGetValue(key, out object value) && value is Color color)
                return color;
            return Colors.Transparent;
        }

        /// <summary>A theme colour of a name ("ChatPrimaryText") for the app's current theme: the "{name}Dark" key in dark mode, else "{name}Light".</summary>
        /// <param name="name">The colour's name</param>
        public static Color ThemeColor(string name)
        {
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return FindColor(name + (dark ? "Dark" : "Light"));
        }

        /// <summary>
        /// Give a view's colour property the theme's light and dark colours of a name: the keys "{name}Light" and
        /// "{name}Dark" (e.g. "ChatSurface"), following the app's theme.
        /// </summary>
        /// <param name="view">The view</param>
        /// <param name="property">The colour property, e.g. BackgroundColorProperty</param>
        /// <param name="name">The colour's name</param>
        public static void SetThemeColor(BindableObject view, BindableProperty property, string name)
        {
            view.SetAppThemeColor(property, FindColor(name + "Light"), FindColor(name + "Dark"));
        }

        /// <summary>Give a view the style of a key, unless it has one.</summary>
        /// <param name="view">The view</param>
        /// <param name="key">The style's key</param>
        public static void ApplyStyle(View view, string key)
        {
            if (view.Style == null && Find<Style>(key) is Style style && style.TargetType.IsInstanceOfType(view))
                view.Style = style;
        }

        /// <summary>
        /// Give a control its default template, by key, unless it already has one (set directly or by a style).
        /// Called once the control has a parent, when implicit styles have been applied.
        /// </summary>
        /// <param name="view">The control</param>
        /// <param name="key">The template's key</param>
        public static void ApplyDefaultTemplate(TemplatedView view, string key)
        {
            if (view.Parent != null && view.ControlTemplate == null)
                view.ControlTemplate = Find<ControlTemplate>(key);
        }
    }
}
