using System;
using EasyIDMMA.Enums;

namespace EasyIDMMA.Extensions
{
    public static class IDMObjectTypeExtensions
    {
        public static bool Is(this string objectType, IDMObjectType idmObjectType)
        {
            return objectType.Equals(idmObjectType.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}