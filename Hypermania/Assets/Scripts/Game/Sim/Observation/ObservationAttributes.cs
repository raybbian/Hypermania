using System;

namespace Game.Sim.Observation
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class NonObservableAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class ObservationFieldAttribute : Attribute
    {
        public string Category { get; }

        public ObservationFieldAttribute(string category = null)
        {
            Category = category;
        }
    }
}
