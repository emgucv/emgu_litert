//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Maui.Demo.Lite
{
    public class AboutPage : ContentPage
    {
        public AboutPage(String htmlSource = null)
        {
            String tensorflowVer = Emgu.TF.Lite.TfLiteInvoke.Version;

            if (htmlSource == null)
            {
                bool hasXnnPack = Emgu.TF.Lite.TfLiteInvoke.HasXNNPack;

                htmlSource = String.Format(
                    @"<html>
                    <body>
                    <H1> Emgu TF Lite Examples </H1>
                    <H3> Tensorflow Lite version: {0} </H3>
                    <H3> Has XNNPack: {1} </H3>
                    <H3> Tensorflow Lite <a href=https://github.com/tensorflow/tensorflow/blob/master/LICENSE > license</a> </H3>
                    <H3><a href=https://www.emgu.com/wiki/index.php/Emgu_TF >Visit our website</a> <br/><br/><H3>
                    <H3><a href=mailto:support@emgu.com>Email Support</a> <br/><br/><H3>"
                    + @"
                    </body>
                    </html>", tensorflowVer, hasXnnPack);
            }

            // No WidthRequest/HeightRequest: a fixed 1000x1000 (device-independent units) WebView is wider than a
            // phone screen and gets centered, which pushed the left-aligned page content off-screen and left the
            // page looking blank. As the page's only content, the WebView fills the page by default.
            Content =
                    new WebView()
                    {
                        Source =  new HtmlWebViewSource()
                        {
                            Html = htmlSource
                        }
                    };
        }
    }
}
