#ifndef TFAPI_EXPORTS
#define TFAPI_EXPORTS
#endif

#if (defined WIN32 || defined _WIN32 || defined WINCE || \
     defined __CYGWIN__) &&                              \
    defined TFAPI_EXPORTS
#define TF_EXPORTS __declspec(dllexport)
#elif defined __GNUC__ && __GNUC__ >= 4
#define TF_EXPORTS __attribute__((visibility("default")))
#else
#define TF_EXPORTS
#endif

#ifndef TF_EXTERN_C
#ifdef __cplusplus
#define TF_EXTERN_C extern "C"
#else
#define TF_EXTERN_C
#endif
#endif

#if defined WIN32 || defined _WIN32
#define TF_CDECL __cdecl
#define TF_STDCALL __stdcall
#else
#define TF_CDECL
#define TF_STDCALL
#endif

#ifndef TFAPI
#define TFAPI(rettype) TF_EXTERN_C TF_EXPORTS rettype TF_CDECL
#endif

// #include <stddef.h>
// #include <stdint.h>
#include <cstdlib>
#include <cstring>
#include <vector>

// tfliteextern only uses the TensorFlow Lite C API, so it can link dynamically against LiteRT's own runtime
// library (libLiteRt, which exports the TfLite* C API) instead of statically linking a second copy of the
// TFLite runtime. The tfe* entry points and their pointer-based signatures are unchanged for the C# side; the
// opaque handles below are tfliteextern's own wrappers around the C API objects.
#include "tflite/c/c_api.h"
#include "tflite/c/c_api_experimental.h"
#include "tflite/c/common.h"

#ifndef WITHOUT_XNNPACK
#include "tflite/delegates/xnnpack/xnnpack_delegate.h"
#endif

#ifdef __ANDROID__
// The NNAPI delegate is Android-only. Its C API is part of LiteRT's libLiteRt.so on Android, so tfliteextern
// uses that rather than the C++ StatefulNnApiDelegate.
#include "tflite/delegates/nnapi/nnapi_delegate_c_api.h"
// The TFLite GPU delegate is not part of libLiteRt.so (LiteRT does GPU through its own accelerator plugin), and
// it needs TFLite internals libLiteRt.so doesn't export, so the dynamically linked Android build defines
// WITHOUT_GPU_DELEGATE and tfeGpuDelegateV2Create returns null there.
#ifndef WITHOUT_GPU_DELEGATE
#include "tflite/delegates/gpu/delegate.h"
#endif
#endif

// A loaded model. Keeps its own view of the flatbuffer bytes (the C API's TfLiteModel is opaque), used for
// CheckModelIdentifier and the tensor/node counts.
struct TfeModel {
  TfLiteModel* model;
  // Owned copy of the bytes when loaded from a file; empty when the caller owns the buffer.
  std::vector<char> ownedBuffer;
  const char* buffer;
  size_t bufferSize;
};

// The C API always registers the builtin ops, so the op resolver only marks which ops the caller asked for.
struct TfeOpResolver {
  int unused;
};

// An interpreter. The C API fixes the thread count when the interpreter is created, so SetNumThreads before
// AllocateTensors re-creates the interpreter with the new count, re-applying the delegates added so far.
struct TfeInterpreter {
  TfeModel* model;
  TfLiteInterpreter* interpreter;
  int numThreads;
  bool tensorsAllocated;
  std::vector<TfLiteDelegate*> delegates;
};

struct TfeInterpreterBuilder {
  TfeModel* model;
};

// Builds the string tensor format: int32 count, int32 offsets[count + 1] (from the buffer start), then the
// string bytes. Equivalent to tflite::DynamicBuffer, which is C++ and not part of the C API.
struct TfeDynamicBuffer {
  std::vector<char> data;
  std::vector<int> offset;
};

struct TfeStatefulNnApiDelegate;

TFAPI(TfeModel*) tfeFlatBufferModelBuildFromFile(char* filename);
TFAPI(TfeModel*) tfeFlatBufferModelBuildFromBuffer(
    char* buffer,
    int bufferSize);

TFAPI(bool) tfeFlatBufferModelInitialized(TfeModel* model);
TFAPI(bool) tfeFlatBufferModelCheckModelIdentifier(TfeModel* model);
TFAPI(void) tfeFlatBufferModelRelease(TfeModel** model);

TFAPI(TfeOpResolver*) tfeBuiltinOpResolverCreate(TfeOpResolver** opResolver);
TFAPI(void) tfeBuiltinOpResolverRelease(TfeOpResolver** resolver);

TFAPI(TfeInterpreter*) tfeInterpreterCreate();
TFAPI(void) tfeInterpreterCreateFromModel(
    TfeInterpreter** interpreter,
    TfeModel* model,
    TfeOpResolver* opResolver);
TFAPI(int) tfeInterpreterAllocateTensors(TfeInterpreter* interpreter);
TFAPI(int) tfeInterpreterInvoke(TfeInterpreter* interpreter);
TFAPI(TfLiteTensor*) tfeInterpreterGetTensor(TfeInterpreter* interpreter, int index);
TFAPI(int) tfeInterpreterTensorSize(TfeInterpreter* interpreter);
TFAPI(int) tfeInterpreterNodesSize(TfeInterpreter* interpreter);
TFAPI(int) tfeInterpreterGetInputSize(TfeInterpreter* interpreter);
TFAPI(void) tfeInterpreterGetInput(
    TfeInterpreter* interpreter,
    int* input);
TFAPI(const char*) tfeInterpreterGetInputName(
    TfeInterpreter* interpreter,
    int index);
TFAPI(int) tfeInterpreterResizeInputTensor(
    TfeInterpreter* interpreter,
    int input_index,
    int* input_dims,
    int input_dims_size);
TFAPI(int) tfeInterpreterGetOutputSize(TfeInterpreter* interpreter);
TFAPI(int) tfeInterpreterGetOutput(
    TfeInterpreter* interpreter,
    int* output);
TFAPI(const char*) tfeInterpreterGetOutputName(
    TfeInterpreter* interpreter,
    int index);
TFAPI(void) tfeInterpreterSetNumThreads(
    TfeInterpreter* interpreter,
    int numThreads);
TFAPI(void) tfeInterpreterRelease(TfeInterpreter** interpreter);
TFAPI(int) tfeInterpreterModifyGraphWithDelegate(
    TfeInterpreter* interpreter,
    TfLiteDelegate* delegate);

TFAPI(TfeInterpreterBuilder*) tfeInterpreterBuilderCreate(
    TfeModel* model,
    TfeOpResolver* opResolver);
TFAPI(void) tfeInterpreterBuilderRelease(TfeInterpreterBuilder** builder);
TFAPI(int) tfeInterpreterBuilderBuild(
    TfeInterpreterBuilder* builder,
    TfeInterpreter* interpreter);

TFAPI(int) tfeTensorGetType(TfLiteTensor* tensor);
TFAPI(char*) tfeTensorGetData(TfLiteTensor* tensor);
TFAPI(void) tfeTensorGetQuantizationParams(
    TfLiteTensor* tensor,
    TfLiteQuantizationParams* params);
TFAPI(int) tfeTensorGetAllocationType(TfLiteTensor* tensor);
TFAPI(int) tfeTensorGetByteSize(TfLiteTensor* tensor);
TFAPI(const char*) tfeTensorGetName(TfLiteTensor* tensor);
TFAPI(TfLiteIntArray*) tfeTensorGetDims(TfLiteTensor* tensor);
TFAPI(bool) tfeTensorIsVariable(TfLiteTensor* tensor);
TFAPI(void) tfeMemcpy(void* dst, void* src, int length);

TFAPI(TfeDynamicBuffer*) tfeDynamicBufferCreate();
TFAPI(void) tfeDynamicBufferRelease(TfeDynamicBuffer** buffer);
TFAPI(void) tfeDynamicBufferAddString(
    TfeDynamicBuffer* buffer,
    char* str,
    int len);
TFAPI(void) tfeDynamicBufferWriteToTensor(
    TfeDynamicBuffer* buffer,
    TfLiteTensor* tensor,
    TfLiteIntArray* newShape);

TFAPI(TfLiteIntArray*) tfeIntArrayCreate(int size);
TFAPI(int) tfeIntArrayGetSize(TfLiteIntArray* v);
TFAPI(int*) tfeIntArrayGetData(TfLiteIntArray* v);
TFAPI(void) tfeIntArrayRelease(TfLiteIntArray** v);

TFAPI(TfeStatefulNnApiDelegate*) tfeStatefulNnApiDelegateCreate(TfLiteDelegate** tfLiteDelegate);
TFAPI(void) tfeStatefulNnApiDelegateRelease(TfeStatefulNnApiDelegate** delegate);

TFAPI(TfLiteDelegate*) tfeGpuDelegateV2Create();
TFAPI(void) tfeGpuDelegateV2Delete(TfLiteDelegate** delegate);

TFAPI(TfLiteDelegate*) tfeXNNPackDelegateCreateDefault();
TFAPI(TfLiteDelegate*) tfeXNNPackDelegateCreate(int numThreads);
TFAPI(void) tfeTfLiteDelegateRelease(TfLiteDelegate** delegate);

TFAPI(const char*) tfeGetLiteVersion();

extern "C" typedef int (*TfeErrorCallback)(int status, const char* errMsg);

TFAPI(void) tfeRedirectError(TfeErrorCallback errCallback);
