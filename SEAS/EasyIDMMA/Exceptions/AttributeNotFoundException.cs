using System;

namespace EasyIDMMA.Exceptions
{
    public class AttributeNotFoundException : Exception
    {
        public AttributeNotFoundException(string message)
            : base(message) { }
    }
}