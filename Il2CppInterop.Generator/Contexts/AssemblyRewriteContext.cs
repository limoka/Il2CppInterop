using System.Diagnostics;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;

namespace Il2CppInterop.Generator.Contexts;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
public class AssemblyRewriteContext
{
    public readonly RewriteGlobalContext GlobalContext;

    public readonly RuntimeAssemblyReferences Imports;
    private readonly Dictionary<string, TypeRewriteContext> myNameTypeMap = new();
    private readonly Dictionary<TypeDefinition, TypeRewriteContext> myNewTypeMap = new();

    private readonly Dictionary<TypeDefinition, TypeRewriteContext> myOldTypeMap = new();
    public readonly AssemblyDefinition NewAssembly;

    public readonly AssemblyDefinition OriginalAssembly;

    public AssemblyRewriteContext(RewriteGlobalContext globalContext, AssemblyDefinition originalAssembly,
        AssemblyDefinition newAssembly)
    {
        OriginalAssembly = originalAssembly;
        NewAssembly = newAssembly;
        GlobalContext = globalContext;

        Imports = globalContext.ImportsMap.GetOrCreate(newAssembly.ManifestModule!,
            mod => new RuntimeAssemblyReferences(mod, globalContext));
    }

    public IEnumerable<TypeRewriteContext> Types => myNewTypeMap.Values;

    public TypeRewriteContext GetContextForOriginalType(TypeDefinition type)
    {
        return myOldTypeMap[type];
    }

    public TypeRewriteContext? TryGetContextForOriginalType(TypeDefinition type)
    {
        return myOldTypeMap.TryGetValue(type, out var result) ? result : null;
    }

    public TypeRewriteContext GetContextForNewType(TypeDefinition type)
    {
        return myNewTypeMap[type];
    }

    public void RegisterTypeRewrite(TypeRewriteContext context)
    {
        var exists = context.OriginalType != null && myOldTypeMap.ContainsKey(context.OriginalType);
        if (context.OriginalType != null && !exists)
            myOldTypeMap[context.OriginalType] = context;
        myNewTypeMap[context.NewType] = context;
        if (!exists)
            myNameTypeMap[(context.OriginalType ?? context.NewType).FullName] = context;
    }

    public IMethodDefOrRef RewriteMethodRef(IMethodDefOrRef methodRef)
    {
        var newType = GlobalContext.GetNewTypeForOriginal(methodRef.DeclaringType!.Resolve()!);
        var newMethod = newType.GetMethodByOldMethod(methodRef.Resolve()!).NewMethod;
        return NewAssembly.ManifestModule!.DefaultImporter.ImportMethod(newMethod);
    }

    public TypeReference RewriteTypeRef(ITypeDescriptor typeRef, bool typeIsBoxed)
    {
        return RewriteTypeRef(typeRef?.ToTypeSignature()).ToTypeDefOrRef();
    }

    public TypeSignature RewriteTypeRef(TypeSignature? typeRef)
    {
        if (typeRef == null)
            return Imports.Il2CppObjectBase;

        var sourceModule = NewAssembly.ManifestModule!;

        if (typeRef is ArrayBaseTypeSignature arrayType)
        {
            if (arrayType.Rank != 1)
                return Imports.Il2CppObjectBase;

            var elementType = arrayType.BaseType;
            if (elementType.FullName == "System.String")
                return Imports.Il2CppStringArray;

            var convertedElementType = RewriteTypeRef(elementType, typeIsBoxed);
            if (elementType is GenericParameterSignature)
                return new GenericInstanceTypeSignature(Imports.Il2CppArrayBase.ToTypeDefOrRef(), false, convertedElementType);

            return new GenericInstanceTypeSignature(convertedElementType.IsValueType
                    ? Imports.Il2CppStructArray.ToTypeDefOrRef()
                    : Imports.Il2CppReferenceArray.ToTypeDefOrRef(), false, convertedElementType);
        }

        if (typeRef is GenericParameterSignature genericParameter)
        {
            var genericParameterDeclaringType = genericParameter.DeclaringType;
            if (genericParameterDeclaringType != null)
                return RewriteTypeRef(genericParameterDeclaringType, typeIsBoxed).GenericParameters[genericParameter.Position];

            return RewriteMethodRef(genericParameter.DeclaringMethod).GenericParameters[genericParameter.Position];
        }

        if (typeRef is ByReferenceTypeSignature byRef)
            return new ByReferenceTypeSignature(RewriteTypeRef(byRef.BaseType, typeIsBoxed));

        if (typeRef is PointerTypeSignature pointerType)
            return new PointerTypeSignature(RewriteTypeRef(pointerType.BaseType, typeIsBoxed));

        if (typeRef is GenericInstanceTypeSignature genericInstance)
        {
            var genericTypeContext = GetTypeContext(genericInstance);
            if (genericTypeContext.ComputedTypeSpecifics == TypeRewriteContext.TypeSpecifics.GenericBlittableStruct && !IsUnmanaged(typeRef, typeIsBoxed))
            {
                var type = sourceModule.ImportReference(genericTypeContext.BoxedTypeContext.NewType);
                var newRef = new GenericInstanceTypeSignature(type, type.IsValueType);
                foreach (var originalParameter in genericInstance.TypeArguments)
                    newRef.TypeArguments.Add(RewriteTypeRef(originalParameter, typeIsBoxed));

                return newRef;
            }
            else
            {
                var genericType = RewriteTypeRef(genericInstance.GenericType.ToTypeSignature()).ToTypeDefOrRef();
                var newRef = new GenericInstanceTypeSignature(genericType, genericType.IsValueType);
                foreach (var originalParameter in genericInstance.TypeArguments)
                    newRef.TypeArguments.Add(RewriteTypeRef(originalParameter, typeIsBoxed));

                return newRef;
            }
        }

        if (typeRef.IsPrimitive() || typeRef.FullName == "System.TypedReference")
            return sourceModule.ImportCorlibReference(typeRef.FullName);

        if (typeRef.FullName == "System.Void")
            return Imports.Module.Void();

        if (typeRef.FullName == "System.String")
            return Imports.Module.String();

        if (typeRef.FullName == "System.Object")
            return sourceModule.DefaultImporter.ImportType(GlobalContext.GetAssemblyByName("mscorlib")
                .GetTypeByName("System.Object").NewType).ToTypeSignature();

        if (typeRef.FullName == "System.Attribute")
            return sourceModule.DefaultImporter.ImportType(GlobalContext.GetAssemblyByName("mscorlib")
                .GetTypeByName("System.Attribute").NewType).ToTypeSignature();

        var target = GetTypeContext(typeRef);

        if (typeIsBoxed && target.BoxedTypeContext != null)
        {
            target = target.BoxedTypeContext;
        }

        return sourceModule.DefaultImporter.ImportType(target.NewType).ToTypeSignature();
    }

    private TypeRewriteContext GetTypeContext(TypeReference typeRef)
    {
        var typeDef = typeRef.Resolve()!;
        var targetAssembly = GlobalContext.GetNewAssemblyForOriginal(typeDef.Module!.Assembly!);
        var typeContext = targetAssembly.GetContextForOriginalType(typeDef);

        return typeContext;
    }

    private bool IsUnmanaged(TypeReference originalType, bool typeIsBoxed)
    {
        if (originalType is GenericParameter genericParameter)
        {
            GenericParameter newGenericParameter = (GenericParameter)RewriteTypeRef(genericParameter, typeIsBoxed);
            return newGenericParameter.CustomAttributes.Any(attribute => attribute.AttributeType.Name.Equals("IsUnmanagedAttribute"));
        }

        if (originalType is GenericInstanceType genericInstanceType)
        {
            foreach (TypeReference genericArgument in genericInstanceType.GenericArguments)
            {
                if (!IsUnmanaged(genericArgument, typeIsBoxed))
                    return false;
            }
        }

        var paramTypeContext = GetTypeContext(originalType);
        return paramTypeContext.ComputedTypeSpecifics.IsBlittable();
    }

    public TypeRewriteContext GetTypeByName(string name)
    {
        return myNameTypeMap[name];
    }

    public TypeRewriteContext? TryGetTypeByName(string name)
    {
        return myNameTypeMap.TryGetValue(name, out var result) ? result : null;
    }

    private string GetDebuggerDisplay()
    {
        return NewAssembly.FullName;
    }
}
