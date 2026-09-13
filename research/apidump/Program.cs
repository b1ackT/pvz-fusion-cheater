// Metadata-only API dumper: reads an Il2CppInterop-generated assembly and prints the
// member shapes we need for Harmony patching (no assembly load => safe outside the game).
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: apidump <assembly.dll> <TypeName> [TypeName...]");
    return 1;
}

string path = args[0];
var wanted = new HashSet<string>(args.Skip(1), StringComparer.Ordinal);

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var provider = new TypeNameProvider(md);

static string FullName(MetadataReader md, TypeDefinition td)
{
    string ns = md.GetString(td.Namespace);
    string nm = md.GetString(td.Name);
    return string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
}

var types = md.TypeDefinitions
    .Select(h => md.GetTypeDefinition(h))
    .Where(td => wanted.Contains(FullName(md, td)) || wanted.Contains(md.GetString(td.Name)))
    .ToList();

Console.WriteLine($"# assembly: {Path.GetFileName(path)}   matched {types.Count} type(s)");
foreach (var td in types)
{
    Console.WriteLine();
    Console.WriteLine($"===== {FullName(md, td)}  [attrs: {td.Attributes}] =====");

    // fields
    foreach (var fh in td.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        bool isStatic = (f.Attributes & FieldAttributes.Static) != 0;
        string tn = f.DecodeSignature(provider, null);
        Console.WriteLine($"  F {(isStatic ? "static " : "")}{tn} {md.GetString(f.Name)}");
    }

    // properties (interop turns il2cpp fields into properties)
    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        var acc = p.GetAccessors();
        bool isStatic = false;
        if (!acc.Getter.IsNil) isStatic = (md.GetMethodDefinition(acc.Getter).Attributes & MethodAttributes.Static) != 0;
        else if (!acc.Setter.IsNil) isStatic = (md.GetMethodDefinition(acc.Setter).Attributes & MethodAttributes.Static) != 0;
        string tn = p.DecodeSignature(provider, null).ReturnType;
        string kind = (!acc.Getter.IsNil && !acc.Setter.IsNil) ? "get/set" : (!acc.Getter.IsNil ? "get" : "set");
        Console.WriteLine($"  P {(isStatic ? "static " : "")}{tn} {md.GetString(p.Name)}  ({kind})");
    }

    // methods
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string name = md.GetString(m.Name);
        if (name.StartsWith("get_") || name.StartsWith("set_")) continue;    // shown via properties
        if (name is ".ctor" or ".cctor") continue;
        bool isStatic = (m.Attributes & MethodAttributes.Static) != 0;
        var sig = m.DecodeSignature(provider, null);
        string ps = string.Join(", ", sig.ParameterTypes);
        Console.WriteLine($"  M {(isStatic ? "static " : "")}{sig.ReturnType} {name}({ps})");
    }
}
return 0;

sealed class TypeNameProvider : ISignatureTypeProvider<string, object>
{
    private readonly MetadataReader _md;
    public TypeNameProvider(MetadataReader md) => _md = md;

    private string NameOf(TypeDefinitionHandle h)
    {
        var td = _md.GetTypeDefinition(h);
        string ns = _md.GetString(td.Namespace), nm = _md.GetString(td.Name);
        return string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
    }

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.IntPtr => "IntPtr",
        PrimitiveTypeCode.UIntPtr => "UIntPtr",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString()
    };
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => NameOf(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        string ns = reader.GetString(tr.Namespace), nm = reader.GetString(tr.Name);
        return string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
    }
    public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
