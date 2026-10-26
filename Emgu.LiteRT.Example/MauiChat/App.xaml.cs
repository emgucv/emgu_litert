//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace MauiChat;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        // The chat page has its own header, so no NavigationPage around it.
        return new Window(new MainPage()) { Title = "LiteRT Chat" };
    }
}
