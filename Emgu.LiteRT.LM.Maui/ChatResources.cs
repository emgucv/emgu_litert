//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Finds the templates and styles of the chat controls by key: the app's resources first (App.xaml, including
    /// merged dictionaries), so an app replaces one by defining a resource with its key, else the built-in
    /// <see cref="ChatStyles"/>.
    /// </summary>
    internal static class ChatResources
    {
        private static ChatStyles _defaults;

        private static ChatStyles Defaults
        {
            get
            {
                if (_defaults == null)
                    _defaults = new ChatStyles();
                return _defaults;
            }
        }

        public static T Find<T>(string key) where T : class
        {
            ResourceDictionary appResources = Application.Current?.Resources;
            if (appResources != null && appResources.TryGetValue(key, out object value) && value is T found)
                return found;
            return Defaults.TryGetValue(key, out object builtIn) ? builtIn as T : null;
        }

        /// <summary>
        /// Give a control its default template, by key, unless it already has one (set directly or by a style).
        /// Called once the control has a parent, when implicit styles have been applied.
        /// </summary>
        public static void ApplyDefaultTemplate(TemplatedView view, string key)
        {
            if (view.Parent != null && view.ControlTemplate == null)
                view.ControlTemplate = Find<ControlTemplate>(key);
        }
    }
}
