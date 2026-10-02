#include "tfliteextern.h"

#include <cstdarg>
#include <cstdio>
#include <fstream>
#include <map>
#include <mutex>

#include "tflite/schema/schema_generated.h"

// ---------------------------------------------------------------------------
// Error reporting
// ---------------------------------------------------------------------------

static TfeErrorCallback customErrorCallback = 0;

static void tfeReportError(void* /*userData*/, const char* format, va_list args) {
  char errBuffer[2048];
  const int result = vsnprintf(errBuffer, sizeof(errBuffer), format, args);
  if (customErrorCallback) customErrorCallback(result, errBuffer);
}

static void tfeReportErrorf(const char* format, ...) {
  va_list args;
  va_start(args, format);
  tfeReportError(nullptr, format, args);
  va_end(args);
}

void tfeRedirectError(TfeErrorCallback errCallback) {
  customErrorCallback = errCallback;
}

// ---------------------------------------------------------------------------
// Model
// ---------------------------------------------------------------------------

static const tflite::SubGraph* tfePrimarySubgraph(const TfeModel* model) {
  if (!model || !model->buffer) return nullptr;
  const tflite::Model* m = tflite::GetModel(model->buffer);
  if (!m || !m->subgraphs() || m->subgraphs()->size() == 0) return nullptr;
  return m->subgraphs()->Get(0);
}

static TfeModel* tfeModelCreate(TfeModel* model) {
  model->model = TfLiteModelCreateWithErrorReporter(
      model->buffer, model->bufferSize, tfeReportError, nullptr);
  if (!model->model) {
    delete model;
    return nullptr;
  }
  return model;
}

TfeModel* tfeFlatBufferModelBuildFromFile(char* filename) {
  std::ifstream file(filename, std::ios::binary | std::ios::ate);
  if (!file) {
    tfeReportErrorf("Could not open '%s'.", filename);
    return nullptr;
  }
  std::streamsize size = file.tellg();
  file.seekg(0, std::ios::beg);

  TfeModel* model = new TfeModel();
  model->ownedBuffer.resize(static_cast<size_t>(size));
  if (size > 0 && !file.read(model->ownedBuffer.data(), size)) {
    tfeReportErrorf("Could not read '%s'.", filename);
    delete model;
    return nullptr;
  }
  model->buffer = model->ownedBuffer.data();
  model->bufferSize = model->ownedBuffer.size();
  return tfeModelCreate(model);
}

TfeModel* tfeFlatBufferModelBuildFromBuffer(char* buffer, int bufferSize) {
  // The caller (FlatBufferModel in C#) keeps the buffer pinned for the lifetime of the model.
  TfeModel* model = new TfeModel();
  model->buffer = buffer;
  model->bufferSize = static_cast<size_t>(bufferSize);
  return tfeModelCreate(model);
}

bool tfeFlatBufferModelInitialized(TfeModel* model) {
  return model && model->model;
}

bool tfeFlatBufferModelCheckModelIdentifier(TfeModel* model) {
  if (!model || !model->buffer || model->bufferSize < 8 ||
      !tflite::ModelBufferHasIdentifier(model->buffer)) {
    tfeReportErrorf("Model provided has model identifier '%.4s', should be '%s'",
                    model && model->buffer && model->bufferSize >= 8 ? model->buffer + 4 : "",
                    tflite::ModelIdentifier());
    return false;
  }
  return true;
}

void tfeFlatBufferModelRelease(TfeModel** model) {
  if (*model) {
    TfLiteModelDelete((*model)->model);
    delete *model;
  }
  *model = 0;
}

// ---------------------------------------------------------------------------
// Op resolver
// ---------------------------------------------------------------------------

TfeOpResolver* tfeBuiltinOpResolverCreate(TfeOpResolver** opResolver) {
  TfeOpResolver* resolver = new TfeOpResolver();
  *opResolver = resolver;
  return resolver;
}

void tfeBuiltinOpResolverRelease(TfeOpResolver** resolver) {
  delete *resolver;
  *resolver = 0;
}

// ---------------------------------------------------------------------------
// Interpreter
// ---------------------------------------------------------------------------

// (Re)creates the C API interpreter from the model, thread count and delegates. Returns false on failure.
static bool tfeInterpreterRecreate(TfeInterpreter* interpreter) {
  if (interpreter->interpreter) {
    TfLiteInterpreterDelete(interpreter->interpreter);
    interpreter->interpreter = nullptr;
  }
  if (!interpreter->model || !interpreter->model->model) return false;

  TfLiteInterpreterOptions* options = TfLiteInterpreterOptionsCreate();
  TfLiteInterpreterOptionsSetNumThreads(options, interpreter->numThreads);
  TfLiteInterpreterOptionsSetErrorReporter(options, tfeReportError, nullptr);
  interpreter->interpreter =
      TfLiteInterpreterCreate(interpreter->model->model, options);
  TfLiteInterpreterOptionsDelete(options);
  if (!interpreter->interpreter) return false;

  for (TfLiteDelegate* delegate : interpreter->delegates) {
    if (TfLiteInterpreterModifyGraphWithDelegate(interpreter->interpreter, delegate) != kTfLiteOk)
      return false;
  }
  return true;
}

TfeInterpreter* tfeInterpreterCreate() {
  TfeInterpreter* interpreter = new TfeInterpreter();
  interpreter->model = nullptr;
  interpreter->interpreter = nullptr;
  interpreter->numThreads = -1;
  interpreter->tensorsAllocated = false;
  return interpreter;
}

static int tfeInterpreterBuildFromModel(TfeInterpreter* interpreter, TfeModel* model) {
  interpreter->model = model;
  interpreter->delegates.clear();
  interpreter->tensorsAllocated = false;
  return tfeInterpreterRecreate(interpreter) ? kTfLiteOk : kTfLiteError;
}

void tfeInterpreterCreateFromModel(
    TfeInterpreter** interpreter,
    TfeModel* model,
    TfeOpResolver* /*opResolver*/) {
  if (tfeInterpreterBuildFromModel(*interpreter, model) != kTfLiteOk) {
    // Match InterpreterBuilder: the interpreter is reset to null when it can't be built.
    tfeInterpreterRelease(interpreter);
  }
}

int tfeInterpreterAllocateTensors(TfeInterpreter* interpreter) {
  TfLiteStatus status = TfLiteInterpreterAllocateTensors(interpreter->interpreter);
  if (status == kTfLiteOk) interpreter->tensorsAllocated = true;
  return status;
}

int tfeInterpreterInvoke(TfeInterpreter* interpreter) {
  return TfLiteInterpreterInvoke(interpreter->interpreter);
}

TfLiteTensor* tfeInterpreterGetTensor(TfeInterpreter* interpreter, int index) {
  return TfLiteInterpreterGetTensor(interpreter->interpreter, index);
}

// The C API has no tensor or node count, so these come from the model's primary subgraph. Tensors or nodes
// added later by delegates are not included.
int tfeInterpreterTensorSize(TfeInterpreter* interpreter) {
  const tflite::SubGraph* subgraph = tfePrimarySubgraph(interpreter->model);
  return subgraph && subgraph->tensors() ? subgraph->tensors()->size() : 0;
}

int tfeInterpreterNodesSize(TfeInterpreter* interpreter) {
  const tflite::SubGraph* subgraph = tfePrimarySubgraph(interpreter->model);
  return subgraph && subgraph->operators() ? subgraph->operators()->size() : 0;
}

int tfeInterpreterGetInputSize(TfeInterpreter* interpreter) {
  return TfLiteInterpreterGetInputTensorCount(interpreter->interpreter);
}

void tfeInterpreterGetInput(TfeInterpreter* interpreter, int* input) {
  int count = TfLiteInterpreterGetInputTensorCount(interpreter->interpreter);
  memcpy(input, TfLiteInterpreterInputTensorIndices(interpreter->interpreter),
         count * sizeof(int));
}

const char* tfeInterpreterGetInputName(TfeInterpreter* interpreter, int index) {
  return TfLiteTensorName(
      TfLiteInterpreterGetInputTensor(interpreter->interpreter, index));
}

int tfeInterpreterResizeInputTensor(
    TfeInterpreter* interpreter,
    int input_index,
    int* input_dims,
    int input_dims_size) {
  return TfLiteInterpreterResizeInputTensor(
      interpreter->interpreter, input_index, input_dims, input_dims_size);
}

int tfeInterpreterGetOutputSize(TfeInterpreter* interpreter) {
  return TfLiteInterpreterGetOutputTensorCount(interpreter->interpreter);
}

int tfeInterpreterGetOutput(TfeInterpreter* interpreter, int* output) {
  int count = TfLiteInterpreterGetOutputTensorCount(interpreter->interpreter);
  memcpy(output, TfLiteInterpreterOutputTensorIndices(interpreter->interpreter),
         count * sizeof(int));
  return count;
}

const char* tfeInterpreterGetOutputName(TfeInterpreter* interpreter, int index) {
  return TfLiteTensorName(
      TfLiteInterpreterGetOutputTensor(interpreter->interpreter, index));
}

void tfeInterpreterSetNumThreads(TfeInterpreter* interpreter, int numThreads) {
  if (interpreter->numThreads == numThreads) return;
  // These are warnings on stderr rather than tfeReportError: the C# error handler throws from the callback,
  // which .NET can't unwind through native frames on macOS/Linux, and the C++ API this replaces accepted the
  // call silently.
  if (interpreter->tensorsAllocated) {
    // The C API can't change the thread count of a live interpreter, and re-creating it would invalidate the
    // tensors the caller already holds.
    fprintf(stderr, "tfliteextern: SetNumThreads(%d) ignored, it must be called before AllocateTensors.\n",
            numThreads);
    return;
  }
  interpreter->numThreads = numThreads;
  if (interpreter->model && !tfeInterpreterRecreate(interpreter))
    fprintf(stderr, "tfliteextern: failed to re-create the interpreter with %d threads.\n", numThreads);
}

void tfeInterpreterRelease(TfeInterpreter** interpreter) {
  if (*interpreter) {
    TfLiteInterpreterDelete((*interpreter)->interpreter);
    delete *interpreter;
  }
  *interpreter = 0;
}

int tfeInterpreterModifyGraphWithDelegate(
    TfeInterpreter* interpreter,
    TfLiteDelegate* delegate) {
  TfLiteStatus status =
      TfLiteInterpreterModifyGraphWithDelegate(interpreter->interpreter, delegate);
  // Remembered so a later re-create (SetNumThreads) can re-apply it.
  if (status == kTfLiteOk) interpreter->delegates.push_back(delegate);
  return status;
}

// ---------------------------------------------------------------------------
// Interpreter builder
// ---------------------------------------------------------------------------

TfeInterpreterBuilder* tfeInterpreterBuilderCreate(
    TfeModel* model,
    TfeOpResolver* /*opResolver*/) {
  TfeInterpreterBuilder* builder = new TfeInterpreterBuilder();
  builder->model = model;
  return builder;
}

void tfeInterpreterBuilderRelease(TfeInterpreterBuilder** builder) {
  delete *builder;
  *builder = 0;
}

int tfeInterpreterBuilderBuild(
    TfeInterpreterBuilder* builder,
    TfeInterpreter* interpreter) {
  return tfeInterpreterBuildFromModel(interpreter, builder->model);
}

// ---------------------------------------------------------------------------
// Tensor (TfLiteTensor is the public struct from common.h)
// ---------------------------------------------------------------------------

int tfeTensorGetType(TfLiteTensor* tensor) { return tensor->type; }

char* tfeTensorGetData(TfLiteTensor* tensor) { return tensor->data.raw; }

void tfeTensorGetQuantizationParams(
    TfLiteTensor* tensor,
    TfLiteQuantizationParams* params) {
  memcpy(params, &(tensor->params), sizeof(TfLiteQuantizationParams));
}

int tfeTensorGetAllocationType(TfLiteTensor* tensor) {
  return tensor->allocation_type;
}
int tfeTensorGetByteSize(TfLiteTensor* tensor) { return (int)tensor->bytes; }
const char* tfeTensorGetName(TfLiteTensor* tensor) { return tensor->name; }
TfLiteIntArray* tfeTensorGetDims(TfLiteTensor* tensor) { return tensor->dims; }
bool tfeTensorIsVariable(TfLiteTensor* tensor) { return tensor->is_variable; }

void tfeMemcpy(void* dst, void* src, int length) { memcpy(dst, src, length); }

// ---------------------------------------------------------------------------
// String tensor buffer
// ---------------------------------------------------------------------------

TfeDynamicBuffer* tfeDynamicBufferCreate() {
  TfeDynamicBuffer* buffer = new TfeDynamicBuffer();
  buffer->offset.push_back(0);
  return buffer;
}

void tfeDynamicBufferRelease(TfeDynamicBuffer** buffer) {
  delete *buffer;
  *buffer = 0;
}

void tfeDynamicBufferAddString(TfeDynamicBuffer* buffer, char* str, int len) {
  if (len > 0) buffer->data.insert(buffer->data.end(), str, str + len);
  buffer->offset.push_back(buffer->offset.back() + len);
}

void tfeDynamicBufferWriteToTensor(
    TfeDynamicBuffer* buffer,
    TfLiteTensor* tensor,
    TfLiteIntArray* newShape) {
  int32_t numStrings = static_cast<int32_t>(buffer->offset.size()) - 1;
  int32_t start = sizeof(int32_t) * (numStrings + 2);
  int32_t bytes = start + static_cast<int32_t>(buffer->data.size());

  // TfLiteTensorReset takes ownership of a malloc'd buffer (the tensor frees it with free()).
  char* tensorBuffer = static_cast<char*>(malloc(bytes));
  if (!tensorBuffer) return;
  memcpy(tensorBuffer, &numStrings, sizeof(int32_t));
  for (size_t i = 0; i < buffer->offset.size(); i++) {
    int32_t offset = start + buffer->offset[i];
    memcpy(tensorBuffer + sizeof(int32_t) * (i + 1), &offset, sizeof(int32_t));
  }
  if (!buffer->data.empty())
    memcpy(tensorBuffer + start, buffer->data.data(), buffer->data.size());

  if (newShape == nullptr) newShape = TfLiteIntArrayCopy(tensor->dims);
  TfLiteTensorReset(tensor->type, tensor->name, newShape, tensor->params,
                    tensorBuffer, bytes, kTfLiteDynamic, tensor->allocation,
                    tensor->is_variable, tensor);
}

// ---------------------------------------------------------------------------
// Int array
// ---------------------------------------------------------------------------

TfLiteIntArray* tfeIntArrayCreate(int size) {
  return TfLiteIntArrayCreate(size);
}
int tfeIntArrayGetSize(TfLiteIntArray* v) { return v->size; }
int* tfeIntArrayGetData(TfLiteIntArray* v) { return v->data; }
void tfeIntArrayRelease(TfLiteIntArray** v) {
  TfLiteIntArrayFree(*v);
  *v = 0;
}

// ---------------------------------------------------------------------------
// Delegates
// ---------------------------------------------------------------------------

// tfeTfLiteDelegateRelease is shared by every delegate type on the C# side, so remember how each delegate
// created here has to be deleted.
typedef void (*TfeDelegateDeleter)(TfLiteDelegate*);
static std::mutex delegateDeletersMutex;
static std::map<TfLiteDelegate*, TfeDelegateDeleter>& tfeDelegateDeleters() {
  static std::map<TfLiteDelegate*, TfeDelegateDeleter> deleters;
  return deleters;
}

static TfLiteDelegate* tfeTrackDelegate(TfLiteDelegate* delegate, TfeDelegateDeleter deleter) {
  if (delegate) {
    std::lock_guard<std::mutex> lock(delegateDeletersMutex);
    tfeDelegateDeleters()[delegate] = deleter;
  }
  return delegate;
}

void tfeTfLiteDelegateRelease(TfLiteDelegate** delegate) {
  if (*delegate) {
    TfeDelegateDeleter deleter = nullptr;
    {
      std::lock_guard<std::mutex> lock(delegateDeletersMutex);
      auto it = tfeDelegateDeleters().find(*delegate);
      if (it != tfeDelegateDeleters().end()) {
        deleter = it->second;
        tfeDelegateDeleters().erase(it);
      }
    }
    if (deleter) deleter(*delegate);
  }
  *delegate = 0;
}

#ifdef __ANDROID__
struct TfeStatefulNnApiDelegate : public tflite::StatefulNnApiDelegate {};
#endif

TfeStatefulNnApiDelegate* tfeStatefulNnApiDelegateCreate(
    TfLiteDelegate** tfLiteDelegate) {
#ifdef __ANDROID__
  TfeStatefulNnApiDelegate* d = new TfeStatefulNnApiDelegate();
  *tfLiteDelegate = static_cast<TfLiteDelegate*>(d);
  return d;
#else
  *tfLiteDelegate = 0;
  return 0;
#endif
}

void tfeStatefulNnApiDelegateRelease(TfeStatefulNnApiDelegate** delegate) {
#ifdef __ANDROID__
  delete *delegate;
#endif
  *delegate = 0;
}

#ifdef __ANDROID__
static void tfeGpuDelegateV2Deleter(TfLiteDelegate* delegate) {
  TfLiteGpuDelegateV2Delete(delegate);
}
#endif

TfLiteDelegate* tfeGpuDelegateV2Create() {
#ifdef __ANDROID__
  TfLiteGpuDelegateOptionsV2 options = TfLiteGpuDelegateOptionsV2Default();
  return tfeTrackDelegate(TfLiteGpuDelegateV2Create(&options), tfeGpuDelegateV2Deleter);
#else
  return 0;
#endif
}

void tfeGpuDelegateV2Delete(TfLiteDelegate** delegate) {
  tfeTfLiteDelegateRelease(delegate);
}

#ifndef WITHOUT_XNNPACK
static void tfeXNNPackDelegateDeleter(TfLiteDelegate* delegate) {
  TfLiteXNNPackDelegateDelete(delegate);
}
#endif

TfLiteDelegate* tfeXNNPackDelegateCreateDefault() {
#ifndef WITHOUT_XNNPACK
  TfLiteXNNPackDelegateOptions opt = TfLiteXNNPackDelegateOptionsDefault();
  return tfeTrackDelegate(TfLiteXNNPackDelegateCreate(&opt), tfeXNNPackDelegateDeleter);
#else
  return 0;
#endif
}

TfLiteDelegate* tfeXNNPackDelegateCreate(int numThreads) {
#ifndef WITHOUT_XNNPACK
  TfLiteXNNPackDelegateOptions opt = TfLiteXNNPackDelegateOptionsDefault();
  opt.num_threads = numThreads;
  return tfeTrackDelegate(TfLiteXNNPackDelegateCreate(&opt), tfeXNNPackDelegateDeleter);
#else
  return 0;
#endif
}

// ---------------------------------------------------------------------------
// Version
// ---------------------------------------------------------------------------

const char* tfeGetLiteVersion() { return TfLiteVersion(); }
