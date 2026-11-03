"""The static archives a target links that cc_shared_library leaves out.

cc_shared_library walks its deps with an aspect that follows cc rules only, so what a library reaches through other rules
- LiteRT-LM's Rust components (rust_library: minijinja, llguidance, the tool-call parsers, tokenizers and Rust's own
standard library) and objc_library (TFLite's iOS signpost profiler) - isn't linked, and LiteRT-LM's
-undefined dynamic_lookup hides that until the app calls it. This rule takes those archives from the target's CcInfo,
where a cc_binary finds them, so they can be passed to the shared library's link directly: the archives (to make them
inputs of the link) and, in the "filelist" output group, a list of their paths for the linker's -filelist (there are too
many for $(locations), which would pass them as one argument).
"""

def _static_archives_impl(ctx):
    archives = []
    for linker_input in ctx.attr.dep[CcInfo].linking_context.linker_inputs.to_list():
        for library in linker_input.libraries:
            archive = library.static_library or library.pic_static_library
            if archive and [p for p in ctx.attr.path_contains if p in archive.path]:
                archives.append(archive)
    filelist = ctx.actions.declare_file(ctx.label.name + ".filelist")
    ctx.actions.write(filelist, "".join([a.path + "\n" for a in archives]))
    return [
        DefaultInfo(files = depset(archives)),
        OutputGroupInfo(filelist = depset([filelist])),
    ]

static_archives = rule(
    implementation = _static_archives_impl,
    attrs = {
        "dep": attr.label(providers = [CcInfo]),
        # An archive is taken when its path contains one of these.
        "path_contains": attr.string_list(),
    },
)
