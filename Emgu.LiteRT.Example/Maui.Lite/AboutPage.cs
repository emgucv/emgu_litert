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

            // LiteRT has no runtime-queryable version API (no native export to P/Invoke, unlike
            // TfLiteInvoke.Version above); its Major.Minor.Patch is baked into the Emgu.LiteRT
            // assembly's own version at build time (CMakeLists.txt sets CPACK_PACKAGE_VERSION_
            // MAJOR/MINOR/PATCH from litert/version.bzl's LITERT_EXPERIMENTAL_VERSION), so read it
            // back via reflection instead. The Revision component is EmguTF's own git commit count,
            // not part of LiteRT's version, so it's left out.
            Version liteRtAssemblyVer = typeof(Emgu.LiteRT.CompiledModel).Assembly.GetName().Version;
            String liteRtVer = (liteRtAssemblyVer == null)
                ? "unknown"
                : String.Format("{0}.{1}.{2}", liteRtAssemblyVer.Major, liteRtAssemblyVer.Minor, liteRtAssemblyVer.Build);

            if (htmlSource == null)
            {
                bool hasXnnPack = Emgu.TF.Lite.TfLiteInvoke.HasXNNPack;

                htmlSource = String.Format(
                    @"<html>
                    <body>
                    <H1> Emgu TF Lite Examples </H1>
                    <H3> Tensorflow Lite version: {0} </H3>
                    <H3> LiteRT version: {2} </H3>
                    <H3> Has XNNPack: {1} </H3>
                    <H3> Tensorflow Lite <a href=https://github.com/tensorflow/tensorflow/blob/master/LICENSE > license</a> </H3>
                    <H3><a href=https://www.emgu.com/wiki/index.php/Emgu_TF >Visit our website</a> <br/><br/><H3>
                    <H3><a href=mailto:support@emgu.com>Email Support</a> <br/><br/><H3>"
                    + @"
                    </body>
                    </html>", tensorflowVer, hasXnnPack, liteRtVer);
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
