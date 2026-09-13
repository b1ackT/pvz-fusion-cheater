# -*- coding: utf-8 -*-
"""轻量 PE 分析：节区 / 导出 / 导入（用于判断 IL2CPP 原生层结构与保护情况）"""
import struct, sys, os, collections

def pe_info(path):
    d = open(path, "rb").read()
    out = {}
    out["size"] = len(d)
    e_lfanew = struct.unpack_from("<I", d, 0x3C)[0]
    assert d[e_lfanew:e_lfanew+4] == b"PE\0\0", "not PE"
    coff = e_lfanew + 4
    machine, nsec, timestamp, _, _, optsize, chars = struct.unpack_from("<HHIIIHH", d, coff)
    out["machine"] = {0x8664: "x86-64", 0x14c: "x86"}.get(machine, hex(machine))
    out["timestamp"] = timestamp
    out["dll"] = bool(chars & 0x2000)
    opt = coff + 20
    magic = struct.unpack_from("<H", d, opt)[0]
    out["pe32plus"] = magic == 0x20B
    ddoff = opt + (112 if magic == 0x20B else 96)
    dirs = [struct.unpack_from("<II", d, ddoff + i*8) for i in range(16)]
    # sections
    sec = []
    soff = opt + optsize
    for i in range(nsec):
        name = d[soff+i*40: soff+i*40+8].rstrip(b"\0").decode("latin1")
        vsize, vaddr, rawsize, rawptr = struct.unpack_from("<IIII", d, soff+i*40+8)
        sec.append((name, vaddr, vsize, rawptr, rawsize))
    out["sections"] = sec
    def rva2off(rva):
        for name, vaddr, vsize, rawptr, rawsize in sec:
            if vaddr <= rva < vaddr + max(vsize, rawsize):
                return rawptr + (rva - vaddr)
        return None
    # exports
    exprva, expsize = dirs[0]
    exports = []
    if exprva:
        eo = rva2off(exprva)
        # Characteristics,TimeDateStamp,Major,Minor,Name,Base,NumFuncs,NumNames,AddrFuncs,AddrNames,AddrOrdinals
        _, _, _, _, _, _, nfunc, nname, _, addrnames, _ = struct.unpack_from("<IIHHIIIIIII", d, eo)
        nameoff = rva2off(addrnames)
        for i in range(nname):
            r = struct.unpack_from("<I", d, nameoff+i*4)[0]
            o = rva2off(r)
            end = d.find(b"\0", o)
            exports.append(d[o:end].decode("latin1"))
        out["export_count"] = nfunc
    out["exports"] = exports
    # imports
    imps = collections.OrderedDict()
    imprva, impsize = dirs[1]
    if imprva:
        io = rva2off(imprva)
        while True:
            ent = d[io:io+20]
            if len(ent) < 20 or ent == b"\0"*20: break
            namerva = struct.unpack_from("<I", d, io+12)[0]
            no = rva2off(namerva)
            if no is None: break
            end = d.find(b"\0", no)
            dll = d[no:end].decode("latin1")
            thunk = struct.unpack_from("<I", d, io)[0]
            funcs = []
            if thunk:
                to = rva2off(thunk)
                while True:
                    v = struct.unpack_from("<Q", d, to)[0] if out["pe32plus"] else struct.unpack_from("<I", d, to)[0]
                    if v == 0: break
                    if not (v & (1 << (63 if out["pe32plus"] else 31))):
                        fo = rva2off(v & 0x7FFFFFFF)
                        fe = d.find(b"\0", fo+2)
                        funcs.append(d[fo+2:fe].decode("latin1"))
                    to += 8 if out["pe32plus"] else 4
            imps[dll] = funcs
            io += 20
    out["imports"] = imps
    return out

for path in sys.argv[1:]:
    print("=" * 70)
    print(path, f"({os.path.getsize(path)/1048576:.2f} MB)")
    info = pe_info(path)
    print(f"  machine={info['machine']}  PE32+={info['pe32plus']}  isDLL={info['dll']}  timestamp={info['timestamp']}")
    print("  sections:")
    for name, vaddr, vsize, rawptr, rawsize in info["sections"]:
        print(f"    {name:<10} VA=0x{vaddr:08x} VSize=0x{vsize:08x} Raw=0x{rawsize:08x}")
    ex = info["exports"]
    print(f"  EXPORTS: count={info.get('export_count')} named={len(ex)}")
    il2 = [e for e in ex if e.startswith("il2cpp")]
    print(f"    il2cpp_* exports: {len(il2)}  e.g. {il2[:8]}")
    print(f"    other exports sample: {[e for e in ex if not e.startswith('il2cpp')][:15]}")
    print("  IMPORTS:")
    for dll, funcs in info["imports"].items():
        print(f"    {dll:<28} {len(funcs):>4} funcs  {funcs[:6]}")
