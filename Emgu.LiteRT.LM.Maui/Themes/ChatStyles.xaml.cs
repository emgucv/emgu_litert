//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The default templates and styles of the chat controls. The controls find them by key, first in the app's
    /// resources and then here, so an app replaces one by defining a resource with the same key (e.g. its own
    /// "CodeBlockViewTemplate" in App.xaml); it doesn't need to merge this dictionary. The keys and template parts are
    /// listed in ChatStyles.xaml and on each control.
    /// </summary>
    public partial class ChatStyles : ResourceDictionary
    {
        /// <summary>
        /// Create the dictionary of the default styles.
        /// </summary>
        public ChatStyles()
        {
            InitializeComponent();
        }
    }
}
