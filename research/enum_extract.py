# -*- coding: utf-8 -*-
"""从 IL2CPP 元数据提取枚举成员值 —— 还原 PlantType/ZombieType 等 ID 表。

结构 (metadata v31):
  Il2CppTypeDefinition      88 B  : fieldStart@+32, field_count@+68(uint16), flags@+28, nameIndex@+0, namespaceIndex@+4
  Il2CppFieldDefinition     12 B  : nameIndex, typeIndex, token
  Il2CppFieldDefaultValue   12 B  : fieldIndex, typeIndex, dataIndex
  Il2CppType                 : 未直接使用，改用默认值数据宽度表
  data blob                       : fieldAndParameterDefaultValueData
"""
import struct, os, sys, collections

META = r"PlantsVsZombiesRH_Data\il2cpp_data\Metadata\global-metadata.dat"
OUT = r"_re_analysis"
d = open(META, "rb").read()

H = lambda i: struct.unpack_from("<I", d, 8 + i*4)[0]
# 下标 = 头部字段序号(去掉 sanity/version), 见 01_assemblies 生成脚本 HDR 顺序
STR_OFF, STR_SIZE = H(4), H(5)         # stringOffset / stringSize
FDV_OFF, FDV_SIZE = H(14), H(15)       # fieldDefaultOffset / fieldDefaultSize
DATA_OFF = H(16)                       # fieldAndParameterDefaultValueDataOffset
FIELDS_OFF, FIELDS_SIZE = H(22), H(23) # fieldsOffset / fieldsSize
TD_OFF, TD_SIZE = H(38), H(39)         # typeDefinitionsOffset / typeDefinitionsSize

# 索引校验：H(i) 的下标 0 == stringLiteralOffset
assert H(0) == 256, H(0)

strtab = d[STR_OFF:STR_OFF+STR_SIZE]
def s(idx):
    if idx < 0: return ""
    end = strtab.find(b"\x00", idx)
    return strtab[idx:end].decode("utf-8", "replace")

# ---- 字段表 ----
n_fields = FIELDS_SIZE // 12
field_names = []
for i in range(n_fields):
    ni, ti, tok = struct.unpack_from("<iii", d, FIELDS_OFF + i*12)
    field_names.append(s(ni))

# ---- 字段默认值 ----
n_fdv = FDV_SIZE // 12
defval = {}   # fieldIndex -> (typeIndex, dataIndex)
for i in range(n_fdv):
    fi, ti, di = struct.unpack_from("<iii", d, FDV_OFF + i*12)
    defval[fi] = (ti, di)

# ---- 类型定义 ----
TD_FMT = "<16i8H2I"
n_td = TD_SIZE // 88
types = []
for i in range(n_td):
    f = struct.unpack_from(TD_FMT, d, TD_OFF + i*88)
    types.append(dict(name=s(f[0]), ns=s(f[1]), flags=f[7], fieldStart=f[8],
                      methodStart=f[9], field_count=f[16], method_count=f[16]))

# 修正: 重新按偏移取 field_count / method_count
for i in range(n_td):
    b = TD_OFF + i*88
    types[i]["fieldStart"] = struct.unpack_from("<i", d, b+32)[0]
    types[i]["field_count"] = struct.unpack_from("<H", d, b+68)[0]
    types[i]["methodStart"] = struct.unpack_from("<i", d, b+36)[0]
    types[i]["method_count"] = struct.unpack_from("<H", d, b+64)[0]

# IL2CPP 类型码 -> 读取宽度
TYPE_SIZE = {1:1,2:1,3:1,4:2,5:2,6:1,7:1,8:4,9:4,10:8,11:8,12:4,13:8}
TYPE_NAME = {1:"void",2:"bool",3:"char",4:"i2",5:"u2",6:"i1",7:"u1",8:"i4",9:"u4",
             10:"i8",11:"u8",12:"r4",13:"r8",14:"string"}

def read_val(ti, di):
    """从默认值数据 blob 读取值 (ti = Il2CppType 索引 -> 需要 typeIndex 表)"""
    return None

# 旧版数据 blob 是直接内联值；为稳妥起见，尝试宽度=4 读取
def enum_members(tname, want_ns=None):
    res = []
    for t in types:
        if t["name"] != tname: continue
        if want_ns is not None and t["ns"] != want_ns: continue
        fs, fc = t["fieldStart"], t["field_count"]
        for k in range(fc):
            gi = fs + k
            if gi < 0 or gi >= n_fields: continue
            nm = field_names[gi]
            if nm == "value__":   # 枚举的实例字段
                continue
            if gi in defval:
                ti, di = defval[gi]
                # PlantType 等基于 int，值内联在数据 blob
                raw = d[DATA_OFF+di: DATA_OFF+di+8]
                v4 = struct.unpack_from("<i", raw, 0)[0]
                v8 = struct.unpack_from("<q", raw, 0)[0]
                res.append((nm, v4, v8, TYPE_NAME.get(ti, ti)))
            else:
                res.append((nm, None, None, None))
    return res

targets = ["PlantType", "ZombieType", "PlantSubType", "EffectType", "SceneType",
           "SynergyType", "GameStatus", "DamageMode", "GameResult", "BuffType"]
report = []
for t in targets:
    mem = enum_members(t)
    report.append(f"\n########## enum {t}  ({len(mem)} 个成员) ##########")
    for nm, v4, v8, tt in mem:
        report.append(f"  {v4 if v4 is not None else '?':>10}  {nm}   [type={tt} i8={v8}]")

# 额外：所有包含 "Type" 的枚举大小
sizes = collections.Counter()
for t in types:
    if t["field_count"] > 0 and t["name"].endswith("Type"):
        sizes[t["name"]] = t["field_count"]

# --- 输出 ---
with open(os.path.join(OUT, "07_enums.txt"), "w", encoding="utf-8") as f:
    f.write("=== 目标枚举成员值 ===\n")
    f.write("\n".join(report))
    f.write("\n\n=== 所有 *Type 枚举的成员数 ===\n")
    for nm, c in sizes.most_common(60):
        f.write(f"{c:>6}  {nm}\n")

# PlantType 单独导出为 ID->名称 表
for tname in ("PlantType", "ZombieType"):
    mem = enum_members(tname)
    with open(os.path.join(OUT, f"08_{tname}_ids.txt"), "w", encoding="utf-8") as f:
        f.write(f"# {tname}: {len(mem)} 个成员, 按 ID 排序\n")
        rows = sorted([m for m in mem if m[1] is not None], key=lambda x: x[1])
        for nm, v4, v8, tt in rows:
            f.write(f"{v4}\t{nm}\n")

print("field definitions:", n_fields)
print("field default values:", n_fdv)
print("type definitions:", n_td)
for t in targets:
    print(f"  enum {t}: {len(enum_members(t))} members")
print("done")
