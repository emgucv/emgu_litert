//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Emgu.TF.Lite;

namespace Maui.Demo.Lite
{
    public class ModelCheckerPage : ContentPage
    {
        private readonly Label _fileLabel;
        private readonly Label _infoLabel;

        public ModelCheckerPage()
        {
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            Button pick = Theme.PrimaryButton("Choose Model File", Theme.GlyphText);
            pick.Clicked += OnButtonClicked;

            _fileLabel = new Label
            {
                Text = "No model selected",
                FontFamily = Theme.TitleFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                LineBreakMode = LineBreakMode.MiddleTruncation
            };
            _infoLabel = new Label
            {
                Text = "Please select a .tflite file to see the model parameters.",
                FontFamily = Theme.BodyFont,
                FontSize = 14,
                TextColor = Theme.SecondaryText
            };

            var infoStack = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    Theme.SectionTitle("Model Details"),
                    _fileLabel,
                    Theme.Divider(),
                    _infoLabel
                }
            };

            var content = new VerticalStackLayout
            {
                Spacing = 16,
                Padding = new Thickness(16, 12, 16, 24),
                Children =
                {
                    Theme.PageHeader(this, Theme.GlyphSettings, "Model Checker", "Inspect the inputs and outputs of a .tflite model."),
                    pick,
                    Theme.Card(infoStack),
                    Theme.AboutCard("Loads a TensorFlow Lite model file with the interpreter, allocates its tensors and lists the input and output tensors together with the number of tensors and operations in the model.")
                }
            };
            Content = Theme.CenteredScroll(content);
        }

        private static String IntArrayToString(int[] values)
        {
            return String.Format("[{0}]", String.Join(",", values));
        }

        private static String GetModelInfo(String fileName)
        {
            StringBuilder modelResult = new StringBuilder();
            try
            {
                modelResult.Append(String.Format("File Name:{0}{1}", fileName, Environment.NewLine));
                modelResult.Append(Environment.NewLine);

                //string contents = System.Text.Encoding.UTF8.GetString(fileData.DataArray);
                using (FlatBufferModel fbm = new FlatBufferModel(fileName))
                {
                    if (!fbm.CheckModelIdentifier())
                        throw new Exception("Model identifier check failed");

                    using (Interpreter interpreter = new Interpreter(fbm))
                    {

                        Status allocateTensorStatus = interpreter.AllocateTensors();
                        if (allocateTensorStatus == Status.Error)
                            throw new Exception("Failed to allocate tensor");
                        int[] input = interpreter.InputIndices;
                        for (int i = 0; i < input.Length; i++)
                        {
                            Tensor inputTensor = interpreter.GetTensor(input[i]);

                            modelResult.Append(String.Format("Input {0} ({1}): {2}{3}{4}", i, inputTensor.Name,
                                inputTensor.Type, IntArrayToString( inputTensor.Dims ), Environment.NewLine));
                        }

                        modelResult.Append(Environment.NewLine);

                        int[] output = interpreter.OutputIndices;
                        for (int i = 0; i < output.Length; i++)
                        {
                            Tensor outputTensor = interpreter.GetTensor(output[i]);

                            modelResult.Append(String.Format("Output {0} ({1}): {2}{3}{4}", i, outputTensor.Name,
                                outputTensor.Type, IntArrayToString( outputTensor.Dims ), Environment.NewLine));
                        }

                        modelResult.Append(Environment.NewLine);

                        modelResult.Append(String.Format(
                            "Tensor size (number of tensors in the model): {0}{1}",
                            interpreter.TensorSize,
                            Environment.NewLine));
                        modelResult.Append(String.Format(
                            "Node size (number of ops in the model): {0}{1}",
                            interpreter.NodeSize,
                            Environment.NewLine));

                    }
                }

                return modelResult.ToString();
            }
            catch (Exception ex)
            {
                modelResult.Append(String.Format("Exception processing file {0}: {1}{2} ", fileName, ex.ToString(),
                    Environment.NewLine));
                return modelResult.ToString();
            }
        }

        private async void OnButtonClicked(Object sender, EventArgs args)
        {
            FileResult fileResult = await FilePicker.PickAsync(PickOptions.Default);
            if (fileResult == null) //canceled
                return;
            String fileName = fileResult.FullPath;

            _fileLabel.Text = System.IO.Path.GetFileName(fileName);
            _infoLabel.TextColor = Theme.PrimaryText;
            _infoLabel.Text = "Reading model...";
            _infoLabel.Text = await Task.Run(() => GetModelInfo(fileName));
        }
    }
}
