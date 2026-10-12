//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;
using Emgu.TF.Lite.ImageIO;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Base class of the image models: downloads a .tflite model and its label file, compiles the model with LiteRT's
    /// CompiledModel API for the chosen hardware accelerators, and runs images through it. The counterpart of the
    /// Interpreter-based models in Emgu.TF.Lite.Models.
    /// </summary>
    public abstract class CompiledImageModel : Emgu.LiteRT.Util.UnmanagedObject
    {
        private readonly FileDownloadManager _downloadManager;
        private Environment _environment;
        private Model _model;
        private CompiledModel _compiledModel;
        private TensorBuffer[] _inputBuffers;
        private TensorBuffer[] _outputBuffers;
        private String[] _labels;

        /// <summary>
        /// Create the model. Call Init to download and compile it.
        /// </summary>
        protected CompiledImageModel()
        {
            _downloadManager = new FileDownloadManager();
            _downloadManager.OnDownloadProgressChanged += onDownloadProgressChanged;
        }

        private void onDownloadProgressChanged(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            if (OnDownloadProgressChanged != null)
                OnDownloadProgressChanged(totalBytesToReceive, bytesReceived, progressPercentage);
        }

        /// <summary>
        /// Callback when model download progress is changed.
        /// </summary>
        public event FileDownloadManager.DownloadProgressChangedEventHandler OnDownloadProgressChanged;

        /// <summary>
        /// Download the model and label files if they aren't already, and compile the model.
        /// </summary>
        /// <param name="modelFile">The .tflite model file</param>
        /// <param name="labelFile">The text file with the labels, one per line</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for. The GPU needs LiteRT's GPU
        /// accelerator plugin (the optional Emgu.LiteRT.Metal.runtime.* / Emgu.LiteRT.OpenCL.runtime.maui.android
        /// packages).</param>
        /// <returns>Async task</returns>
        protected async Task InitModel(DownloadableFile modelFile, DownloadableFile labelFile, HwAccelerators accelerators)
        {
            if (Imported)
                return;
            _downloadManager.Clear();
            _downloadManager.AddFile(modelFile);
            _downloadManager.AddFile(labelFile);
            await _downloadManager.Download();
            if (!_downloadManager.AllFilesDownloaded)
                throw new Exception("Failed to download all the model files");
            // Loading and compiling a model can take a moment: not on the caller's (maybe UI) thread.
            await Task.Run(() => Import(accelerators));
        }

        private void Import(HwAccelerators accelerators)
        {
            String labelFileName = _downloadManager.Files[1].LocalFile;
            if (!File.Exists(labelFileName))
                throw new FileNotFoundException(String.Format("File {0} doesn't exist", labelFileName));
            _labels = File.ReadAllLines(labelFileName);

            String modelFileName = _downloadManager.Files[0].LocalFile;
            if (!File.Exists(modelFileName))
                throw new FileNotFoundException(String.Format("File {0} doesn't exist", modelFileName));
            _environment = new Environment();
            _model = new Model(_environment, modelFileName);
            _compiledModel = new CompiledModel(_environment, _model, accelerators);
            _inputBuffers = _compiledModel.CreateInputBuffers();
            _outputBuffers = _compiledModel.CreateOutputBuffers();
        }

        /// <summary>
        /// True once the model has been compiled
        /// </summary>
        public bool Imported
        {
            get { return _compiledModel != null; }
        }

        /// <summary>
        /// The labels
        /// </summary>
        public String[] Labels
        {
            get { return _labels; }
        }

        /// <summary>
        /// The LiteRT model, or null before Init
        /// </summary>
        public Model Model
        {
            get { return _model; }
        }

        /// <summary>
        /// The compiled model, or null before Init
        /// </summary>
        public CompiledModel CompiledModel
        {
            get { return _compiledModel; }
        }

        /// <summary>
        /// The buffers of the model's inputs (its first signature), or null before Init
        /// </summary>
        public TensorBuffer[] InputBuffers
        {
            get { return _inputBuffers; }
        }

        /// <summary>
        /// The buffers of the model's outputs (its first signature), or null before Init
        /// </summary>
        public TensorBuffer[] OutputBuffers
        {
            get { return _outputBuffers; }
        }

        /// <summary>
        /// The type and shape of the model's (first) input, e.g. Float32 [1, 224, 224, 3]
        /// </summary>
        public RankedTensorType InputTensorType
        {
            get { return _model.GetSignature(0).GetInputTensorType(0); }
        }

        /// <summary>
        /// Throw if the model hasn't been compiled yet.
        /// </summary>
        protected void CheckImported()
        {
            if (!Imported)
                throw new InvalidOperationException("The model is not initialized: call Init first");
        }

        /// <summary>
        /// Read an image file into the (first) input buffer, resized to the given size.
        /// </summary>
        /// <typeparam name="T">The input's element type: float, or byte for a quantized model</typeparam>
        /// <param name="imageFile">The image file</param>
        /// <param name="height">The input height</param>
        /// <param name="width">The input width</param>
        /// <param name="mean">Subtracted from each pixel value</param>
        /// <param name="scale">Each pixel value (after subtracting the mean) is multiplied by this</param>
        /// <param name="flipUpsideDown">Whether to flip the image upside down</param>
        /// <param name="swapBR">Whether to swap the blue and red channels</param>
        protected void ReadImageFileToInput<T>(String imageFile, int height, int width, float mean, float scale,
            bool flipUpsideDown = false, bool swapBR = false) where T : struct
        {
            CheckImported();
            IntPtr data = _inputBuffers[0].Lock(TensorBufferLockMode.Write);
            try
            {
                NativeImageIO.ReadImageFileToTensor<T>(imageFile, data, height, width, mean, scale, flipUpsideDown, swapBR);
            }
            finally
            {
                _inputBuffers[0].Unlock();
            }
        }

#if __IOS__
        /// <summary>
        /// Read an image into the (first) input buffer, resized to the given size.
        /// </summary>
        protected void ReadImageToInput<T>(UIKit.UIImage image, int height, int width, float mean, float scale) where T : struct
        {
            CheckImported();
            IntPtr data = _inputBuffers[0].Lock(TensorBufferLockMode.Write);
            try
            {
                NativeImageIO.ReadImageToTensor<T>(image, data, height, width, mean, scale);
            }
            finally
            {
                _inputBuffers[0].Unlock();
            }
        }
#elif __MACOS__
        /// <summary>
        /// Read an image into the (first) input buffer, resized to the given size.
        /// </summary>
        protected void ReadImageToInput<T>(AppKit.NSImage image, int height, int width, float mean, float scale) where T : struct
        {
            CheckImported();
            IntPtr data = _inputBuffers[0].Lock(TensorBufferLockMode.Write);
            try
            {
                NativeImageIO.ReadImageToTensor<T>(image, data, height, width, mean, scale);
            }
            finally
            {
                _inputBuffers[0].Unlock();
            }
        }
#endif

        /// <summary>
        /// Run the model on what is in the input buffers.
        /// </summary>
        protected void Run()
        {
            CheckImported();
            _compiledModel.Run(_inputBuffers, _outputBuffers);
        }

        /// <summary>
        /// Read an output buffer.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="index">The output index</param>
        /// <returns>The output values, flattened</returns>
        protected T[] ReadOutput<T>(int index) where T : struct
        {
            return _outputBuffers[index].Read<T>();
        }

        /// <summary>
        /// Release the buffers, the compiled model, the model and the environment
        /// </summary>
        protected override void DisposeObject()
        {
            DisposeAll(_inputBuffers);
            _inputBuffers = null;
            DisposeAll(_outputBuffers);
            _outputBuffers = null;
            if (_compiledModel != null)
            {
                _compiledModel.Dispose();
                _compiledModel = null;
            }
            if (_model != null)
            {
                _model.Dispose();
                _model = null;
            }
            if (_environment != null)
            {
                _environment.Dispose();
                _environment = null;
            }
        }

        private static void DisposeAll(TensorBuffer[] buffers)
        {
            if (buffers == null)
                return;
            foreach (TensorBuffer buffer in buffers)
                buffer.Dispose();
        }
    }
}
