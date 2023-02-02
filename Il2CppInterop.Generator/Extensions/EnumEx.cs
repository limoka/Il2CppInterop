using Il2CppInterop.Generator.Contexts;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Il2CppInterop.Generator.Extensions;

public static class EnumEx
{
    public static FieldAttributes ForcePublic(this FieldAttributes fieldAttributes)
    {
        return (fieldAttributes & ~FieldAttributes.FieldAccessMask & ~FieldAttributes.HasFieldMarshal) |
               FieldAttributes.Public;
    }

    public static GenericParameterAttributes StripValueTypeConstraint(
        this GenericParameterAttributes parameterAttributes)
    {
        return parameterAttributes & ~(GenericParameterAttributes.NotNullableValueTypeConstraint |
                                       GenericParameterAttributes.VarianceMask);
    }

    public static bool IsBlittable(this TypeRewriteContext.TypeSpecifics typeSpecifics)
    {
        return typeSpecifics == TypeRewriteContext.TypeSpecifics.BlittableStruct ||
               typeSpecifics == TypeRewriteContext.TypeSpecifics.GenericBlittableStruct;
    }
}
