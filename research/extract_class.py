# -*- coding: utf-8 -*-
"""从 dump.cs 抽取指定类/枚举的完整定义（按花括号配对）。
用法: python extract_class.py <dump.cs> <ClassName> [更多类名...]"""
import sys, re, io

def extract(path, names):
    lines = open(path, encoding='utf-8').read().splitlines()
    # 找所有类型起始行
    starts = []
    pat = re.compile(r'^(?:public |internal |private |protected )?(?:sealed |abstract |static |partial )*'
                     r'(?:class|struct|enum|interface) ([A-Za-z_][\w`\.<>, ]*)')
    for i, ln in enumerate(lines):
        m = pat.match(ln)
        if m:
            starts.append((i, m.group(1).split('<')[0].strip()))
    out = io.StringIO()
    for name in names:
        hits = [i for i, n in starts if n == name]
        if not hits:
            # 放宽：包含匹配
            hits = [i for i, n in starts if name.lower() in n.lower()]
        if not hits:
            out.write(f"\n########## {name}: NOT FOUND ##########\n"); continue
        for h in hits:
            # 花括号配对，从起始行往后
            depth = 0; started = False; buf = []
            for j in range(h, min(h + 40000, len(lines))):
                ln = lines[j]
                buf.append(ln)
                depth += ln.count('{') - ln.count('}')
                if '{' in ln: started = True
                if started and depth <= 0:
                    break
            out.write(f"\n########## {name} @line {h+1} ##########\n")
            out.write("\n".join(buf) + "\n")
    return out.getvalue()

if __name__ == '__main__':
    txt = extract(sys.argv[1], sys.argv[2:])
    sys.stdout.reconfigure(encoding='utf-8')
    print(txt)
