using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace OrbModding.GameContractTests;

internal sealed class GameAssemblyMetadata : IDisposable
{
    private readonly FileStream _stream;
    private readonly PEReader _peReader;
    private readonly MetadataTypeNameProvider _typeProvider;

    public GameAssemblyMetadata(string path)
    {
        _stream = File.OpenRead(path);
        _peReader = new PEReader(_stream);
        Reader = _peReader.GetMetadataReader();
        _typeProvider = new MetadataTypeNameProvider(Reader);
    }

    public MetadataReader Reader { get; }

    public bool HasType(string fullName)
    {
        return TryGetType(fullName, out _);
    }

    public string GetBaseType(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        return DecodeTypeHandle(definition.BaseType);
    }

    public TypeContract GetType(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        return new TypeContract(
            fullName,
            GetTypeVisibility(definition.Attributes),
            DecodeTypeHandle(definition.BaseType));
    }

    public IReadOnlyList<string> GetTypesImplementing(string interfaceName)
    {
        var result = new List<string>();
        foreach (var handle in Reader.TypeDefinitions)
        {
            var definition = Reader.GetTypeDefinition(handle);
            foreach (var implementationHandle in definition.GetInterfaceImplementations())
            {
                var implementation = Reader.GetInterfaceImplementation(implementationHandle);
                if (!string.Equals(DecodeTypeHandle(implementation.Interface), interfaceName,
                        StringComparison.Ordinal)) continue;
                result.Add(GetFullTypeName(handle));
            }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public bool ImplementsInterface(string fullName, string interfaceName)
    {
        var direct = new HashSet<string>(
            GetTypesImplementing(interfaceName),
            StringComparer.Ordinal);
        var current = fullName;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (HasType(current) && visited.Add(current))
        {
            if (direct.Contains(current)) return true;
            current = GetBaseType(current);
        }
        return false;
    }

    public string GetFieldType(string fullName, string fieldName)
    {
        return GetField(fullName, fieldName).FieldType;
    }

    public IReadOnlyDictionary<string, int> GetInt32EnumMembers(string fullName)
    {
        if (GetBaseType(fullName) != "System.Enum" || GetFieldType(fullName, "value__") != "System.Int32")
            throw new InvalidOperationException($"Type {fullName} is not an Int32-backed enum.");
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var members = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil) continue;
            var constant = Reader.GetConstant(constantHandle);
            if (constant.TypeCode != ConstantTypeCode.Int32)
                throw new InvalidOperationException($"Enum {fullName} does not use Int32 constants.");
            members.Add(Reader.GetString(field.Name), Reader.GetBlobReader(constant.Value).ReadInt32());
        }
        return members;
    }

    /// <summary>
    /// Every field a type declares itself, in metadata order. Inherited fields are not included,
    /// which matches how a collector enumerates a category: what the type carries, not what its base
    /// contributes.
    /// </summary>
    public IReadOnlyList<FieldContract> GetFields(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var fields = new List<FieldContract>();
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            fields.Add(new FieldContract(
                Reader.GetString(field.Name),
                GetFieldVisibility(field.Attributes),
                (field.Attributes & FieldAttributes.Static) != 0,
                field.DecodeSignature(_typeProvider, null)));
        }

        return fields;
    }

    public FieldContract GetField(string fullName, string fieldName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            if (Reader.GetString(field.Name) == fieldName)
            {
                return new FieldContract(
                    fieldName,
                    GetFieldVisibility(field.Attributes),
                    (field.Attributes & FieldAttributes.Static) != 0,
                    field.DecodeSignature(_typeProvider, null));
            }
        }

        throw new InvalidOperationException($"Field {fullName}.{fieldName} was not found.");
    }

    public IReadOnlyList<MethodContract> GetMethods(string fullName, string methodName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var methods = new List<MethodContract>();
        foreach (var methodHandle in definition.GetMethods())
        {
            var method = Reader.GetMethodDefinition(methodHandle);
            var name = Reader.GetString(method.Name);
            if (name != methodName)
            {
                continue;
            }

            var signature = method.DecodeSignature(_typeProvider, null);
            methods.Add(new MethodContract(
                name,
                GetMethodVisibility(method.Attributes),
                (method.Attributes & MethodAttributes.Static) != 0,
                signature.ReturnType,
                signature.ParameterTypes.ToArray()));
        }

        return methods;
    }

    public IReadOnlyList<MethodContract> GetMethods(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var methods = new List<MethodContract>();
        foreach (var methodHandle in definition.GetMethods())
        {
            var method = Reader.GetMethodDefinition(methodHandle);
            var signature = method.DecodeSignature(_typeProvider, null);
            methods.Add(new MethodContract(
                Reader.GetString(method.Name),
                GetMethodVisibility(method.Attributes),
                (method.Attributes & MethodAttributes.Static) != 0,
                signature.ReturnType,
                signature.ParameterTypes.ToArray()));
        }

        return methods;
    }

    /// <summary>
    /// Whether one uniquely named method body contains the exact metadata token for a target field.
    /// This pins which native value an evaluator consumes, not merely that both members exist.
    /// </summary>
    public bool MethodReferencesField(
        string sourceType,
        string sourceMethod,
        string targetType,
        string targetField)
    {
        var method = RequireUniqueMethod(sourceType, sourceMethod);
        var field = RequireField(targetType, targetField);
        return MethodBodyContainsToken(method, MetadataTokens.GetToken(field));
    }

    public bool MethodReferencesMethod(
        string sourceType,
        string sourceMethod,
        string targetType,
        string targetMethod) =>
        MethodReferenceOffset(sourceType, sourceMethod, targetType, targetMethod) >= 0;

    public int MethodReferenceOffset(
        string sourceType,
        string sourceMethod,
        string targetType,
        string targetMethod)
    {
        var source = RequireUniqueMethod(sourceType, sourceMethod);
        var target = RequireUniqueMethod(targetType, targetMethod);
        return MethodBodyTokenOffset(source, MetadataTokens.GetToken(target));
    }

    public int GetMethodToken(string fullName, string methodName) =>
        MetadataTokens.GetToken(RequireUniqueMethod(fullName, methodName));

    public int GetMethodToken(string fullName, string methodName, params string[] parameterTypes) =>
        MetadataTokens.GetToken(RequireMethod(fullName, methodName, parameterTypes));

    public int GetFieldToken(string fullName, string fieldName) =>
        MetadataTokens.GetToken(RequireField(fullName, fieldName));

    public byte[] GetMethodBodyBytes(string fullName, string methodName)
    {
        var method = Reader.GetMethodDefinition(RequireUniqueMethod(fullName, methodName));
        return _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
    }

    public MethodDispatchContract GetMethodDispatch(string fullName, string methodName)
    {
        var method = Reader.GetMethodDefinition(RequireUniqueMethod(fullName, methodName));
        return new MethodDispatchContract(
            (method.Attributes & MethodAttributes.Virtual) != 0,
            (method.Attributes & MethodAttributes.Abstract) != 0,
            (method.Attributes & MethodAttributes.NewSlot) != 0);
    }

    /// <summary>The one-byte IL opcode immediately preceding an exact definition-token operand.</summary>
    public byte GetMethodReferenceOpcode(
        string sourceType,
        string sourceMethod,
        string targetType,
        string targetMethod)
    {
        var source = RequireUniqueMethod(sourceType, sourceMethod);
        var target = RequireUniqueMethod(targetType, targetMethod);
        var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(target));
        if (offset <= 0)
            throw new InvalidOperationException(
                $"Method {sourceType}.{sourceMethod} did not reference " +
                $"{targetType}.{targetMethod} with a one-byte opcode.");
        var sourceDefinition = Reader.GetMethodDefinition(source);
        var bytes = _peReader.GetMethodBody(sourceDefinition.RelativeVirtualAddress).GetILBytes();
        return bytes![offset - 1];
    }

    /// <summary>
    /// Definition tokens referenced by one uniquely named native method body. This deliberately
    /// reports only methods and fields defined by the inspected game assembly; it is intended for
    /// pinning the game's own state-machine edges rather than producing a general IL disassembly.
    /// </summary>
    public IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyDefinitionReferences(
        string sourceType,
        string sourceMethod)
    {
        var source = RequireUniqueMethod(sourceType, sourceMethod);
        var references = new List<MethodBodyDefinitionReference>();
        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var type = Reader.GetTypeDefinition(typeHandle);
            var typeName = GetFullTypeName(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(methodHandle));
                if (offset < 0) continue;
                var method = Reader.GetMethodDefinition(methodHandle);
                references.Add(new MethodBodyDefinitionReference(
                    offset,
                    MetadataTokens.GetToken(methodHandle),
                    "method",
                    typeName,
                    Reader.GetString(method.Name)));
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(fieldHandle));
                if (offset < 0) continue;
                var field = Reader.GetFieldDefinition(fieldHandle);
                references.Add(new MethodBodyDefinitionReference(
                    offset,
                    MetadataTokens.GetToken(fieldHandle),
                    "field",
                    typeName,
                    Reader.GetString(field.Name)));
            }
        }
        return references.OrderBy(reference => reference.Offset).ToArray();
    }

    public IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyDefinitionReferences(
        string sourceType,
        string sourceMethod,
        params string[] parameterTypes) =>
        GetMethodBodyDefinitionReferences(RequireMethod(sourceType, sourceMethod, parameterTypes));

    /// <summary>
    /// Constructed-generic and external member references used by one uniquely named native
    /// method body. Definition-reference inspection alone cannot see a call through a closed
    /// generic base such as GenericListVariable&lt;GlyphSO&gt;.Empty().
    /// </summary>
    public IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyMemberReferences(
        string sourceType,
        string sourceMethod)
    {
        var source = RequireUniqueMethod(sourceType, sourceMethod);
        var references = new List<MethodBodyDefinitionReference>();
        foreach (var handle in Reader.MemberReferences)
        {
            var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(handle));
            if (offset < 0) continue;
            var member = Reader.GetMemberReference(handle);
            references.Add(new MethodBodyDefinitionReference(
                offset,
                MetadataTokens.GetToken(handle),
                "member-reference",
                DecodeTypeHandle(member.Parent),
                Reader.GetString(member.Name)));
        }
        return references.OrderBy(reference => reference.Offset).ToArray();
    }

    public IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyMemberReferences(
        string sourceType,
        string sourceMethod,
        params string[] parameterTypes) =>
        GetMethodBodyMemberReferences(RequireMethod(sourceType, sourceMethod, parameterTypes));

    private IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyDefinitionReferences(
        MethodDefinitionHandle source)
    {
        var references = new List<MethodBodyDefinitionReference>();
        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var type = Reader.GetTypeDefinition(typeHandle);
            var typeName = GetFullTypeName(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(methodHandle));
                if (offset < 0) continue;
                var method = Reader.GetMethodDefinition(methodHandle);
                references.Add(new MethodBodyDefinitionReference(offset, MetadataTokens.GetToken(methodHandle),
                    "method", typeName, Reader.GetString(method.Name)));
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(fieldHandle));
                if (offset < 0) continue;
                var field = Reader.GetFieldDefinition(fieldHandle);
                references.Add(new MethodBodyDefinitionReference(offset, MetadataTokens.GetToken(fieldHandle),
                    "field", typeName, Reader.GetString(field.Name)));
            }
        }
        return references.OrderBy(reference => reference.Offset).ToArray();
    }

    private IReadOnlyList<MethodBodyDefinitionReference> GetMethodBodyMemberReferences(
        MethodDefinitionHandle source)
    {
        var references = new List<MethodBodyDefinitionReference>();
        foreach (var handle in Reader.MemberReferences)
        {
            var offset = MethodBodyTokenOffset(source, MetadataTokens.GetToken(handle));
            if (offset < 0) continue;
            var member = Reader.GetMemberReference(handle);
            references.Add(new MethodBodyDefinitionReference(offset, MetadataTokens.GetToken(handle),
                "member-reference", DecodeTypeHandle(member.Parent), Reader.GetString(member.Name)));
        }
        return references.OrderBy(reference => reference.Offset).ToArray();
    }

    /// <summary>
    /// Every instruction in the assembly that touches one of the named fields, with the opcode class
    /// that touched it and the member the value was handed straight to.
    /// </summary>
    /// <remarks>
    /// Whole-assembly rather than per-method because the question this answers is the inverse of
    /// <see cref="GetMethodBodyDefinitionReferences(string, string)"/>: not "what does this method
    /// read" but "who reads this field, if anyone". The whole set is scanned in one walk because a
    /// walk per field would re-read every method body once per field.
    /// </remarks>
    public IReadOnlyList<FieldUseSite> GetFieldUseSites(
        IReadOnlyCollection<(string Type, string Field)> fields)
    {
        var named = new Dictionary<int, (string Type, string Field)>();
        foreach (var field in fields)
            named[MetadataTokens.GetToken(RequireField(field.Type, field.Field))] = field;

        var sites = new List<FieldUseSite>();
        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var type = Reader.GetTypeDefinition(typeHandle);
            var typeName = GetFullTypeName(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = Reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;
                var il = _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                if (il is null) continue;
                for (var offset = 1; offset + 4 <= il.Length; offset++)
                {
                    var token = il[offset] | (il[offset + 1] << 8) |
                        (il[offset + 2] << 16) | (il[offset + 3] << 24);
                    if (!named.TryGetValue(token, out var field)) continue;
                    sites.Add(new FieldUseSite(
                        field.Type,
                        field.Field,
                        typeName,
                        Reader.GetString(method.Name),
                        MetadataTokens.GetToken(methodHandle),
                        (method.Attributes & MethodAttributes.Virtual) != 0,
                        FieldOpcodeClass(il[offset - 1]),
                        CalledMemberName(il, offset + 4)));
                }
            }
        }

        return sites;
    }

    /// <summary>
    /// Which of the given method tokens appear in some other method's body, by <c>call</c>,
    /// <c>callvirt</c> or <c>ldftn</c> alike.
    /// </summary>
    /// <remarks>
    /// A definition token in a body is the only dispatch IL states outright. It says nothing about
    /// virtual dispatch through a base slot or about a reflective call, so an empty answer for one
    /// token is evidence only when the method is neither virtual nor an entry point the runtime
    /// calls by name.
    /// </remarks>
    public IReadOnlySet<int> GetReferencedMethodTokens(IReadOnlyCollection<int> candidates)
    {
        var wanted = new HashSet<int>(candidates);
        var referenced = new HashSet<int>();
        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var type = Reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = Reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;
                var own = MetadataTokens.GetToken(methodHandle);
                var il = _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                if (il is null) continue;
                for (var offset = 0; offset + 4 <= il.Length; offset++)
                {
                    var token = il[offset] | (il[offset + 1] << 8) |
                        (il[offset + 2] << 16) | (il[offset + 3] << 24);
                    if (token != own && wanted.Contains(token)) referenced.Add(token);
                }
            }
        }

        return referenced;
    }

    private static string FieldOpcodeClass(byte opcode) => opcode switch
    {
        0x7B or 0x7C or 0x7E or 0x7F => "load",
        0x7D or 0x80 => "store",
        _ => "other",
    };

    /// <summary>The member a <c>call</c> or <c>callvirt</c> at this offset dispatches to.</summary>
    private string CalledMemberName(byte[] il, int offset)
    {
        if (offset + 5 > il.Length) return string.Empty;
        if (il[offset] != 0x28 && il[offset] != 0x6F) return string.Empty;
        var token = il[offset + 1] | (il[offset + 2] << 8) |
            (il[offset + 3] << 16) | (il[offset + 4] << 24);
        var handle = MetadataTokens.EntityHandle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => Reader.GetString(
                Reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.MemberReference => Reader.GetString(
                Reader.GetMemberReference((MemberReferenceHandle)handle).Name),
            _ => string.Empty,
        };
    }

    private static string GetTypeVisibility(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
            TypeAttributes.NestedFamily => "family",
            TypeAttributes.NestedAssembly => "assembly",
            TypeAttributes.NestedFamORAssem => "family-or-assembly",
            TypeAttributes.NestedFamANDAssem => "family-and-assembly",
            _ => "private",
        };

    private static string GetMethodVisibility(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family => "family",
            MethodAttributes.Assembly => "assembly",
            MethodAttributes.FamORAssem => "family-or-assembly",
            MethodAttributes.FamANDAssem => "family-and-assembly",
            _ => "private",
        };

    private static string GetFieldVisibility(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => "public",
            FieldAttributes.Family => "family",
            FieldAttributes.Assembly => "assembly",
            FieldAttributes.FamORAssem => "family-or-assembly",
            FieldAttributes.FamANDAssem => "family-and-assembly",
            _ => "private",
        };

    public void Dispose()
    {
        _peReader.Dispose();
        _stream.Dispose();
    }

    private TypeDefinitionHandle RequireType(string fullName)
    {
        if (TryGetType(fullName, out var handle))
        {
            return handle;
        }

        throw new InvalidOperationException($"Type {fullName} was not found.");
    }

    private FieldDefinitionHandle RequireField(string fullName, string fieldName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        foreach (var handle in definition.GetFields())
        {
            if (Reader.GetString(Reader.GetFieldDefinition(handle).Name) == fieldName)
                return handle;
        }
        throw new InvalidOperationException($"Field {fullName}.{fieldName} was not found.");
    }

    private MethodDefinitionHandle RequireUniqueMethod(string fullName, string methodName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        MethodDefinitionHandle found = default;
        foreach (var handle in definition.GetMethods())
        {
            if (Reader.GetString(Reader.GetMethodDefinition(handle).Name) != methodName) continue;
            if (!found.IsNil)
                throw new InvalidOperationException(
                    $"Method {fullName}.{methodName} is overloaded; an exact selector is required.");
            found = handle;
        }
        if (found.IsNil)
            throw new InvalidOperationException($"Method {fullName}.{methodName} was not found.");
        return found;
    }

    private MethodDefinitionHandle RequireMethod(
        string fullName,
        string methodName,
        IReadOnlyList<string> parameterTypes)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        MethodDefinitionHandle found = default;
        foreach (var handle in definition.GetMethods())
        {
            var method = Reader.GetMethodDefinition(handle);
            if (Reader.GetString(method.Name) != methodName) continue;
            var signature = method.DecodeSignature(_typeProvider, null);
            if (!signature.ParameterTypes.SequenceEqual(parameterTypes, StringComparer.Ordinal)) continue;
            if (!found.IsNil)
                throw new InvalidOperationException(
                    $"Method {fullName}.{methodName}({string.Join(", ", parameterTypes)}) is ambiguous.");
            found = handle;
        }
        if (found.IsNil)
            throw new InvalidOperationException(
                $"Method {fullName}.{methodName}({string.Join(", ", parameterTypes)}) was not found.");
        return found;
    }

    private bool MethodBodyContainsToken(MethodDefinitionHandle methodHandle, int token)
        => MethodBodyTokenOffset(methodHandle, token) >= 0;

    private int MethodBodyTokenOffset(MethodDefinitionHandle methodHandle, int token)
    {
        var method = Reader.GetMethodDefinition(methodHandle);
        if (method.RelativeVirtualAddress == 0) return -1;
        var bytes = _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        if (bytes is null || bytes.Length < sizeof(int)) return -1;
        for (var offset = 0; offset <= bytes.Length - sizeof(int); offset++)
        {
            if (bytes[offset] == (byte)token &&
                bytes[offset + 1] == (byte)(token >> 8) &&
                bytes[offset + 2] == (byte)(token >> 16) &&
                bytes[offset + 3] == (byte)(token >> 24))
                return offset;
        }
        return -1;
    }

    private bool TryGetType(string fullName, out TypeDefinitionHandle handle)
    {
        foreach (var candidate in Reader.TypeDefinitions)
        {
            if (GetFullTypeName(candidate) == fullName)
            {
                handle = candidate;
                return true;
            }
        }

        handle = default;
        return false;
    }

    private string DecodeTypeHandle(EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return string.Empty;
        }

        return handle.Kind switch
        {
            HandleKind.TypeDefinition => _typeProvider.GetTypeFromDefinition(Reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => _typeProvider.GetTypeFromReference(Reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => _typeProvider.GetTypeFromSpecification(Reader, null, (TypeSpecificationHandle)handle, 0),
            _ => handle.Kind.ToString(),
        };
    }

    private string GetFullTypeName(TypeDefinitionHandle handle)
    {
        var definition = Reader.GetTypeDefinition(handle);
        var name = Reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return GetFullTypeName(declaring) + "+" + name;
        }

        var typeNamespace = Reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }
}

internal sealed record MethodContract(
    string Name,
    string Visibility,
    bool IsStatic,
    string ReturnType,
    IReadOnlyList<string> ParameterTypes);

internal sealed record FieldContract(
    string Name,
    string Visibility,
    bool IsStatic,
    string FieldType);

internal sealed record TypeContract(
    string Name,
    string Visibility,
    string BaseType);

internal sealed record MethodBodyDefinitionReference(
    int Offset,
    int Token,
    string Kind,
    string DeclaringType,
    string MemberName);

/// <summary>One instruction that touches a named field, and what it does with it.</summary>
internal sealed record FieldUseSite(
    string FieldOwner,
    string FieldName,
    string MethodOwner,
    string MethodName,
    int MethodToken,
    bool MethodIsVirtual,
    string Use,
    string CalledMember);

internal sealed record MethodDispatchContract(
    bool IsVirtual,
    bool IsAbstract,
    bool IsNewSlot);

internal sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;

    public MetadataTypeNameProvider(MetadataReader reader)
    {
        _reader = reader;
    }

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        _ => typeCode.ToString(),
    };

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + name;
        }

        var typeNamespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "+" + name;
        }

        var typeNamespace = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
