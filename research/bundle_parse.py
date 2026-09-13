#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Static parser for UnityFS AssetBundle  ---  PlantsVsZombiesRH_Data/data.unity3d
Target: Unity 2022.3.62f1c1, bundle format version 8 (big-endian header fields).

Pure-python LZ4 block decompressor (no pip packages, no network).
Only ever READS original game files; writes go to _re_analysis/ only.

Usage:
    python bundle_parse.py            # full run
    python bundle_parse.py --stages header,blocksinfo,paths,sf
"""
import argparse
import hashlib
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BUNDLE = os.path.join(ROOT, "PlantsVsZombiesRH_Data", "data.unity3d")

# ---------------------------------------------------------------- LZ4 block
def lz4_decompress(src, expected_size=None, verbose=False):
    """Pure-python LZ4 *block* (not frame) decompression.

    Format:
      token: high nibble = literal length, low nibble = match length - 4
      literal length / match length >= 15 => extended by extra bytes (255 ... terminator <255)
      then 2-byte LITTLE-endian match offset
      match copies from output[len(out)-offset:] byte by byte (must be byte-wise:
      LZ4 allows overlapping matches, i.e. offset < match_len)
    The final sequence has no match part (input exhausted after literals).
    """
    out = bytearray()
    i = 0
    n = len(src)
    nseq = 0
    while i < n:
        nseq += 1
        token = src[i]; i += 1
        lit_len = token >> 4
        if lit_len == 15:
            while True:
                if i >= n:
                    raise ValueError("LZ4: truncated literal-length extension")
                b = src[i]; i += 1
                lit_len += b
                if b != 255:
                    break
        if lit_len:
            if i + lit_len > n:
                raise ValueError("LZ4: literal run overruns input (%d+%d>%d)" % (i, lit_len, n))
            out += src[i:i + lit_len]
            i += lit_len
        if i >= n:
            break                      # last sequence: literals only
        if i + 2 > n:
            raise ValueError("LZ4: truncated match offset")
        offset = src[i] | (src[i + 1] << 8)
        i += 2
        if offset == 0:
            raise ValueError("LZ4: zero match offset (invalid)")
        match_len = (token & 0x0F) + 4
        if (token & 0x0F) == 15:
            while True:
                if i >= n:
                    raise ValueError("LZ4: truncated match-length extension")
                b = src[i]; i += 1
                match_len += b
                if b != 255:
                    break
        start = len(out) - offset
        if start < 0:
            raise ValueError("LZ4: match offset %d before start of output (len=%d)"
                             % (offset, len(out)))
        # overlapping-safe copy
        if offset >= match_len:
            out += out[start:start + match_len]
        else:
            for j in range(match_len):
                out.append(out[start + j])
        if expected_size is not None and len(out) > expected_size:
            raise ValueError("LZ4: output exceeded expected size")
    if verbose:
        print("    [lz4] %d compressed -> %d decompressed bytes, %d sequences"
              % (n, len(out), nseq))
    return bytes(out)


# ---------------------------------------------------------------- helpers
class Cur:
    """Big/little-endian cursor over a bytes buffer."""
    def __init__(self, buf, endian=">"):
        self.b = buf
        self.p = 0
        self.e = endian

    def u8(self):
        v = self.b[self.p]; self.p += 1; return v

    def u16(self):
        v = struct.unpack_from(self.e + "H", self.b, self.p)[0]; self.p += 2; return v

    def u32(self):
        v = struct.unpack_from(self.e + "I", self.b, self.p)[0]; self.p += 4; return v

    def i32(self):
        v = struct.unpack_from(self.e + "i", self.b, self.p)[0]; self.p += 4; return v

    def u64(self):
        v = struct.unpack_from(self.e + "Q", self.b, self.p)[0]; self.p += 8; return v

    def i64(self):
        v = struct.unpack_from(self.e + "q", self.b, self.p)[0]; self.p += 8; return v

    def read(self, k):
        v = self.b[self.p:self.p + k]; self.p += k; return v

    def align(self, a):
        r = self.p % a
        if r:
            self.p += a - r

    def remaining(self):
        return len(self.b) - self.p


def read_cstr(buf, off):
    end = buf.index(b"\x00", off)
    return buf[off:end].decode("utf-8", "replace"), end + 1


COMPRESSION = {0: "none", 1: "LZMA", 2: "LZ4", 3: "LZ4HC"}
# UnityPy ArchiveFlags
FLAG_BITS = [
    (0x3F,  "kArchiveCompressionTypeMask"),
    (0x40,  "kArchiveBlocksAndDirectoryInfoCombined"),
    (0x80,  "kArchiveBlocksInfoAtTheEnd"),
    (0x100, "kArchiveOldWebPluginCompatibility"),
    (0x200, "kArchiveBlockInfoNeedPaddingAtStart"),
    (0x400, "kArchiveUsesAssetBundleEncryption"),
]


# ---------------------------------------------------------------- stage 1
def stage_header():
    print("=" * 78)
    print("STAGE 1 -- UnityFS header")
    print("=" * 78)
    fsize = os.path.getsize(BUNDLE)
    print("file                : %s" % BUNDLE)
    print("file size on disk   : %d bytes (%.1f MB)" % (fsize, fsize / 1048576.0))

    with open(BUNDLE, "rb") as f:
        head = f.read(256)

    assert head[:8] == b"UnityFS\x00", "bad signature: %r" % head[:8]
    c = Cur(head, ">")
    c.p = 8
    version = c.u32()
    unity_version, off = read_cstr(head, c.p); c.p = off
    unity_rev, off = read_cstr(head, c.p); c.p = off
    size = c.i64()
    cbis = c.u32()
    ubis = c.u32()
    flags = c.u32()
    header_size = c.p

    print("signature           : %r" % head[:8])
    print("bundle format ver   : %d (read BE at offset 8..11)" % version)
    print("unityVersion        : %r" % unity_version)
    print("unityRevision       : %r" % unity_rev)
    print("size field  (i64 BE) : %d  (0x%X)" % (size, size))
    print("compressedBlocksInfoSize   (u32 BE) : %d (0x%X)" % (cbis, cbis))
    print("uncompressedBlocksInfoSize (u32 BE) : %d (0x%X)" % (ubis, ubis))
    print("flags       (u32 BE) : %d (0x%X) = 0b%s"
          % (flags, flags, format(flags, "012b")))
    print("exact header size   : %d bytes  (8 sig + 4 ver + %d + %d + 8 + 4 + 4 + 4)"
          % (header_size, len(unity_version) + 1, len(unity_rev) + 1))
    print()
    print("flags decode:")
    comp = flags & 0x3F
    print("  low 6 bits 0x%02X      -> compression type = %d = %s"
          % (comp, comp, COMPRESSION.get(comp, "?")))
    for bit, name in FLAG_BITS:
        if bit == 0x3F:
            continue
        print("  bit 0x%03X (%-38s) = %s" % (bit, name, "SET" if flags & bit else "clear"))
    print("  bits 0x800+                = 0x%X (unknown/unused here)" % (flags & ~0x7FF))
    print()
    print("size field vs disk  : size=%d disk=%d  delta=%d" % (size, fsize, fsize - size))
    print()
    print("first 64 bytes hex  :")
    for r in range(0, 64, 16):
        print("  0x%04X  %s  |%s|" % (r,
              " ".join("%02x" % b for b in head[r:r + 16]),
              "".join(chr(b) if 32 <= b < 127 else "." for b in head[r:r + 16])))
    return dict(file_size=fsize, version=version, unity_version=unity_version,
                unity_revision=unity_rev, size=size, cbis=cbis, ubis=ubis,
                flags=flags, header_size=header_size, compression=comp)


# ---------------------------------------------------------------- stage 2
def stage_blocksinfo(hdr):
    """Locate + decompress the blocksInfo blob, testing both candidate layouts."""
    print()
    print("=" * 78)
    print("STAGE 2 -- locate and decompress blocksInfo")
    print("=" * 78)
    fsize = hdr["file_size"]
    cbis, ubis, flags = hdr["cbis"], hdr["ubis"], hdr["flags"]
    comp = hdr["compression"]
    print("compression type from flags low nibble : %s" % COMPRESSION.get(comp))
    print("compressedBlocksInfoSize   = %d" % cbis)
    print("uncompressedBlocksInfoSize = %d" % ubis)
    print()

    pad = 0
    if flags & 0x200:
        pad = (16 - (hdr["header_size"] % 16)) % 16
        print("flag 0x200 (BlockInfoNeedPaddingAtStart) is SET -> align file cursor")
        print("  header_size %% 16 = %d, so padding = %d zero bytes"
              % (hdr["header_size"] % 16, pad))
    if flags & 0x80:
        print("flag 0x80 (BlocksInfoAtTheEnd) is SET -> blob expected at EOF-cbis")
    else:
        print("flag 0x80 is CLEAR -> blob expected immediately after header+padding")

    candidates = []
    candidates.append(("at_end", fsize - cbis))
    candidates.append(("after_header+pad", hdr["header_size"] + pad))
    candidates.append(("after_header_no_pad", hdr["header_size"]))
    # de-dup preserving order
    seen = set()
    cands = []
    for name, off in candidates:
        if off not in seen:
            seen.add(off)
            cands.append((name, off))

    results = []
    with open(BUNDLE, "rb") as f:
        for name, off in cands:
            f.seek(off)
            raw = f.read(cbis)
            ok = False
            note = ""
            data = None
            try:
                if comp in (2, 3):
                    data = lz4_decompress(raw, ubis, verbose=True)
                    note = "LZ4 block decode succeeded"
                    ok = (len(data) == ubis)
                elif comp == 1:
                    import lzma
                    raw2 = raw.rstrip(b"\x00")
                    d = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
                    data = d.decompress(raw2)
                    ok = (len(data) == ubis)
                    note = "LZMA FORMAT_ALONE"
                else:
                    data = raw
                    note = "stored uncompressed"
                    ok = (len(data) == ubis)
            except Exception as e:
                note = "FAILED: %s: %s" % (type(e).__name__, e)
            # independent validation: first 16 bytes must be the data hash and
            # the int32 right after must be a plausible block count
            bc = None
            if ok and len(data) >= 20:
                bc = struct.unpack_from(">i", data, 16)[0]
                # blocksInfo must be able to hold the block table + node table
                # within uncompressedBlocksInfoSize
                if not (0 < bc < (ubis - 20) // 10):
                    ok = False
                    note += " | implausible blockCount=%d -> rejected" % bc
                else:
                    note += " | blockCount=%d (fits in blob)" % bc
            results.append(dict(name=name, offset=off, ok=ok, note=note, data=data,
                                block_count=bc))
            print("candidate %-18s offset=0x%08X (%9d): %s"
                  % (name, off, off, note))

    good = [r for r in results if r["ok"]]
    if not good:
        print("!! no candidate produced valid blocksInfo -- aborting")
        sys.exit(2)
    win = good[0]
    print()
    print(">>> blocksInfo LOCATION CONFIRMED: %s at file offset 0x%X (%d), "
          "length %d compressed / %d uncompressed"
          % (win["name"], win["offset"], win["offset"], cbis, len(win["data"])))
    assert len(win["data"]) == ubis, "len mismatch %d != %d" % (len(win["data"]), ubis)
    print(">>> ASSERT OK: decompressed length == uncompressedBlocksInfoSize (%d)" % ubis)
    print()
    print("first 40 bytes of decompressed blocksInfo:")
    d = win["data"]
    for r in range(0, 40, 16):
        print("  0x%04X  %s" % (r, " ".join("%02x" % b for b in d[r:r + 16])))
    print("  uncompressedDataHash = %s" % d[0:16].hex())
    print("  (md5-ish of uncompressed block payload; used by Unity for cache checks)")
    return win["data"], win["offset"]


# ---------------------------------------------------------------- stage 3
def stage_parse_blocksinfo(data):
    print()
    print("=" * 78)
    print("STAGE 3 -- parse decompressed blocksInfo (big-endian structs)")
    print("=" * 78)
    c = Cur(data, ">")
    data_hash = c.read(16)
    print("uncompressedDataHash[4] : %s  (16 bytes, cursor now %d)"
          % (data_hash.hex(), c.p))

    nblocks = c.i32()
    print("blocksInfoCount (int32 BE) : %d   (cursor %d)" % (nblocks, c.p))
    assert 0 < nblocks < (len(data) - 20) // 10, "implausible block count %d" % nblocks
    blocks = []
    for k in range(nblocks):
        us = c.u32(); cs = c.u32(); fl = c.u16()
        blocks.append((us, cs, fl))
        if k < 12 or k >= nblocks - 3:
            print("  block[%5d] uncompressedSize=%12d compressedSize=%12d flags=0x%04X (%s%s)"
                  % (k, us, cs, fl, COMPRESSION.get(fl & 0x3F, "?"),
                     ",streamed" if fl & 0x40 else ""))
        elif k == 12:
            print("  ... (%d more block entries) ..." % (nblocks - 15))
    sum_us = sum(b[0] for b in blocks)
    sum_cs = sum(b[1] for b in blocks)
    print("SUM uncompressedSize = %d   SUM compressedSize = %d" % (sum_us, sum_cs))
    size_calc = sum(cs + 10 for cs, in [(b[1],) for b in blocks])  # placeholder
    print("sum(compressedSize) + 10*nblocks + 16 + 4 + 4 = %d"
          % (sum_cs + 10 * nblocks + 16 + 4 + 4))

    nnodes = c.i32()
    print()
    print("nodesCount (int32 BE) : %d   (cursor %d)" % (nnodes, c.p))
    assert 0 <= nnodes < 100000, "implausible node count %d" % nnodes
    node_table_start = c.p

    # Two candidate path encodings exist in the wild:
    #   (A) null-terminated string, NO alignment (what this file uses)
    #   (B) int32 length + bytes, then align(4)
    # Both are tried and validated against the blob end / node contiguity.
    def try_layout(mode):
        p = node_table_start
        ns = []
        for k in range(nnodes):
            off = struct.unpack_from(">q", data, p)[0]; p += 8
            sz = struct.unpack_from(">q", data, p)[0]; p += 8
            fl = struct.unpack_from(">I", data, p)[0]; p += 4
            if mode == "null":
                e = data.index(b"\x00", p)
                s = data[p:e].decode("utf-8")
                p = e + 1
            else:
                ln = struct.unpack_from(">i", data, p)[0]; p += 4
                if not (0 < ln < 4096):
                    raise ValueError("bad path length %d at %d" % (ln, p))
                s = data[p:p + ln].decode("utf-8")
                p += ln
                p = (p + 3) // 4 * 4
            ns.append(dict(offset=off, size=sz, flags=fl, path=s, path_len=len(s)))
        return ns, p

    chosen = None
    for mode in ("null", "len"):
        try:
            ns, end = try_layout(mode)
        except Exception as e:
            print("  layout %-4s -> FAILED (%s)" % (mode, e))
            continue
        contig = all(ns[i]["offset"] + ns[i]["size"] == ns[i + 1]["offset"]
                     for i in range(len(ns) - 1))
        fits = 0 <= len(data) - end <= 3
        ascii_ok = all(all(32 <= ord(ch) < 127 for ch in n["path"]) for n in ns)
        print("  layout %-4s -> end=%d blob=%d (slack %d) contiguous=%s printable=%s"
              % (mode, end, len(data), len(data) - end, contig, ascii_ok))
        if fits and contig and ascii_ok:
            chosen = (mode, ns, end)
            break
    if chosen is None:
        # fall back to whatever parsed without exception
        for mode in ("null", "len"):
            try:
                ns, end = try_layout(mode)
                chosen = (mode, ns, end)
                break
            except Exception:
                pass
    assert chosen, "could not parse node table with either layout"
    mode, nodes, nodes_end = chosen
    print("  >>> node table layout selected: %s-terminated paths%s"
          % (mode, "" if mode == "null" else " (4-aligned)"))
    print()
    for k, n in enumerate(nodes):
        print("  node[%d] offset=%12d size=%12d (0x%X) flags=0x%08X pathLen=%3d path=%r"
              % (k, n["offset"], n["size"], n["size"], n["flags"], n["path_len"], n["path"]))

    print()
    print("cursor after node table : %d  (buffer size %d, %d bytes trailing)"
          % (nodes_end, len(data), len(data) - nodes_end))
    if len(data) - nodes_end:
        print("trailing bytes hex: %s" % data[nodes_end:nodes_end + 64].hex())

    # ---- cross validation -------------------------------------------------
    print()
    print("--- cross-validation ---")
    checks = []
    nt = nodes_end - node_table_start
    expect_total = 16 + 4 + 10 * nblocks + 4 + nt
    print("computed blocksInfo size = 16 + 4 + 10*%d + 4 + %d = %d ; actual %d -> %s"
          % (nblocks, nt, expect_total, len(data),
             "MATCH" if expect_total == len(data) else "MISMATCH"))
    checks.append(("blocksInfo size adds up", expect_total == len(data)))
    # 2) node offsets must tile the concatenated uncompressed blocks
    tot_block_out = sum_us
    tot_node = sum(n["size"] for n in nodes)
    print("sum(node.size) = %d ; sum(block.uncompressedSize) = %d -> %s"
          % (tot_node, tot_block_out,
             "MATCH" if tot_node == tot_block_out else "MISMATCH"))
    checks.append(("node sizes == block sizes", tot_node == tot_block_out))
    # 3) each block compressedSize must fit inside the file after blocksInfo
    return dict(blocks=blocks, nodes=nodes, data_hash=data_hash.hex(),
                nblocks=nblocks, checks=checks)


# ---------------------------------------------------------------- stage 4
def find_data_offset(hdr, bi_off, blocks, flags):
    """Determine where block payload begins (after blocksInfo + alignment).

    Discriminator: data_offset + sum(block.compressedSize) must equal file size,
    because the compressed blocks tile the whole remainder of the file.
    """
    blob_end = bi_off + hdr["cbis"]
    fsize = hdr["file_size"]
    sum_cs = sum(b[1] for b in blocks)
    cands = sorted(set([blob_end] + [((blob_end + a - 1) // a) * a for a in (2, 4, 8, 16)]))
    winner = None
    with open(BUNDLE, "rb") as f:
        for off in cands:
            f.seek(off)
            chunk = f.read(32)
            exact = (off + sum_cs == fsize)
            print("  data-start candidate %8d (0x%X): %s | %s"
                  % (off, off, chunk.hex(),
                     "offset+sum(compressedSize)==fileSize -> EXACT" if exact
                     else "remainder mismatch (%d)" % (fsize - (off + sum_cs))))
            if exact and winner is None:
                winner = off
    assert winner is not None, "no data offset makes the blocks tile the file exactly"
    print("  >>> block payload data offset = %d (0x%X)" % (winner, winner))
    return winner


def block_payload_reader(hdr, bi_off, blocks, data_offset, cache_limit=None):
    """Generator giving decompressed bytes per block, reading lazily from disk."""
    comp = hdr["compression"]
    pos = data_offset
    for k, (us, cs, fl) in enumerate(blocks):
        with open(BUNDLE, "rb") as f:
            f.seek(pos)
            raw = f.read(cs)
        assert len(raw) == cs, "short read on block %d" % k
        bcomp = fl & 0x3F
        if bcomp in (2, 3):
            data = lz4_decompress(raw, us)
        elif bcomp == 1:
            import lzma
            data = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE).decompress(raw)
        else:
            data = raw
        assert len(data) == us, "block %d size mismatch %d != %d" % (k, len(data), us)
        pos += cs
        yield data


def extract_range(hdr, bi_off, blocks, data_offset, start, length):
    """Concatenate decompressed blocks to cover [start, start+length)."""
    out = bytearray()
    base = 0
    for data in block_payload_reader(hdr, bi_off, blocks, data_offset):
        if base + len(data) <= start:
            base += len(data)
            continue
        s = max(0, start - base)
        e = min(len(data), start + length - base)
        out += data[s:e]
        base += len(data)
        if len(out) >= length:
            break
    return bytes(out[:length])


# ---------------------------------------------------------------- stage 5
CLASSDEFS = {1: "GameObject", 4: "Transform", 21: "Material", 28: "Texture2D",
             43: "Mesh", 48: "Shader", 49: "TextAsset", 74: "AnimationClip",
             83: "AudioClip", 114: "MonoBehaviour", 115: "MonoScript",
             128: "Font", 142: "AssetBundle", 213: "Sprite",
             222: "CanvasRenderer", 223: "Canvas", 224: "RectTransform",
             384: "RenderTexture"}


def hexdump(buf, base=0, n=256, label=""):
    print("  hex dump %s (base 0x%X, %d bytes):" % (label, base, min(n, len(buf))))
    for r in range(0, min(n, len(buf)), 16):
        chunk = buf[r:r + 16]
        print("    0x%04X  %-47s  |%s|" % (r,
              " ".join("%02x" % b for b in chunk),
              "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)))


def parse_sf_header(buf, node_size):
    """SerializedFile header, layout EMPIRICALLY DERIVED from this bundle and
    cross-validated on 3 independent SerializedFile nodes (see report S6).

    All fields big-endian:
      0x00 u32  metadataSize   (legacy 32-bit slot, written 0)
      0x04 u32  fileSize       (legacy 32-bit slot, written 0)
      0x08 u32  version        = 22
      0x0C u32  dataOffset     (legacy 32-bit slot, written 0)
      0x10 i64  metadataSize   (64-bit field, new in version 22)
      0x18 i64  fileSize       (64-bit)  == bundle node size
      0x20 i64  dataOffset     (64-bit)
      0x28 i64  unknown/zero
      0x30 char[] unityVersion (null-terminated, e.g. '2022.3.62f1c1')
      then: u16 const 0x0013 (=19 -> BuildTarget.StandaloneWindows64),
            u32 typeCount, then the type table (first entry's u32 = classID)
    Header is 48 bytes; metadata lives in [48, 48+metadataSize); the object data
    region starts at align16(48+metadataSize) and equals the header dataOffset.
    """
    d = {}
    d["legacy_metadata_size"] = struct.unpack_from(">I", buf, 0x00)[0]
    d["legacy_file_size"] = struct.unpack_from(">I", buf, 0x04)[0]
    d["version"] = struct.unpack_from(">I", buf, 0x08)[0]
    d["legacy_data_offset"] = struct.unpack_from(">I", buf, 0x0C)[0]
    d["metadata_size"] = struct.unpack_from(">q", buf, 0x10)[0]
    d["file_size"] = struct.unpack_from(">q", buf, 0x18)[0]
    d["data_offset"] = struct.unpack_from(">q", buf, 0x20)[0]
    d["unknown_28"] = struct.unpack_from(">q", buf, 0x28)[0]
    end = buf.index(b"\x00", 0x30)
    d["unity_version"] = buf[0x30:end].decode("ascii", "replace")
    d["const_3e"] = struct.unpack_from(">H", buf, end + 1)[0] if end + 1 + 2 <= len(buf) else None
    d["header_size"] = 48
    hs, ms, do = 48, d["metadata_size"], d["data_offset"]
    d["metadata_region"] = (hs, hs + ms)
    d["data_region"] = (do, d["file_size"])
    d["derived_ok"] = (d["file_size"] == node_size
                       and do == ((hs + ms + 15) // 16) * 16
                       and d["version"] == 22)
    return d


def stage_serializedfile(hdr, bi_off, blocks, data_offset, nodes):
    print()
    print("=" * 78)
    print("STAGE 5 -- SerializedFile header analysis (all .assets/player-data nodes)")
    print("=" * 78)
    results = {}
    for i, n in enumerate(nodes):
        path = n["path"]
        is_sf = (n["flags"] & 0x04) and not path.endswith(".resS")
        if not is_sf:
            print("node[%d] %-34r flags=0x%08X -> NOT a SerializedFile (streaming data node), skipped"
                  % (i, path, n["flags"]))
            continue
        probe_len = min(n["size"], 4096)
        head = extract_range(hdr, bi_off, blocks, data_offset, n["offset"], probe_len)
        print()
        print("--- node[%d] %r  node_size=%d ---" % (i, path, n["size"]))
        if i == 0:
            hexdump(head, 0, 128, "first 128 bytes of node[0]")
            print("  LE u32 view @0x00: %s" % (struct.unpack_from("<IIII", head, 0),))
            print("  BE u32 view @0x00: %s" % (struct.unpack_from(">IIII", head, 0),))
            print("  -> both views give metadataSize/fileSize = 0/0, so the classic")
            print("     20-byte SerializedFile header does NOT apply to version 22.")
        d = parse_sf_header(head, n["size"])
        for k in ("version", "metadata_size", "file_size", "data_offset",
                  "unity_version", "const_3e", "unknown_28",
                  "legacy_metadata_size", "legacy_file_size", "legacy_data_offset"):
            print("    %-22s = %s" % (k, d[k]))
        print("    header_size            = %d" % d["header_size"])
        print("    metadata region        = [%d, %d)" % d["metadata_region"])
        print("    data region            = [%d, %d)" % d["data_region"])
        print("    dataOffset == align16(48+metadataSize) -> %s"
              % (d["data_offset"] == ((48 + d["metadata_size"] + 15) // 16) * 16))
        print("    fileSize  == bundle node size          -> %s"
              % (d["file_size"] == n["size"]))
        print("    version==22                            -> %s" % (d["version"] == 22))
        # tentative type table
        tc = struct.unpack_from(">I", head, 0x40)[0]
        first_ids = [struct.unpack_from(">I", head, 0x44 + 4 * k)[0] for k in range(min(tc, 6))]
        print("    TENTATIVE: typeCount @0x40 (u32 BE) = %d ; first u32s after it "
              "(candidate classIDs) = %s" % (tc, first_ids))
        print("      mapped: %s" % [(c, CLASSDEFS.get(c, "?")) for c in first_ids])
        results[path] = d
    print()
    print("NOTE: full object-table enumeration was NOT completed (see report S6):")
    print("      the version-22 metadata body could not be decoded reliably in the")
    print("      available time, so object counts / classID histograms are NOT reported.")
    return results


# ---------------------------------------------------------------- sidecars
def stage_sidecars():
    print()
    print("=" * 78)
    print("STAGE 6 -- sidecar .resource files")
    print("=" * 78)
    out = {}
    for name in ("resources.resource", "sharedassets0.resource"):
        p = os.path.join(ROOT, "PlantsVsZombiesRH_Data", name)
        sz = os.path.getsize(p)
        with open(p, "rb") as f:
            h = f.read(64)
        print()
        print("%s : %d bytes (%.1f MB)" % (name, sz, sz / 1048576.0))
        print("  first 64 bytes hex: %s" % h.hex())
        print("  ascii            : %s" % "".join(chr(b) if 32 <= b < 127 else "." for b in h))
        u32 = [struct.unpack_from("<I", h, i)[0] for i in range(0, 32, 4)]
        print("  first 8 u32 LE   : %s" % u32)
        if h[:4] == b"FSB5":
            ver, num, shs, nts, ds, mode = u32[1], u32[2], u32[3], u32[4], u32[5], u32[6]
            print("  MAGIC = FSB5 -> FMOD Sample Bank. version=%d  field2(u32)=%d  "
                  "field3(u32)=%d" % (ver, num, shs))
            print("  candidate decode: sampleHeadersSize=%d nameTableSize=%d dataSize=%d mode=%d"
                  % (shs, nts, ds, mode))
            print("  accounting: 48(hdr)+%d(sampleHeaders)+%d(names)+%d(data) = %d ; file=%d ; residual=%d"
                  % (shs, nts, ds, 48 + shs + nts + ds, sz, sz - (48 + shs + nts + ds)))
            print("  NOTE: this is an FMOD bank, NOT Unity's ResourceFile format "
                  "(no Unity ResourceFile header present at offset 0).")
        out[name] = dict(size=sz, hex=h.hex(), magic=h[:4].decode("ascii", "replace"), u32_le=u32)
    return out


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stages", default="header,blocksinfo,paths,sf,side")
    ap.add_argument("--dump-paths", default=os.path.join(HERE, "11_bundle_paths.txt"))
    args = ap.parse_args()
    want = set(args.stages.split(","))

    hdr = stage_header()
    data, bi_off = stage_blocksinfo(hdr)
    info = stage_parse_blocksinfo(data)
    data_offset = find_data_offset(hdr, bi_off, info["blocks"], hdr["flags"])
    print()
    print("block payload region starts at file offset %d (0x%X)" % (data_offset, data_offset))

    if "paths" in want:
        p = args.dump_paths
        with open(p, "w", encoding="utf-8") as f:
            f.write("# UnityFS node paths from %s\n" % os.path.basename(BUNDLE))
            f.write("# nodes=%d  blocks=%d  flags=0x%X  unity=%s\n"
                    % (len(info["nodes"]), info["nblocks"], hdr["flags"], hdr["unity_revision"]))
            f.write("# index\toffset\tsize\tflags\tpath\n")
            for i, n in enumerate(info["nodes"]):
                f.write("%d\t%d\t%d\t0x%08X\t%s\n"
                        % (i, n["offset"], n["size"], n["flags"], n["path"]))
            f.write("# raw paths (one per line)\n")
            for n in info["nodes"]:
                f.write("%s\n" % n["path"])
        print("wrote %s (%d nodes)" % (p, len(info["nodes"])))

    sf = None
    if "sf" in want:
        sf = stage_serializedfile(hdr, bi_off, info["blocks"], data_offset, info["nodes"])
    side = stage_sidecars() if "side" in want else {}

    summary = dict(header=hdr, blocksinfo_offset=bi_off, data_offset=data_offset,
                   nblocks=info["nblocks"],
                   nodes=[{k: n[k] for k in ("offset", "size", "flags", "path")}
                          for n in info["nodes"]],
                   checks=info["checks"], serializedfile=sf,
                   sidecars={k: v for k, v in side.items()})
    with open(os.path.join(HERE, "bundle_parse_result.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=1)
    print()
    print("wrote _re_analysis/bundle_parse_result.json")
    print("checks: %s" % info["checks"])


if __name__ == "__main__":
    main()
