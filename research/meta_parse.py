# -*- coding: utf-8 -*-
"""IL2CPP global-metadata.dat (v31, Unity 2022.3) 解析：程序集 / 命名空间 / 类型 / 字符串字面量"""
import struct, io, os, sys, collections, re

META = r"PlantsVsZombiesRH_Data\il2cpp_data\Metadata\global-metadata.dat"
OUT = r"_re_analysis"
data = open(META, "rb").read()

HDR = [
 "stringLiteralOffset","stringLiteralSize","stringLiteralDataOffset","stringLiteralDataSize",
 "stringOffset","stringSize","eventsOffset","eventsSize","propertiesOffset","propertiesSize","methodsOffset","methodsSize",
 "paramDefaultOffset","paramDefaultSize","fieldDefaultOffset","fieldDefaultSize",
 "fieldAndParamDefaultDataOffset","fieldAndParamDefaultDataSize","fieldMarshaledOffset","fieldMarshaledSize",
 "parametersOffset","parametersSize","fieldsOffset","fieldsSize","genericParamsOffset","genericParamsSize",
 "genericContainersOffset","genericContainersSize","genericParamConstraintsOffset","genericParamConstraintsSize",
 "nestedTypesOffset","nestedTypesSize","interfacesOffset","interfacesSize","vtableMethodsOffset","vtableMethodsSize",
 "interfaceOffsetsOffset","interfaceOffsetsSize","typeDefinitionsOffset","typeDefinitionsSize","imagesOffset","imagesSize",
 "assembliesOffset","assembliesSize","fieldRefsOffset","fieldRefsSize","referencedAssembliesOffset","referencedAssembliesSize",
 "attributeDataOffset","attributeDataSize","attributeDataRangeOffset","attributeDataRangeSize",
 "unresolvedVirtualCallOffset","unresolvedVirtualCallSize","unresolvedVirtualCallParameterTypesOffset",
 "unresolvedVirtualCallParameterTypesSize","unresolvedVirtualCallParameterRangesOffset",
 "unresolvedVirtualCallParameterRangesSize","windowsRuntimeTypeNamesOffset","windowsRuntimeTypeNamesSize",
 "windowsRuntimeStringsOffset","windowsRuntimeStringsSize","exportedTypeDefinitionsOffset","exportedTypeDefinitionsSize",
]
h = {name: struct.unpack_from("<I", data, 8 + i*4)[0] for i, name in enumerate(HDR)}
assert struct.unpack_from("<I", data, 0)[0] == 0xFAB11BAF, "bad magic"
assert struct.unpack_from("<i", data, 4)[0] == 31, "unexpected version"
print("magic OK, version =", struct.unpack_from("<i", data, 4)[0])

# ---- 字符串表 ----
strtab = data[h["stringOffset"]: h["stringOffset"] + h["stringSize"]]
def s(idx):
    if idx < 0: return ""
    end = strtab.find(b"\x00", idx)
    return strtab[idx:end].decode("utf-8", "replace")

# ---- 字符串字面量 ----
lits = []
off, cnt = h["stringLiteralOffset"], h["stringLiteralSize"] // 8
datapool = data[h["stringLiteralDataOffset"]: h["stringLiteralDataOffset"] + h["stringLiteralDataSize"]]
for i in range(cnt):
    length, didx = struct.unpack_from("<Ii", data, off + i*8)
    lits.append(datapool[didx:didx+length].decode("utf-8", "replace"))
print("string literals:", len(lits))

# ---- 程序集 (images) ----
IMAGES_OFF, NDEF = h["imagesOffset"], h["typeDefinitionsOffset"]
n_img = h["imagesSize"] // 40
images = []
for i in range(n_img):
    b = IMAGES_OFF + i*40
    nameIdx, asmIdx, tstart, tcount, etstart, etcount, epIdx, token, castart, cacount = struct.unpack_from("<10i", data, b)
    images.append(dict(name=s(nameIdx), asm=asmIdx, tstart=tstart, tcount=tcount))
print("assemblies:", len(images))

# ---- 类型定义 ----
TD_SIZE = 88
TD_FMT = "<16i8H2I"
n_td = h["typeDefinitionsSize"] // TD_SIZE
types = []
for i in range(n_td):
    f = struct.unpack_from(TD_FMT, data, NDEF + i*TD_SIZE)
    types.append(dict(name=s(f[0]), ns=s(f[1]), decl=f[3], parent=f[4], flags=f[7],
                      mcount=f[16] if False else 0))
print("type definitions:", n_td)

td_by_index = types
# 归属到程序集
for img in images:
    img["types"] = [(i, types[i]) for i in range(img["tstart"], img["tstart"]+img["tcount"])]

# ---- 输出 1: 程序集列表 ----
with open(os.path.join(OUT, "01_assemblies.txt"), "w", encoding="utf-8") as f:
    f.write(f"{'assembly':<45}{'types':>8}\n" + "-"*55 + "\n")
    for img in sorted(images, key=lambda x: -x["tcount"]):
        f.write(f"{img['name']:<45}{img['tcount']:>8}\n")

# ---- 输出 2: 全类型表（带程序集、命名空间）----
with open(os.path.join(OUT, "02_types.txt"), "w", encoding="utf-8") as f:
    for img in images:
        for i, t in img["types"]:
            if t["decl"] >= 0:      # 嵌套类型单独标注
                continue
            f.write(f"{img['name']}\t{t['ns']}\t{t['name']}\n")

# ---- 输出 3: 游戏逻辑命名空间统计（排除 Unity/System/第三方库）----
LIB_PREFIX = ("System", "Unity", "Microsoft", "Mono", "JetBrains", "Cysharp", "Spine", "Newtonsoft",
              "DG.Tweening", "TMPro", "AOT", "I18N", "Internal", "Mono.Security", "BepInEx", "HarmonyLib",
              "Interop", "MS.", "Mono.Cecil", "Mono.Net", "Mono.Xml", "Mono.Xml")
def is_game(img, t):
    n = img["name"]
    if n.startswith(("UnityEngine", "Unity.", "System", "mscorlib", "netstandard", "Mono.", "Newtonsoft",
                     "System.", "Microsoft", "Cysharp", "Spine", "DG.", "TMPro", "0Harmony", "HarmonyLib",
                     "UniTask", "Burst", "UnityEngine.")):
        return False
    ns = t["ns"]
    if ns.startswith(("System", "Unity", "Microsoft", "Mono", "Cysharp", "Spine", "Newtonsoft", "DG.", "TMPro")):
        return False
    return True

ns_count = collections.Counter()
for img in images:
    for i, t in img["types"]:
        if is_game(img, t):
            ns_count[t["ns"] or "(global)"] += 1
with open(os.path.join(OUT, "03_namespaces.txt"), "w", encoding="utf-8") as f:
    for ns, c in ns_count.most_common():
        f.write(f"{c:>6}  {ns}\n")

# ---- 输出 4: 非 Unity 程序集里的类型清单（游戏逻辑主体）----
with open(os.path.join(OUT, "04_game_types.txt"), "w", encoding="utf-8") as f:
    for img in images:
        if img["name"].startswith(("UnityEngine", "Unity.", "System", "mscorlib", "netstandard", "Mono.",
                                   "Newtonsoft", "Microsoft", "Cysharp", "Spine", "DG.", "TMPro", "0Harmony",
                                   "HarmonyLib", "UniTask", "Burst", "Unity.")):
            continue
        f.write(f"\n===== {img['name']}  ({img['tcount']} types) =====\n")
        for i, t in img["types"]:
            if t["decl"] < 0:
                f.write(f"  {t['ns']}.{t['name']}\n" if t["ns"] else f"  {t['name']}\n")

# ---- 输出 5: 有趣字符串字面量 ----
cjk = re.compile(r"[\u4e00-\u9fff]")
pats = [
    ("URL/网络", re.compile(r"https?://|www\.|api\.|\.com|\.cn|\.net/", re.I)),
    ("文件路径", re.compile(r"\.(dll|json|txt|png|asset|unity3d|dat|xml|ini|cfg|bundle)$|[/\\][A-Za-z_]+[/\\]", re.I)),
    ("调试/作弊", re.compile(r"cheat|debug|gm|console|admin|unlock|secret|dev|test|fps|god", re.I)),
    ("游戏玩法", re.compile(r"plant|zombie|sun|seed|wave|level|damage|attack|hp|coin|card|fusion|merge|"
                          r"植物|僵尸|阳光|关卡|波|伤害|融合|卡片|豌豆", re.I)),
]
with open(os.path.join(OUT, "05_strings_interesting.txt"), "w", encoding="utf-8") as f:
    f.write(f"# 字符串字面量总数: {len(lits)}\n")
    n_cjk = sum(1 for x in lits if cjk.search(x))
    f.write(f"# 含中日韩文字的: {n_cjk}\n\n")
    for title, rx in pats:
        hits = sorted({x for x in lits if len(x) < 220 and rx.search(x)})
        f.write(f"\n########## {title}  ({len(hits)}) ##########\n")
        for x in hits:
            f.write(x.replace("\n", "\\n") + "\n")

# 纯中文文本（UI 文案）
with open(os.path.join(OUT, "06_chinese_text.txt"), "w", encoding="utf-8") as f:
    seen = set()
    for x in lits:
        if cjk.search(x) and len(x) < 400 and x not in seen:
            seen.add(x); f.write(x.replace("\n", "\\n") + "\n")

print("done")
