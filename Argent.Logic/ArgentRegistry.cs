using Argent.Contracts;
using System;
using System.Collections.Generic;

namespace Argent.Logic
{
    public class ArgentRegistry : IComponentRegistry
    {
        private readonly Dictionary<string, Type> _mappings = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string typeName, Type componentType)
            => _mappings[typeName] = componentType;

        public Type Resolve(string typeName)
        {
            if (_mappings.TryGetValue(typeName, out var type))
                return type;

            throw new InvalidOperationException($"Component type '{typeName}' is not registered.");
        }

        public IEnumerable<string> GetRegisteredTypes() => _mappings.Keys;
    }
}
