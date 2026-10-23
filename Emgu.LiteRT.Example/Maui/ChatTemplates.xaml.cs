//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// True if all the values are true (e.g. the line between a shown thinking and an answer).
    /// </summary>
    public sealed class AllTrueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values != null && values.All(v => v is bool b && b);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// The demo's templates for Emgu.LiteRT.LM.Maui's code blocks and thinking, merged in App.xaml.
    /// </summary>
    public partial class ChatTemplates : ResourceDictionary
    {
        public ChatTemplates()
        {
            InitializeComponent();
        }
    }
}
