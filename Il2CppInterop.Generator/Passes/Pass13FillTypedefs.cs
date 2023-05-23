using System.Diagnostics.CodeAnalysis;
using Il2CppInterop.Common;
using AsmResolver.DotNet;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.Extensions;
using Il2CppInterop.Generator.Utils;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Generator.Passes;

public static class Pass13FillTypedefs
{
    public static void DoPass(RewriteGlobalContext context)
    {
        foreach (var assemblyContext in context.Assemblies)
        {
            foreach (var typeContext in assemblyContext.Types)
            {
                foreach (var originalParameter in typeContext.OriginalType.GenericParameters)
                {
                    var newParameter = new GenericParameter(originalParameter.Name.MakeValidInSource(),
                        originalParameter.Attributes.StripValueTypeConstraint());
                    typeContext.NewType.GenericParameters.Add(newParameter);

                    //TODO ensure works
                    if (!typeContext.ComputedTypeSpecifics.IsBlittable())
                        newParameter.Attributes = originalParameter.Attributes.StripValueTypeConstraint();
                    else
                    {
                        newParameter.Attributes = originalParameter.Attributes;
                        newParameter.MakeUnmanaged(assemblyContext);
                    }
                }

                if (typeContext.OriginalType.IsEnum)
                    typeContext.NewType.BaseType = assemblyContext.Imports.Module.Enum().ToTypeDefOrRef();
                else if (typeContext.ComputedTypeSpecifics.IsBlittable())
                    typeContext.NewType.BaseType = assemblyContext.Imports.Module.ValueType().ToTypeDefOrRef();
            }
        }

        // Second pass is explicitly done after first to account for rewriting of generic base types - value-typeness is important there
        foreach (var assemblyContext in context.Assemblies)
            foreach (var typeContext in assemblyContext.Types)
                if (!typeContext.OriginalType.IsEnum && !typeContext.ComputedTypeSpecifics.IsBlittable())
                    typeContext.NewType.BaseType = assemblyContext.RewriteTypeRef(typeContext.OriginalType.BaseType, false);
    }
}
