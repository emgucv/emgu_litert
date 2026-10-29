//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui;

/// <summary>A choice in <see cref="ChatChoices.ChooseAsync"/>'s list: its id, its name and a line about it.</summary>
/// <param name="Id">What the choice returns</param>
/// <param name="Title">Its name</param>
/// <param name="Detail">A line about it, or null</param>
public sealed record ChatChoice(string Id, string Title, string Detail = null);

/// <summary>
/// One choice from a list in the page's sheet - a row each, with its name (the chosen one ticked) and a line about it -
/// rather than the system's action sheet, which on Mac Catalyst stacks a long list as plain buttons with nothing but
/// their names.
/// </summary>
public static class ChatChoices
{
    /// <summary>Show the list in the page's sheet; the id of the row tapped, or null when it was closed.</summary>
    /// <param name="host">The page</param>
    /// <param name="title">The sheet's title</param>
    /// <param name="choices">The rows, in order</param>
    /// <param name="selectedId">The row ticked as chosen now, or null</param>
    /// <param name="cancel">The closing button's text</param>
    public static Task<string> ChooseAsync(this IChatPageHost host, string title, IEnumerable<ChatChoice> choices, string selectedId = null,
        string cancel = "Cancel")
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (ChatChoice choice in choices ?? Enumerable.Empty<ChatChoice>())
        {
            var name = new Label { Text = (choice.Id == selectedId ? "✓ " : "") + choice.Title };
            ChatResources.ApplyStyle(name, "ChatListTitleStyle");
            var content = new VerticalStackLayout { Spacing = 2, Children = { name } };
            if (!string.IsNullOrEmpty(choice.Detail))
            {
                var detail = new Label { Text = choice.Detail };
                ChatResources.ApplyStyle(detail, "ChatListDetailStyle");
                content.Children.Add(detail);
            }
            var row = new Border { Content = content };
            ChatResources.ApplyStyle(row, "ChatListRowStyle");
            string id = choice.Id;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => host.DismissSheet(id);
            row.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(row, choice.Title + (choice.Id == selectedId ? ", chosen" : ""));
            list.Children.Add(row);
        }
        return host.ShowSheetAsync(title, list, cancel);
    }
}
