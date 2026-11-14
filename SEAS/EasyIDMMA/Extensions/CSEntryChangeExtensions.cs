using System;
using System.Linq;
using Microsoft.MetadirectoryServices;

namespace EasyIDMMA.Extensions
{
    public static class CSEntryChangeExtensions
    {
        public static object GetValue(this CSEntryChange csEntry, string attributeName)
        {
            var anchorAttributes = csEntry.AnchorAttributes.ToDictionary(
                aa => aa.Name,
                aa => aa,
                StringComparer.OrdinalIgnoreCase
            );
            var attributeChanges = csEntry.AttributeChanges.ToDictionary(
                ach => ach.Name,
                ach => ach,
                StringComparer.OrdinalIgnoreCase
            );

            if (attributeName.Equals("IDMObjectId") && anchorAttributes.TryGetValue("IDMObjectId", out var anchorAttribute))
            {
                return anchorAttribute.Value.ToString();
            }
            
            return attributeChanges.TryGetValue(attributeName, out var attributeChange) ? attributeChange.ValueChanges[0].Value : null;
        }
    }
}