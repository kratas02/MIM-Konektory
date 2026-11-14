using System;

namespace EasyIDMMA.Exceptions
{
    public class AnchorAttributeNotPresentException : Exception
    {
        public AnchorAttributeNotPresentException(string message)
            : base(message) { }
    }
}