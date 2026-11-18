// Minimal miniz replacement, backed by zlib.
//
// ffx_blob_decompress.h includes <miniz.h> and calls mz_uncompress() when a
// shader blob starts with FFX_BLOB_COMPRESS_MAGIC. The tables this repository
// generates are raw SPIR-V, so the call is a passthrough in practice; zlib gives
// the same stream semantics for the case where a compressed blob is ever used.
#pragma once

#include <zlib.h>

typedef unsigned char mz_uint8;
typedef unsigned long mz_ulong;

#define MZ_OK Z_OK

static inline int mz_uncompress(unsigned char* dest, mz_ulong* destLen,
                                const unsigned char* source, mz_ulong sourceLen) {
    uLongf written = static_cast<uLongf>(*destLen);
    const int status = uncompress(dest, &written, source,
                                  static_cast<uLong>(sourceLen));
    *destLen = written;
    return status;
}
