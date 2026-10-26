//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The colours and fonts of the chat controls (Themes/ChatTheme.xaml), merged into the app's resources by
    /// <see cref="ChatResources.EnsureTheme"/>; an app changes one by defining a resource with the same key in App.xaml.
    /// </summary>
    public partial class ChatTheme : ResourceDictionary
    {
        /// <summary>Load the theme.</summary>
        public ChatTheme()
        {
            InitializeComponent();
        }
    }
}
