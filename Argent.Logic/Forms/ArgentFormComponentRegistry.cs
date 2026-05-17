using Argent.Contracts.Forms;
using System;
using System.Collections.Generic;

namespace Argent.Runtime.Forms
{
    public class ArgentFormComponentRegistry : IFormComponentRegistry
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
