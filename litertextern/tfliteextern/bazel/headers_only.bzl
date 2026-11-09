"""A rule that gives its users the headers of its deps, and none of their code.

The Windows tfliteextern.dll is compiled against the TensorFlow Lite C API's headers and linked against libLiteRt.dll's
import library (the runtime lives there), so the C API's libraries must not be linked into it a second time.
"""

def _headers_only_impl(ctx):
    contexts = [dep[CcInfo].compilation_context for dep in ctx.attr.deps]
    return [CcInfo(compilation_context = cc_common.merge_compilation_contexts(compilation_contexts = contexts))]

headers_only = rule(
    implementation = _headers_only_impl,
    attrs = {"deps": attr.label_list(providers = [CcInfo])},
    provides = [CcInfo],
)
