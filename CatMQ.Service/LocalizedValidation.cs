using System.ComponentModel.DataAnnotations;

namespace CatMQ.Service
{
    /// <summary>
    /// Attributes can only hold constants, so these build their (localized) message when it is needed instead.
    /// The display name is the same phrase that the page uses for the label of the field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LocalizedRequiredAttribute(string displayName)
        : RequiredAttribute
    {
        public override string FormatErrorMessage(string name)
            => Localize("{0} is required.", Localize(displayName));
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LocalizedRangeAttribute(string displayName, double minimum, double maximum)
        : RangeAttribute(minimum, maximum)
    {
        public override string FormatErrorMessage(string name)
            => Localize("{0} must be between {1:#,0.###} and {2:#,0.###}.", Localize(displayName), minimum, maximum);
    }
}
